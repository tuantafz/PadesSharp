// Original implementation based on public standards, no code copied from iText 5/7.

namespace ModernPdf.Abstractions.Validation
{
    /// <summary>
    /// Identifies where the revocation data used during validation was obtained.
    /// </summary>
    public enum RevocationSource
    {
        /// <summary>Revocation was not checked.</summary>
        None,

        /// <summary>Revocation data came from the PDF's embedded DSS dictionary.</summary>
        Dss,

        /// <summary>Revocation data was fetched online via OCSP.</summary>
        OcspOnline,

        /// <summary>Revocation data was fetched online via CRL.</summary>
        CrlOnline,
    }
}
