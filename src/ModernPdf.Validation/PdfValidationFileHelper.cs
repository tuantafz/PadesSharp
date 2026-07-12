// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.IO;
using ModernPdf.Abstractions.Validation;

namespace ModernPdf.Validation
{
    /// <summary>
    /// Convenience helpers for validating PDF signatures from file paths or byte arrays.
    /// </summary>
    public static class PdfValidationFileHelper
    {
        /// <summary>
        /// Validates all signatures in the PDF file at <paramref name="pdfPath"/>.
        /// </summary>
        /// <param name="pdfPath">Path to the PDF file to validate.</param>
        /// <param name="validator">The validator implementation to use.</param>
        /// <param name="options">
        /// Validation options.  Pass <c>null</c> to use the defaults.
        /// </param>
        /// <returns>Validation report with per-signature results.</returns>
        public static PdfValidationReport ValidateFile(
            string pdfPath,
            IPdfSignatureValidator validator,
            PdfValidationOptions? options = null)
        {
            if (pdfPath    == null) throw new ArgumentNullException(nameof(pdfPath));
            if (validator  == null) throw new ArgumentNullException(nameof(validator));

            // Read with FileShare.Read so other processes can still read the file.
            using var fs = new FileStream(pdfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return validator.Validate(fs, options);
        }

        /// <summary>
        /// Validates all signatures in the supplied <paramref name="pdfBytes"/> array.
        /// </summary>
        public static PdfValidationReport ValidateBytes(
            byte[] pdfBytes,
            IPdfSignatureValidator validator,
            PdfValidationOptions? options = null)
        {
            if (pdfBytes  == null) throw new ArgumentNullException(nameof(pdfBytes));
            if (validator == null) throw new ArgumentNullException(nameof(validator));

            using var ms = new MemoryStream(pdfBytes, writable: false);
            return validator.Validate(ms, options);
        }
    }
}
