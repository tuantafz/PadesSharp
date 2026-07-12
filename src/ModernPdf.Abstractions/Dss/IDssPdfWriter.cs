namespace ModernPdf.Abstractions.Dss
{
    /// <summary>
    /// Appends a /DSS (Document Security Store) dictionary to a signed PDF as an
    /// incremental update, enabling PAdES-LTV (Long-Term Validation).
    /// </summary>
    public interface IDssPdfWriter
    {
        /// <summary>
        /// Appends an incremental PDF update containing the /DSS dictionary to
        /// <paramref name="signedPdfBytes"/> and returns the extended PDF bytes.
        /// The existing byte range of the signature is never modified.
        /// </summary>
        /// <param name="signedPdfBytes">The fully-signed PDF bytes.</param>
        /// <param name="dssData">Certificates, OCSP responses, CRLs and VRI entries.</param>
        /// <returns>New PDF bytes with the /DSS update appended.</returns>
        byte[] AppendDss(byte[] signedPdfBytes, DssData dssData);
    }
}
