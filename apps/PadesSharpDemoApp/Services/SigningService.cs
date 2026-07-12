using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModernPdf.Abstractions.Appearance;
using ModernPdf.Abstractions.Signing;
using ModernPdf.Abstractions.Tsa;
using ModernPdf.Appearance;
using ModernPdf.Crypto;
using ModernPdf.Crypto.Tsa;
using ModernPdf.Pades;
using ModernPdf.Signing;
using PadesSharpDemoApp.Models;

namespace PadesSharpDemoApp.Services;

public sealed class SigningService
{
    private readonly ILogger _log;

    public SigningService(ILogger log) => _log = log;

    public async Task<bool> SignAsync(
        BatchSignJob job,
        X509Certificate2 certificate,
        IReadOnlyList<X509Certificate2> chain,
        SigningOptions opts,
        AppearanceOptions appearance,
        CancellationToken ct)
    {
        try
        {
            _log.LogInformation(AppLocale.Current.LogSignStart(job.InputPath, job.OutputPath));
            job.Status = JobStatus.Running;

            // ── 1. Chuẩn bị các thành phần ký ──────────────────────────────
            var digestService    = new DefaultDigestService();
            var cmsSigner        = new BouncyCastleCmsSigner(digestService);
            var appearanceBuilder = new DefaultPdfSignatureAppearanceBuilder();
            var signatureProvider = new RsaSoftwareSignatureProvider(certificate);

            // ── 2. TSA client ────────────────────────────────────────────────
            ITsaClient? tsaClient = null;
            HttpClient? httpClient = null;
            if (opts.Level is SigningLevel.Tsa or SigningLevel.LtvDss
                && !string.IsNullOrWhiteSpace(opts.TsaUrl))
            {
                httpClient = BuildHttpClient(opts.TsaUrl, opts.TsaUser, opts.TsaPass);
                var tsaOptions = new TsaClientOptions { TsaUrl = opts.TsaUrl };
                tsaClient = new Rfc3161TsaClient(tsaOptions, httpClient, digestService);
            }

            // ── 3. Appearance request ────────────────────────────────────────
            PdfSignatureAppearanceRequest? appearanceReq = null;
            if (appearance.Enabled)
            {
                appearanceReq = new PdfSignatureAppearanceRequest
                {
                    SignerName     = certificate.GetNameInfo(X509NameType.SimpleName, false),
                    Reason         = opts.Reason,
                    Location       = opts.Location,
                    PageNumber     = appearance.PageNumber,
                    Width          = appearance.Width,
                    Height         = appearance.Height,
                    ShowDate       = appearance.ShowDate,
                    ShowReason     = appearance.ShowReason,
                    ShowLocation   = appearance.ShowLocation,
                    LogoImageBytes = appearance.LogoImageBytes,
                    Rectangle = new PdfSignatureRectangle
                    {
                        X      = appearance.X,
                        Y      = appearance.Y,
                        Width  = appearance.Width,
                        Height = appearance.Height
                    }
                };
            }

            // ── 4. Ký PDF ────────────────────────────────────────────────────
            var engine = new PdfSigningEngine(cmsSigner, digestService, appearanceBuilder);

            byte[] inputBytes  = await File.ReadAllBytesAsync(job.InputPath, ct);
            byte[] outputBytes;

            using (var inputStream  = new MemoryStream(inputBytes))
            using (var outputStream = new MemoryStream())
            {
                var request = new PdfSignRequest
                {
                    InputPdf             = inputStream,
                    OutputPdf            = outputStream,
                    SignatureProvider    = signatureProvider,
                    Certificate          = certificate,
                    CertificateChain     = chain,
                    Reason               = opts.Reason,
                    Location             = opts.Location,
                    SubFilter            = opts.SubFilter,
                    SignatureContentSize = opts.SignatureContentSize,
                    TsaClient            = tsaClient,
                    Appearance           = appearanceReq
                };

                var result = await engine.SignAsync(request, ct);

                if (!result.Success)
                {
                    job.Status       = JobStatus.Failed;
                    job.ErrorMessage = AppLocale.Current.ErrEngineReturnedFalse;
                    _log.LogError(AppLocale.Current.LogSignFailed(job.InputPath));
                    httpClient?.Dispose();
                    return false;
                }

                outputBytes = outputStream.ToArray();

                // ── 5. LTV/DSS ────────────────────────────────────────────────
                if (opts.Level == SigningLevel.LtvDss)
                {
                    _log.LogInformation(AppLocale.Current.LogEmbedDss(job.InputPath));
                    var collector = new LtvDataCollector();
                    var dssData   = await collector.CollectAsync(chain, result.SignatureCmsBytes, ct);

                    var dssWriter = new DssIncrementalWriter();
                    outputBytes   = dssWriter.AppendDss(outputBytes, dssData);
                }
            }

            // ── 6. Ghi file đầu ra ───────────────────────────────────────────
            Directory.CreateDirectory(Path.GetDirectoryName(job.OutputPath)!);
            await File.WriteAllBytesAsync(job.OutputPath, outputBytes, ct);

            signatureProvider.Dispose();
            httpClient?.Dispose();

            job.Status = JobStatus.Success;
            _log.LogInformation(AppLocale.Current.LogSignOk(job.InputPath, opts.Level.ToString()));
            return true;
        }
        catch (OperationCanceledException)
        {
            job.Status       = JobStatus.Failed;
            job.ErrorMessage = AppLocale.Current.ErrJobCancelled;
            _log.LogWarning(AppLocale.Current.LogSignCancelled(job.InputPath));
            return false;
        }
        catch (Exception ex)
        {
            job.Status       = JobStatus.Failed;
            job.ErrorMessage = ex.Message;
            _log.LogError(ex, AppLocale.Current.LogSignError(job.InputPath));
            return false;
        }
    }

    private static HttpClient BuildHttpClient(string tsaUrl, string? user, string? pass)
    {
        var handler = new HttpClientHandler();
        var client  = new HttpClient(handler) { BaseAddress = new Uri(tsaUrl) };
        client.Timeout = TimeSpan.FromSeconds(30);

        if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass))
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{user}:{pass}"));
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);
        }

        return client;
    }

    public static string BuildOutputPath(string inputPath, string? outputFolder,
        bool sameFolder, SigningLevel level)
    {
        string dir  = sameFolder
            ? Path.GetDirectoryName(inputPath)!
            : outputFolder ?? Path.GetDirectoryName(inputPath)!;
        string name = Path.GetFileNameWithoutExtension(inputPath);
        string suffix = level switch
        {
            SigningLevel.Tsa    => "_signed_tsa",
            SigningLevel.LtvDss => "_signed_ltv",
            _                   => "_signed"
        };
        return Path.Combine(dir, name + suffix + ".pdf");
    }
}
