// Original implementation based on public standards, no code copied from iText 5/7.
// Reference: RFC 5280 §5 — Certificate Revocation Lists (CRL)

using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace ModernPdf.Abstractions.Revocation;

/// <summary>
/// Checks certificate revocation status via CRL (RFC 5280).
/// </summary>
public interface ICrlClient
{
    /// <summary>
    /// Downloads and checks the CRL for the revocation status of
    /// <paramref name="certificate"/>.
    /// </summary>
    /// <param name="certificate">The certificate to check.</param>
    /// <param name="crlUrl">
    /// Override URL for the CRL. When <c>null</c>, URLs are extracted from the
    /// certificate's CRL Distribution Points (CDP) extension.
    /// </param>
    /// <param name="cancellationToken">Cancellation support.</param>
    Task<CrlResult> CheckRevocationAsync(
        X509Certificate2 certificate,
        string? crlUrl = null,
        CancellationToken cancellationToken = default);
}
