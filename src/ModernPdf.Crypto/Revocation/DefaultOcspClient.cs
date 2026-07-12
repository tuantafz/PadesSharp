// Original implementation based on public standards, no code copied from iText 5/7.
// Reference: RFC 6960 — X.509 Internet PKI Online Certificate Status Protocol (OCSP)

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModernPdf.Abstractions.Revocation;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Ocsp;
using Org.BouncyCastle.Asn1.Oiw;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Ocsp;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace ModernPdf.Crypto.Revocation;

/// <summary>
/// Default OCSP client.  Builds an OCSP request per RFC 6960, POSTs it to the
/// responder (URL from AIA or caller-supplied), and parses the response.
/// </summary>
public sealed class DefaultOcspClient : IOcspClient
{
    // Content-Type / Accept headers defined by RFC 6960 §A.1
    private const string OcspRequestMime  = "application/ocsp-request";
    private const string OcspResponseMime = "application/ocsp-response";

    // id-ad-ocsp access method OID
    private static readonly string OcspAccessMethodOid = "1.3.6.1.5.5.7.48.1";
    // Authority Information Access extension OID
    private static readonly string AiaOid = "1.3.6.1.5.5.7.1.1";

    private readonly HttpClient                _http;
    private readonly ILogger<DefaultOcspClient> _logger;
    private readonly TimeSpan                  _timeout;

    /// <param name="httpClient">Caller-managed HttpClient (DI / IHttpClientFactory).</param>
    /// <param name="timeout">Per-request timeout. Default: 15 s.</param>
    /// <param name="logger">Optional logger.</param>
    public DefaultOcspClient(
        HttpClient httpClient,
        TimeSpan? timeout = null,
        ILogger<DefaultOcspClient>? logger = null)
    {
        _http    = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _timeout = timeout ?? TimeSpan.FromSeconds(15);
        _logger  = logger  ?? NullLogger<DefaultOcspClient>.Instance;
    }

    /// <inheritdoc/>
    public async Task<OcspResult> CheckRevocationAsync(
        X509Certificate2 certificate,
        X509Certificate2 issuer,
        string? ocspUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (certificate is null) throw new ArgumentNullException(nameof(certificate));
        if (issuer is null)      throw new ArgumentNullException(nameof(issuer));

        // Resolve OCSP URL.
        string? url = ocspUrl ?? ExtractOcspUrl(certificate);
        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.LogWarning("Certificate {Serial} has no OCSP URL and none was provided.",
                certificate.SerialNumber);
            return Unavailable("No OCSP responder URL found in certificate AIA extension.");
        }

        _logger.LogDebug("OCSP check for {Serial} → {Url}", certificate.SerialNumber, url);

