// Original implementation based on public standards, no code copied from iText 5/7.
// Reference: RFC 3161 — Internet X.509 PKI Time-Stamp Protocol (TSP)

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModernPdf.Abstractions.Crypto;
using ModernPdf.Abstractions.Tsa;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tsp;

namespace ModernPdf.Crypto.Tsa;

/// <summary>
/// RFC 3161-compliant TSA client.
/// Builds a <c>TimeStampReq</c> using BouncyCastle, POSTs it to the configured
/// TSA URL, validates the <c>TimeStampResp</c> and returns the
/// DER-encoded <c>TimeStampToken</c>.
/// </summary>
public sealed class Rfc3161TsaClient : ITsaClient
{
    // MIME types (RFC 3161 §3.4)
    private const string TsQueryMime  = "application/timestamp-query";
    private const string TsReplyMime  = "application/timestamp-reply";

    private readonly TsaClientOptions _options;
    private readonly HttpClient       _http;
    private readonly IDigestService   _digestService;
    private readonly ILogger<Rfc3161TsaClient> _logger;

    /// <summary>
    /// Initialises the client with a provided <see cref="HttpClient"/>.
    /// The caller (or DI) is responsible for the HttpClient lifetime.
    /// </summary>
    public Rfc3161TsaClient(
        TsaClientOptions options,
        HttpClient httpClient,
        IDigestService digestService,
        ILogger<Rfc3161TsaClient>? logger = null)
    {
        _options       = options       ?? throw new ArgumentNullException(nameof(options));
        _http          = httpClient    ?? throw new ArgumentNullException(nameof(httpClient));
        _digestService = digestService ?? throw new ArgumentNullException(nameof(digestService));
        _logger        = logger        ?? NullLogger<Rfc3161TsaClient>.Instance;

        if (string.IsNullOrWhiteSpace(options.TsaUrl))
            throw new ArgumentException("TsaClientOptions.TsaUrl must not be empty.", nameof(options));
    }

    /// <inheritdoc/>
    public async Task<TsaTokenResult> GetTimestampAsync(
        byte[] signatureBytes,
        CancellationToken cancellationToken = default)
    {
        if (signatureBytes is null || signatureBytes.Length == 0)
            throw new ArgumentException("signatureBytes must not be empty.", nameof(signatureBytes));

        // 1. Hash the signature bytes using the configured digest algorithm.
        byte[] messageImprint = _digestService.ComputeDigest(signatureBytes, _options.DigestAlgorithm);
        string digestOid      = _digestService.GetDigestOid(_options.DigestAlgorithm);

        // 2. Generate nonce (optional but recommended for replay protection).
        BigInteger? nonce = null;
        if (_options.UseNonce)
            nonce = new BigInteger(64, new SecureRandom());

        // 3. Build the TimeStampReq.
        byte[] tsqBytes = BuildTsq(messageImprint, digestOid, nonce);

        // 4. POST to TSA with retry / exponential back-off.
        byte[] tsrBytes = await PostWithRetryAsync(tsqBytes, cancellationToken)
            .ConfigureAwait(false);

        // 5. Parse and validate the TimeStampResp.
        return ParseTsr(tsrBytes, messageImprint, digestOid, nonce);
    }

    // -----------------------------------------------------------------------
    // TSQ builder — uses BouncyCastle TimeStampRequestGenerator
    // -----------------------------------------------------------------------

    private byte[] BuildTsq(byte[] messageImprint, string digestOid, BigInteger? nonce)
    {
        var gen = new TimeStampRequestGenerator();
        gen.SetCertReq(_options.RequestCertificate);

        TimeStampRequest req = gen.Generate(digestOid, messageImprint, nonce);
        return req.GetEncoded();
    }

    // -----------------------------------------------------------------------
    // HTTP POST with retry / exponential back-off
    // -----------------------------------------------------------------------

