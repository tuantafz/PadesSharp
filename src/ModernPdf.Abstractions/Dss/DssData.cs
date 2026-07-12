using System.Collections.Generic;

namespace ModernPdf.Abstractions.Dss
{
    /// <summary>
    /// Data model for the PDF /DSS (Document Security Store) dictionary.
    /// Contains global pools of certificate, OCSP, and CRL DER bytes, plus
    /// per-signature VRI (Validation-Related Information) entries.
    /// </summary>
    public sealed class DssData
    {
        /// <summary>
        /// Global pool of DER-encoded X.509 certificate bytes to embed in /DSS/Certs.
        /// </summary>
        public List<byte[]> Certificates { get; } = new List<byte[]>();

        /// <summary>
        /// Global pool of DER-encoded OCSP response bytes to embed in /DSS/OCSPs.
        /// </summary>
        public List<byte[]> OcspResponses { get; } = new List<byte[]>();

        /// <summary>
        /// Global pool of DER-encoded CRL bytes to embed in /DSS/CRLs.
        /// </summary>
        public List<byte[]> Crls { get; } = new List<byte[]>();

        /// <summary>
        /// VRI entries keyed by the uppercase hex string of the SHA-1 hash of each
        /// signature's DER-encoded CMS bytes (i.e. the /Contents value).
        /// </summary>
        public Dictionary<string, VriData> Vri { get; } = new Dictionary<string, VriData>();
    }
}