        // Build the OCSP request.
        byte[] reqBytes;
        CertificateID certId;
        try
        {
            (reqBytes, certId) = BuildOcspRequest(certificate, issuer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build OCSP request.");
            return Unavailable("Failed to build OCSP request: " + ex.Message);
        }

        // POST and receive response.
        byte[] respBytes;
        try
        {
            respBytes = await PostOcspAsync(url, reqBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OCSP HTTP request to {Url} failed.", url);
            return Unavailable($"OCSP request to '{url}' failed: {ex.Message}");
        }

        // Parse and interpret response.
        return ParseOcspResponse(respBytes, certId, url);
    }

    // -----------------------------------------------------------------------
    // OCSP request builder
    // -----------------------------------------------------------------------

    private static (byte[] requestBytes, CertificateID certId) BuildOcspRequest(
        X509Certificate2 certificate,
        X509Certificate2 issuer)
    {
        var bcIssuer = new X509CertificateParser().ReadCertificate(issuer.RawData);
        var hashAlg  = new AlgorithmIdentifier(OiwObjectIdentifiers.IdSha1);
        var certId   = new CertificateID(hashAlg, bcIssuer,
                           new BigInteger(certificate.SerialNumber, 16));

        var gen = new OcspReqGenerator();
        gen.AddRequest(certId);
        OcspReq req = gen.Generate();
        return (req.GetEncoded(), certId);
    }

    // -----------------------------------------------------------------------
    // HTTP POST
    // -----------------------------------------------------------------------

    private async Task<byte[]> PostOcspAsync(
        string url,
        byte[] requestBytes,
        CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(requestBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(OcspRequestMime);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        using var response = await _http.PostAsync(url, content, cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // OCSP response parser
    // -----------------------------------------------------------------------

    private OcspResult ParseOcspResponse(byte[] respBytes, CertificateID certId, string url)
    {
        OcspResp ocspResp;
        try
        {
            ocspResp = new OcspResp(respBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse OCSP response.");
            return Unavailable("Failed to parse OCSP response: " + ex.Message);
        }

        // OcspRespStatus: 0 = successful
        if (ocspResp.Status != OcspRespStatus.Successful)
        {
            string statusStr = ocspResp.Status switch
            {
                OcspRespStatus.MalformedRequest    => "malformedRequest",
                OcspRespStatus.InternalError       => "internalError",
                OcspRespStatus.TryLater            => "tryLater",
                OcspRespStatus.SigRequired         => "sigRequired",
                OcspRespStatus.Unauthorized        => "unauthorized",
                _                                  => ocspResp.Status.ToString(),
            };
            _logger.LogWarning("OCSP response status: {Status}", statusStr);
            return new OcspResult
            {
                Status            = RevocationStatus.Unavailable,
                ErrorMessage      = $"OCSP responder returned status: {statusStr}",
                OcspResponseBytes = respBytes,
                ResponderUrl      = url,
            };
        }

        BasicOcspResp basic;
        try
        {
            basic = (BasicOcspResp)ocspResp.GetResponseObject();
        }
        catch (Exception ex)
        {
            return Unavailable("Failed to extract BasicOCSPResponse: " + ex.Message);
        }

        // Find the SingleResponse matching our certId.
        SingleResp? singleResp = null;
        foreach (var sr in basic.Responses)
        {
            if (sr.GetCertID().SerialNumber.Equals(certId.SerialNumber))
            {
                singleResp = sr;
                break;
            }
        }

        if (singleResp is null)
        {
            _logger.LogWarning("OCSP response did not contain an entry for the queried certificate.");
            return new OcspResult
            {
                Status            = RevocationStatus.Unknown,
                ErrorMessage      = "OCSP response has no entry for the queried certificate.",
                OcspResponseBytes = respBytes,
                ResponderUrl      = url,
            };
        }

        // Map certStatus to RevocationStatus.
        object certStatus = singleResp.GetCertStatus();

        if (certStatus == CertificateStatus.Good)
        {
            return new OcspResult
            {
                Status            = RevocationStatus.Good,
                ThisUpdate        = ToDateTimeOffset(singleResp.ThisUpdate),
                NextUpdate        = singleResp.NextUpdate.HasValue
                                    ? ToDateTimeOffset(singleResp.NextUpdate.Value) : null,
                OcspResponseBytes = respBytes,
                ResponderUrl      = url,
            };
        }

        if (certStatus is RevokedStatus revoked)
        {
            string? reason = revoked.HasRevocationReason
                ? GetReasonString(revoked.RevocationReason)
                : null;
            return new OcspResult
            {
                Status            = RevocationStatus.Revoked,
                ThisUpdate        = ToDateTimeOffset(singleResp.ThisUpdate),
                NextUpdate        = singleResp.NextUpdate.HasValue
                                    ? ToDateTimeOffset(singleResp.NextUpdate.Value) : null,
                RevocationTime    = ToDateTimeOffset(revoked.RevocationTime),
                RevocationReason  = reason,
                OcspResponseBytes = respBytes,
                ResponderUrl      = url,
            };
        }

        // UnknownStatus or anything else
        return new OcspResult
        {
            Status            = RevocationStatus.Unknown,
            ThisUpdate        = ToDateTimeOffset(singleResp.ThisUpdate),
            OcspResponseBytes = respBytes,
            ResponderUrl      = url,
        };
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Extracts the OCSP responder URL from the certificate's AIA extension.
    /// </summary>
    private static string? ExtractOcspUrl(X509Certificate2 certificate)
    {
        var bcCert  = new X509CertificateParser().ReadCertificate(certificate.RawData);
        var aiaExt  = bcCert.GetExtensionValue(new DerObjectIdentifier(AiaOid));
        if (aiaExt is null) return null;

        var aiaSeq = (Asn1Sequence)Asn1Object.FromByteArray(aiaExt.GetOctets());
        foreach (Asn1Encodable item in aiaSeq)
        {
            var accessDesc = AccessDescription.GetInstance(item);
            if (accessDesc.AccessMethod.Id == OcspAccessMethodOid)
            {
                var gn = accessDesc.AccessLocation;
                if (gn.TagNo == GeneralName.UniformResourceIdentifier)
                    return ((IAsn1String)gn.Name).GetString();
            }
        }
        return null;
    }

    private static OcspResult Unavailable(string message) =>
        new OcspResult { Status = RevocationStatus.Unavailable, ErrorMessage = message };

    private static DateTimeOffset ToDateTimeOffset(DateTime dt) =>
        new DateTimeOffset(dt, TimeSpan.Zero);

    private static string GetReasonString(int reason) => reason switch
    {
        0  => "unspecified",
        1  => "keyCompromise",
        2  => "cACompromise",
        3  => "affiliationChanged",
        4  => "superseded",
        5  => "cessationOfOperation",
        6  => "certificateHold",
        8  => "removeFromCRL",
        9  => "privilegeWithdrawn",
        10 => "aACompromise",
        _  => reason.ToString(),
    };
}
