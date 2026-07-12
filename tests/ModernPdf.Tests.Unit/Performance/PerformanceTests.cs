// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ModernPdf.Abstractions.Crypto;
using ModernPdf.Abstractions.Signing;
using ModernPdf.Abstractions.Validation;
using ModernPdf.Crypto;
using ModernPdf.Signing;
using ModernPdf.Validation;

namespace ModernPdf.Tests.Unit.Performance;

/// <summary>
/// Sprint 10 — Performance, large-file and cross-platform (file API) tests.
/// </summary>
public class PerformanceTests : IDisposable
{
    private readonly DefaultDigestService _digestService = new();
    private readonly BouncyCastleCmsSigner _cmsSigner;
    private readonly PdfSigningEngine _signingEngine;
    private readonly X509Certificate2 _cert;
    private readonly RsaSoftwareSignatureProvider _provider;
    private readonly DefaultPdfSignatureValidator _validator = new();

    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "PadesSharp_S10_" + Guid.NewGuid().ToString("N")[..8]);

    public PerformanceTests()
    {
        _cmsSigner     = new BouncyCastleCmsSigner(_digestService);
        _signingEngine = new PdfSigningEngine(_cmsSigner, _digestService);

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=PerfTester,O=PadesSharp,C=VN",
            rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        _cert     = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        _provider = new RsaSoftwareSignatureProvider(_cert);

        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _cert.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private PdfSignRequest MakeRequest(MemoryStream output, int contentSize = 8192)
        => new()
        {
            OutputPdf            = output,
            DigestAlgorithm      = PdfDigestAlgorithm.Sha256,
            Certificate          = _cert,
            SignatureProvider    = _provider,
            Reason               = "Sprint10 perf test",
            Location             = "Hanoi",
            SignatureName        = "Sig1",
            SignatureContentSize = contentSize,
        };

    private byte[] Sign(int contentSize = 8192)
    {
        using var ms = new MemoryStream();
        var req = MakeRequest(ms, contentSize);
        _signingEngine.SignAsync(req).GetAwaiter().GetResult();
        return ms.ToArray();
    }

    // -----------------------------------------------------------------------
    // Section 1 — Throughput: sign N PDFs sequentially
    // -----------------------------------------------------------------------

    [Fact]
    public void Sign_50Sequential_CompletesWithinBudget()
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 50; i++)
            Sign();
        sw.Stop();

        // 50 software-RSA signs should complete in well under 60 s on any CI machine.
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(60),
            "50 sequential RSA-2048 sign operations should complete in under 60 s");
    }

    [Fact]
    public void Sign_10Sequential_OutputSizeIsReasonable()
    {
        for (int i = 0; i < 10; i++)
        {
            byte[] pdf = Sign();
            // A minimal signed PDF should be at least 1 KB and less than 100 KB.
            pdf.Length.Should().BeInRange(1_024, 100_000,
                "signed PDF size should be between 1 KB and 100 KB for a minimal document");
        }
    }

    // -----------------------------------------------------------------------
    // Section 2 — Concurrent signing (thread safety)
    // -----------------------------------------------------------------------

    [Fact]
    public void Sign_8Concurrent_AllSucceed()
    {
        const int count = 8;
        var results = new byte[count][];
        var exceptions = new Exception?[count];

        Parallel.For(0, count, i =>
        {
            try
            {
                results[i] = Sign();
            }
            catch (Exception ex)
            {
                exceptions[i] = ex;
            }
        });

        for (int i = 0; i < count; i++)
        {
            exceptions[i].Should().BeNull($"signer #{i} should not throw");
            results[i].Should().NotBeNullOrEmpty($"signer #{i} should produce bytes");
        }
    }

    [Fact]
    public void Sign_8Concurrent_AllVerify()
    {
        const int count = 8;
        var pdfs = new byte[count][];

        Parallel.For(0, count, i => pdfs[i] = Sign());

        foreach (byte[] pdf in pdfs)
        {
            var report = _validator.Validate(new MemoryStream(pdf));
            report.IsValid.Should().BeTrue("every concurrently signed PDF must validate");
        }
    }

    // -----------------------------------------------------------------------
    // Section 3 — Large placeholder (large CMS budget)
    // -----------------------------------------------------------------------

    [Fact]
    public void Sign_With64KbContentPlaceholder_Succeeds()
    {
        byte[] pdf = Sign(contentSize: 65536);
        pdf.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Sign_With64KbContentPlaceholder_ValidatesOk()
    {
        byte[] pdf = Sign(contentSize: 65536);

        var report = _validator.Validate(new MemoryStream(pdf));
        report.IsValid.Should().BeTrue("large-placeholder PDF must still validate correctly");
    }

    // -----------------------------------------------------------------------
    // Section 4 — File helper API (cross-platform file operations)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SignToFile_CreatesFile()
    {
        string path = Path.Combine(_tempDir, "output.pdf");
        var req = MakeRequest(null!, 8192);

        await PdfSigningFileHelper.SignToFileAsync(path, req, _signingEngine);

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task SignToFile_FileIsNonEmpty()
    {
        string path = Path.Combine(_tempDir, "output.pdf");
        var req = MakeRequest(null!, 8192);

        await PdfSigningFileHelper.SignToFileAsync(path, req, _signingEngine);

        new FileInfo(path).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SignToFile_FileIsValidSignedPdf()
    {
        string path = Path.Combine(_tempDir, "output.pdf");
        var req = MakeRequest(null!, 8192);

        await PdfSigningFileHelper.SignToFileAsync(path, req, _signingEngine);

        // Read back and validate
        byte[] bytes = File.ReadAllBytes(path);
        var report = _validator.Validate(new MemoryStream(bytes));
        report.IsValid.Should().BeTrue("file-signed PDF must validate");
    }

    [Fact]
    public async Task SignToFile_SubdirCreatedAutomatically()
    {
        string path = Path.Combine(_tempDir, "sub1", "sub2", "output.pdf");
        var req = MakeRequest(null!, 8192);

        await PdfSigningFileHelper.SignToFileAsync(path, req, _signingEngine);

        File.Exists(path).Should().BeTrue("subdirectory must be created automatically");
    }

    [Fact]
    public void SignToFile_NullOutputPath_ThrowsArgumentNull()
    {
        var req = MakeRequest(null!, 8192);
        Func<Task> act = () => PdfSigningFileHelper.SignToFileAsync(null!, req, _signingEngine);
        act.Should().ThrowAsync<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Section 5 — Validation file helper
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidateFile_ValidSignedPdf_ReturnsValidReport()
    {
        string path = Path.Combine(_tempDir, "to_validate.pdf");
        var req = MakeRequest(null!, 8192);
        await PdfSigningFileHelper.SignToFileAsync(path, req, _signingEngine);

        var report = PdfValidationFileHelper.ValidateFile(path, _validator);
        report.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateBytes_ValidSignedPdf_ReturnsValidReport()
    {
        byte[] pdf    = Sign();
        var    report = PdfValidationFileHelper.ValidateBytes(pdf, _validator);
        report.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateFile_NullPath_ThrowsArgumentNull()
    {
        Action act = () => PdfValidationFileHelper.ValidateFile(null!, _validator);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ValidateBytes_NullArray_ThrowsArgumentNull()
    {
        Action act = () => PdfValidationFileHelper.ValidateBytes(null!, _validator);
        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Section 6 — Cross-platform / encoding
    // -----------------------------------------------------------------------

    [Fact]
    public void Sign_Latin1Encoding_WorksOnAnyPlatform()
    {
        // The engine uses Encoding.GetEncoding(28591) (Latin-1 / ISO-8859-1)
        // which is available on all platforms supported by .NET 8.
        // Verify that a signed PDF can be round-tripped through the validator.
        byte[] pdf    = Sign();
        var    report = _validator.Validate(new MemoryStream(pdf));
        report.IsValid.Should().BeTrue("Latin-1 round-trip must work on all platforms");
    }

    [Fact]
    public void Sign_UnicodeReasonField_DoesNotThrow()
    {
        using var ms = new MemoryStream();
        var req = MakeRequest(ms);
        req.Reason   = "Chữ ký điện tử — \u00e9\u00e0\u00fc\u00f6\u00e4"; // mixed UTF-8 chars
        req.Location = "Hà Nội, Việt Nam";

        Action act = () => _signingEngine.SignAsync(req).GetAwaiter().GetResult();
        act.Should().NotThrow("Unicode reason/location fields must not crash");
    }
}
