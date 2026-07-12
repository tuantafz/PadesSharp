// Original implementation based on public standards, no code copied from iText 5/7.

using System.Collections.Generic;

namespace ModernPdf.Abstractions.Validation
{
    /// <summary>
    /// Top-level validation report for all signatures found in a PDF document.
    /// </summary>
    public sealed class PdfValidationReport
    {
        /// <summary>
        /// <c>true</c> when every signature in <see cref="Signatures"/> is valid.
        /// <c>false</c> if any signature failed validation or no signatures were found.
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>Per-signature validation results, one entry per AcroForm signature field.</summary>
        public IReadOnlyList<PdfSignatureValidationResult> Signatures { get; set; }
            = new List<PdfSignatureValidationResult>();
    }
}
