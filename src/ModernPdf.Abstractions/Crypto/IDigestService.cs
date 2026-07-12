// Original implementation based on public standards, no code copied from iText 5/7.

namespace ModernPdf.Abstractions.Crypto;

/// <summary>
/// Computes message digests and maps algorithms to their ASN.1 OIDs.
/// </summary>
public interface IDigestService
{
    /// <summary>
    /// Returns the ASN.1 OID string for the specified digest algorithm.
    /// </summary>
    /// <param name="algorithm">The digest algorithm.</param>
    /// <returns>OID string, e.g. "2.16.840.1.101.3.4.2.1" for SHA-256.</returns>
    string GetDigestOid(PdfDigestAlgorithm algorithm);

    /// <summary>
    /// Computes the digest of the given stream.
    /// The stream is read from its current position to the end.
    /// </summary>
    /// <param name="input">Input stream to hash.</param>
    /// <param name="algorithm">Digest algorithm to use.</param>
    /// <returns>Raw digest bytes.</returns>
    byte[] ComputeDigest(Stream input, PdfDigestAlgorithm algorithm);

    /// <summary>
    /// Computes the digest of the given byte array.
    /// </summary>
    /// <param name="input">Input bytes to hash.</param>
    /// <param name="algorithm">Digest algorithm to use.</param>
    /// <returns>Raw digest bytes.</returns>
    byte[] ComputeDigest(byte[] input, PdfDigestAlgorithm algorithm);
}
