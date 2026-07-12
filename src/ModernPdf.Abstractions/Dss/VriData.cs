using System.Collections.Generic;

namespace ModernPdf.Abstractions.Dss
{
    /// <summary>
    /// Per-signature validation-related information (VRI) for embedding in the PDF /DSS dictionary.
    /// Each VriData entry corresponds to one signature, identified by the SHA-1 hex of its CMS bytes.
    /// Indices reference the global cert/OCSP/CRL pools in <see cref="DssData"/>.
    /// </summary>
    public sealed class VriData
    {
        /// <summary>Indices into <see cref="DssData.Certificates"/> used for this signature.</summary>
        public List<int> CertificateIndices { get; } = new List<int>();

        /// <summary>Indices into <see cref="DssData.OcspResponses"/> used for this signature.</summary>
        public List<int> OcspIndices { get; } = new List<int>();

        /// <summary>Indices into <see cref="DssData.Crls"/> used for this signature.</summary>
        public List<int> CrlIndices { get; } = new List<int>();
    }
}
