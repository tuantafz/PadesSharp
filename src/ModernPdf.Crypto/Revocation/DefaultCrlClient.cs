// Original implementation based on public standards, no code copied from iText 5/7.
// Reference: RFC 5280 §5 — Certificate Revocation Lists (CRL)

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModernPdf.Abstractions.Revocation;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace ModernPdf.Crypto.Revocation;

/// <summary>
/// Default CRL client.  Downloads a CRL from a CRL Distribution Point (CDP)
/// URL (from the certificate's extension or caller-supplied), parses it, and
/// checks whether the certificate is listed as revoked.
/// </summary>
public sealed class DefaultCrlClient : ICrlClient
{
    // CRL Distribution Points extension OID (RFC 5280 §4.2.1.13)
    private static readonly string CdpOid = "2.5.29.31";

    private readonly HttpClient               _http;
    private readonly ILogger<DefaultCrlClient> _logger;
    private readonly TimeSpan                 _timeout;

    /// <param name="httpClient">Caller-managed HttpClient.</param>
    /// <param name="timeout">Per-download timeout. Default: 30 s.</param>
    /// <param name="logger">Optional logger.</param>
    public DefaultCrlClient(
        HttpClient httpClient,
        TimeSpan? timeout = null,
        ILogger<DefaultCrlClient>? logger = null)
    {
        _http    = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        _logger  = logger  ?? NullLogger<DefaultCrlClient>.Instance;
    }

    /// <inheritdoc/>
    public async Task<CrlResult> CheckRevocationAsync(
        X509Certificate2 certificate,
        string? crlUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (certificate is null) throw new ArgumentNullException(nameof(certificate));

        // Resolve CRL URL(s).
        IReadOnlyList<string> urls;
        if (!string.IsNullOrWhiteSpace(crlUrl))
        {
            urls = new[] { crlUrl };
        }
        else
        {
            urls = ExtractCdpUrls(certificate);
            if (urls.Count == 0)
            {
                _logger.LogWarning("Certificate {Serial} has no CDP URLs.", certificate.SerialNumber);
                return Unavailable("No CRL distribution point URL found in certificate.");
            }
        }

        // Try each URL until one succeeds.
        Exception? lastEx = null;
        foreach (string url in urls)
        {
            _logger.LogDebug("CRL check for {Serial} → {Url}", certificate.SerialNumber, url);
            try
            {
                byte[] crlBytes = await DownloadCrlAsync(url, cancellationToken).ConfigureAwait(false);
                return CheckCrl(crlBytes, certificate, url);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download or parse CRL from {Url}.", url);
                lastEx = ex;
            }
        }

        return Unavailable($"All CRL URLs failed. Last error: {lastEx?.Message}");
    }

    // -----------------------------------------------------------------------
    // CRL download
    // -----------------------------------------------------------------------

    private async Task<byte[]> DownloadCrlAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        using var response = await _http.GetAsync(url, cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // CRL parsing and revocation check
    // -----------------------------------------------------------------------

    private CrlResult CheckCrl(byte[] crlBytes, X509Certificate2 certificate, string url)
    {
        X509Crl crl;
        try
        {
            crl = new X509CrlParser().ReadCrl(crlBytes);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to parse CRL: " + ex.Message, ex);
        }

        DateTimeOffset? thisUpdate = new DateTimeOffset(crl.ThisUpdate, TimeSpan.Zero);
        DateTimeOffset? nextUpdate = crl.NextUpdate.HasValue
            ? new DateTimeOffset(crl.NextUpdate.Value, TimeSpan.Zero)
            : null;

        var serialNumber = new BigInteger(certificate.SerialNumber, 16);
        X509CrlEntry? entry = crl.GetRevokedCertificate(serialNumber);

        if (entry is null)
        {
            // Not present in the CRL → Good.
            return new CrlResult
            {
                Status     = RevocationStatus.Good,
                ThisUpdate = thisUpdate,
                NextUpdate = nextUpdate,
                CrlBytes   = crlBytes,
                CrlUrl     = url,
            };
        }

        // Certificate is revoked.
        string? reason     = GetReasonString(entry);
        var revocationDate = new DateTimeOffset(entry.RevocationDate, TimeSpan.Zero);

        _logger.LogWarning("Certificate {Serial} is revoked per CRL at {Url}. Time: {Time}",
            certificate.SerialNumber, url, revocationDate);

        return new CrlResult
        {
            Status           = RevocationStatus.Revoked,
            ThisUpdate       = thisUpdate,
            NextUpdate       = nextUpdate,
            RevocationTime   = revocationDate,
            RevocationReason = reason,
            CrlBytes         = crlBytes,
            CrlUrl           = url,
        };
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Extracts http/https CRL Distribution Point URIs from the certificate's CDP extension.
    /// </summary>
    private static IReadOnlyList<string> ExtractCdpUrls(X509Certificate2 certificate)
    {
        var bcCert = new X509CertificateParser().ReadCertificate(certificate.RawData);
        var cdpExt = bcCert.GetExtensionValue(new DerObjectIdentifier(CdpOid));
        if (cdpExt is null) return Array.Empty<string>();

        var urls   = new List<string>();
        var cdpSeq = (Asn1Sequence)Asn1Object.FromByteArray(cdpExt.GetOctets());

        foreach (Asn1Encodable item in cdpSeq)
        {
            var dp = DistributionPoint.GetInstance(item);
            if (dp.DistributionPointName?.Type != DistributionPointName.FullName)
                continue;

            var gns = GeneralNames.GetInstance(dp.DistributionPointName.Name);
            foreach (GeneralName gn in gns.GetNames())
            {
                if (gn.TagNo == GeneralName.UniformResourceIdentifier)
                {
                    string uri = ((IAsn1String)gn.Name).GetString();
                    if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        urls.Add(uri);
                }
            }
        }

        return urls;
    }

    private static string? GetReasonString(X509CrlEntry entry)
    {
        var reasonExt = entry.GetExtensionValue(new DerObjectIdentifier("2.5.29.21"));
        if (reasonExt is null) return null;

        try
        {
            var reasonValue = (DerEnumerated)Asn1Object.FromByteArray(reasonExt.GetOctets());
            return reasonValue.IntValueExact switch
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
                _  => reasonValue.IntValueExact.ToString(),
            };
        }
        catch
        {
            return null;
        }
    }

    private static CrlResult Unavailable(string message) =>
        new CrlResult { Status = RevocationStatus.Unavailable, ErrorMessage = message };
}
