// Original implementation based on public standards, no code copied from iText 5/7.

using System.IO;

namespace ModernPdf.Abstractions.Validation
{
    /// <summary>
    /// Validates PDF digital signatures: document integrity, CMS cryptography,
    /// certificate chain, revocation and timestamps.
    /// </summary>
    public interface IPdfSignatureValidator
    {
        /// <summary>
        /// Validates all signature fields in <paramref name="pdfInput"/> and returns
        /// a per-signature report.
        /// </summary>
        /// <param name="pdfInput">Readable stream containing the signed PDF bytes.</param>
        /// <param name="options">
        /// Controls which checks are performed. Pass <c>null</c> for all defaults.
        /// </param>
        PdfValidationReport Validate(Stream pdfInput, PdfValidationOptions? options = null);
    }
}
