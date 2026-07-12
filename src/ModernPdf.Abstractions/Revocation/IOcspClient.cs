// Original implementation based on public standards, no code copied from iText 5/7.
// Reference: RFC 6960 — X.509 Internet PKI Online Certificate Status Protocol (OCSP)

using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace ModernPdf.Abstractions.Revocation;

/// <summary>
/// Checks certificate revocation status via OCSP (RFC 6960).
/// </summary>
public interface IOcspClient
{
    /// <summary>
    /// Queries the OCSP responder for the revocation status of
    /// <paramref name="certificate"/>.
    /// </summary>
    /// <param name="certificate">The end-entity (or intermediate) certificate to check.</param>
    /// <param name="issuer">
    /// The issuing CA certificate, required for building the OCSP CertificateID.
    /// </param>
    /// <param name="ocspUrl">
    /// Override URL for the OCSP responder. When <c>null</c>, the URL is extracted
    /// from the certificate's Authority Information Access (AIA) extension.
    /// </param>
    /// <param name="cancellationToken">Cancellation support.</param>
    Task<OcspResult> CheckRevocationAsync(
        X509Certificate2 certificate,
        X509Certificate2 issuer,
        string? ocspUrl = null,
        CancellationToken cancellationToken = default);
}
