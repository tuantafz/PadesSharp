// Original implementation based on public standards, no code copied from iText 5/7.
// Reference: ISO 32000-1 §12.8, ETSI EN 319 102-1 (PAdES)

using System.Threading;
using System.Threading.Tasks;

namespace ModernPdf.Abstractions.Signing;

/// <summary>
/// Orchestrates the complete PDF signing flow: ByteRange calculation,
/// digest computation, CMS creation, and /Contents injection.
/// </summary>
public interface IPdfSigner
{
    /// <summary>
    /// Signs the PDF contained in <paramref name="request"/> and writes
    /// the signed output into <paramref name="request"/>.<see cref="PdfSignRequest.OutputPdf"/>.
    /// </summary>
    Task<PdfSignResult> SignAsync(PdfSignRequest request, CancellationToken cancellationToken = default);
}