    private async Task<byte[]> PostWithRetryAsync(
        byte[] tsqBytes,
        CancellationToken cancellationToken)
    {
        int maxAttempts  = Math.Max(1, _options.MaxRetries);
        Exception? last  = null;
        TimeSpan   delay = _options.RetryBaseDelay;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await PostOnceAsync(tsqBytes, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                last = ex;
                _logger.LogWarning(ex,
                    "TSA request attempt {Attempt}/{Max} failed: {Message}",
                    attempt, maxAttempts, ex.Message);

                if (attempt < maxAttempts)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
                }
            }
        }

        throw new InvalidOperationException(
            $"TSA request failed after {maxAttempts} attempt(s). Last error: {last?.Message}", last);
    }

    private async Task<byte[]> PostOnceAsync(
        byte[] tsqBytes,
        CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(tsqBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(TsQueryMime);

        using var cts = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_options.Timeout);

        using var response = await _http
            .PostAsync(_options.TsaUrl, content, cts.Token)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        // Verify Content-Type (some TSAs use application/octet-stream).
        string? ct = response.Content.Headers.ContentType?.MediaType;
        if (ct != null && ct != TsReplyMime && ct != "application/octet-stream")
            _logger.LogWarning(
                "TSA replied with unexpected Content-Type '{ContentType}'; attempting to parse anyway.", ct);

        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // TSR parser — uses BouncyCastle TimeStampResponse
    // -----------------------------------------------------------------------

    private TsaTokenResult ParseTsr(
        byte[] tsrBytes,
        byte[] expectedMessageImprint,
        string expectedDigestOid,
        BigInteger? expectedNonce)
    {
        TimeStampResponse tsr;
        try
        {
            tsr = new TimeStampResponse(tsrBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse TSA response.");
            return Fail("Failed to parse TSA response: " + ex.Message);
        }

        // PKIStatusInfo.status: 0 = granted, 1 = grantedWithMods
        int status = tsr.Status;
        if (status != 0 && status != 1)
        {
            string statusText = tsr.GetStatusString() ?? status.ToString();
            _logger.LogWarning("TSA returned non-granted status {Status}: {Text}", status, statusText);
            return Fail($"TSA returned status {status}: {statusText}");
        }

        TimeStampToken tst;
        try
        {
            tst = tsr.TimeStampToken;
            if (tst is null)
                return Fail("TSA response contains no TimeStampToken.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract TimeStampToken from TSA response.");
            return Fail("Failed to extract TimeStampToken: " + ex.Message);
        }

        // Validate nonce echo.
        if (expectedNonce != null)
        {
            BigInteger? returnedNonce = tst.TimeStampInfo.Nonce;
            if (returnedNonce == null || !returnedNonce.Equals(expectedNonce))
            {
                _logger.LogWarning("TSA nonce mismatch. Expected {Expected}, got {Got}",
                    expectedNonce, returnedNonce);
                return Fail("TSA nonce mismatch — possible replay attack.");
            }
        }

        // Validate message imprint algorithm and hash.
        var info = tst.TimeStampInfo;
        if (info.MessageImprintAlgOid != expectedDigestOid)
        {
            _logger.LogWarning("TSA imprint OID mismatch. Expected {E}, got {G}",
                expectedDigestOid, info.MessageImprintAlgOid);
            return Fail(
                $"TSA imprint algorithm mismatch: expected {expectedDigestOid}, got {info.MessageImprintAlgOid}.");
        }

        byte[] returnedImprint = info.GetMessageImprintDigest();
        if (!ConstantTimeEquals(returnedImprint, expectedMessageImprint))
        {
            _logger.LogWarning("TSA message imprint hash mismatch.");
            return Fail("TSA message imprint hash does not match the submitted hash.");
        }

        DateTimeOffset genTime;
        try
        {
            genTime = new DateTimeOffset(info.GenTime, TimeSpan.Zero);
        }
        catch
        {
            genTime = DateTimeOffset.UtcNow; // fallback; should not happen
        }

        return new TsaTokenResult
        {
            Success       = true,
            TokenBytes    = tst.GetEncoded(),
            TimestampTime = genTime,
        };
    }

    // -----------------------------------------------------------------------

    private static TsaTokenResult Fail(string message) =>
        new TsaTokenResult { Success = false, ErrorMessage = message };

    /// <summary>
    /// Constant-time byte array comparison to prevent timing side-channels.
    /// </summary>
    private static bool ConstantTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
