// Original implementation based on public standards, no code copied from iText 5/7.

namespace ModernPdf.Abstractions.Crypto;

/// <summary>
/// Digest algorithm used for PDF signing and CMS construction.
/// OIDs defined in NIST FIPS 180-4 / RFC 5754.
/// </summary>
public enum PdfDigestAlgorithm
{
    /// <summary>SHA-256 — OID 2.16.840.1.101.3.4.2.1</summary>
    Sha256,

    /// <summary>SHA-384 — OID 2.16.840.1.101.3.4.2.2</summary>
    Sha384,

    /// <summary>SHA-512 — OID 2.16.840.1.101.3.4.2.3</summary>
    Sha512
}
