// Original implementation based on public standards, no code copied from iText 5/7.

using System.Security.Cryptography.X509Certificates;

namespace ModernPdf.Abstractions.Crypto;

/// <summary>
/// Request parameters for creating a CMS detached signature (RFC 5652).
/// All properties marked with &lt;c&gt;// required&lt;/c&gt; must be set before passing to <see cref="ICmsSigner"/>.
/// </summary>
public sealed class CmsSigningRequest
{
    /// <summary>
    /// The pre-computed content digest (hash of the PDF byte ranges).
    /// This is the digest of the content referenced in the CMS messageDigest attribute.
    /// </summary>
    public byte[] ContentDigest { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// The signing certificate. Its public key algorithm must match <see cref="SignatureProvider"/>.
    /// </summary>
    public X509Certificate2 SigningCertificate { get; set; } = null!;

    /// <summary>
    /// Full certificate chain, including the signing certificate at index 0,
    /// followed by intermediate CAs (root CA typically excluded).
    /// </summary>
    public IReadOnlyList<X509Certificate2> CertificateChain { get; set; } = Array.Empty<X509Certificate2>();

    /// <summary>The digest algorithm used to compute <see cref="ContentDigest"/>.</summary>
    public PdfDigestAlgorithm DigestAlgorithm { get; set; } = PdfDigestAlgorithm.Sha256;

    /// <summary>The private-key signing provider.</summary>
    public ISignatureProvider SignatureProvider { get; set; } = null!;

    /// <summary>Signing time embedded in the signingTime signed attribute.</summary>
    public DateTimeOffset SigningTime { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When true, includes the ESS signingCertificateV2 signed attribute (CAdES-BES).
    /// Recommended for PAdES compliance.
    /// </summary>
    public bool IncludeSigningCertificateV2 { get; set; } = true;
}
