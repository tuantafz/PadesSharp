// Original implementation based on public standards, no code copied from iText 5/7.

namespace ModernPdf.Abstractions.Crypto;

/// <summary>
/// Abstraction for private-key signing operations.
/// Implementations may use software keys, PKCS#11/HSM tokens, or cloud KMS.
/// </summary>
public interface ISignatureProvider
{
    /// <summary>
    /// Gets the signature algorithm name, e.g. "SHA256withRSA", "SHA256withECDSA".
    /// </summary>
    string SignatureAlgorithmName { get; }

    /// <summary>
    /// Gets the signature algorithm OID, e.g. "1.2.840.113549.1.1.11" for SHA256withRSA.
    /// </summary>
    string SignatureAlgorithmOid { get; }

    /// <summary>
    /// Signs the given digest bytes using the private key.
    /// For RSA PKCS#1 v1.5, the implementation wraps the digest in a DigestInfo
    /// before signing if required by the underlying mechanism.
    /// </summary>
    /// <param name="digest">Raw digest bytes to sign.</param>
    /// <param name="digestAlgorithm">The algorithm used to produce the digest.</param>
    /// <returns>Raw signature bytes.</returns>
    byte[] SignDigest(byte[] digest, PdfDigestAlgorithm digestAlgorithm);
}
