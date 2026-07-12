// Original implementation based on public standards, no code copied from iText 5/7.

using System.Security.Cryptography;
using ModernPdf.Abstractions.Crypto;

namespace ModernPdf.Crypto;

/// <summary>
/// Default implementation of <see cref="IDigestService"/> using
/// <see cref="System.Security.Cryptography"/> hash algorithms.
/// Supports SHA-256, SHA-384, and SHA-512 as required by PAdES / ISO 32000.
/// SHA-1 is intentionally NOT supported.
/// </summary>
public sealed class DefaultDigestService : IDigestService
{
    // OIDs from NIST FIPS 180-4 / RFC 5754
    private const string OidSha256 = "2.16.840.1.101.3.4.2.1";
    private const string OidSha384 = "2.16.840.1.101.3.4.2.2";
    private const string OidSha512 = "2.16.840.1.101.3.4.2.3";

    /// <inheritdoc/>
    public string GetDigestOid(PdfDigestAlgorithm algorithm) => algorithm switch
    {
        PdfDigestAlgorithm.Sha256 => OidSha256,
        PdfDigestAlgorithm.Sha384 => OidSha384,
        PdfDigestAlgorithm.Sha512 => OidSha512,
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported digest algorithm.")
    };

    /// <inheritdoc/>
    public byte[] ComputeDigest(Stream input, PdfDigestAlgorithm algorithm)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));

        using var hashAlgorithm = CreateHashAlgorithm(algorithm);
        return hashAlgorithm.ComputeHash(input);
    }

    /// <inheritdoc/>
    public byte[] ComputeDigest(byte[] input, PdfDigestAlgorithm algorithm)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));

        using var hashAlgorithm = CreateHashAlgorithm(algorithm);
        return hashAlgorithm.ComputeHash(input);
    }

    private static HashAlgorithm CreateHashAlgorithm(PdfDigestAlgorithm algorithm) => algorithm switch
    {
        PdfDigestAlgorithm.Sha256 => SHA256.Create(),
        PdfDigestAlgorithm.Sha384 => SHA384.Create(),
        PdfDigestAlgorithm.Sha512 => SHA512.Create(),
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported digest algorithm.")
    };
}
