// Original implementation based on public standards, no code copied from iText 5/7.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ModernPdf.Abstractions.Crypto;

namespace ModernPdf.Crypto;

/// <summary>
/// RSA PKCS#1 v1.5 software signing provider using a <see cref="X509Certificate2"/>
/// with an exportable private key.
/// Intended for testing and development. Production deployments should use
/// <c>Pkcs11SignatureProvider</c> or a cloud KMS provider.
/// </summary>
public sealed class RsaSoftwareSignatureProvider : ISignatureProvider, IDisposable
{
    private readonly RSA _rsa;
    private bool _disposed;

    /// <summary>
    /// Initialises the provider from a certificate with an attached private key.
    /// </summary>
    /// <param name="certificate">Certificate that carries an RSA private key.</param>
    /// <exception cref="ArgumentException">Thrown if the certificate has no RSA private key.</exception>
    public RsaSoftwareSignatureProvider(X509Certificate2 certificate)
    {
        if (certificate is null) throw new ArgumentNullException(nameof(certificate));

        _rsa = certificate.GetRSAPrivateKey()
            ?? throw new ArgumentException("Certificate does not contain an RSA private key.", nameof(certificate));
    }

    /// <summary>
    /// Initialises the provider directly from an <see cref="RSA"/> key.
    /// </summary>
    public RsaSoftwareSignatureProvider(RSA rsaKey)
    {
        _rsa = rsaKey ?? throw new ArgumentNullException(nameof(rsaKey));
    }

    /// <inheritdoc/>
    public string SignatureAlgorithmName => "SHA256withRSA";

    /// <inheritdoc/>
    /// <remarks>OID for rsaEncryption (PKCS#1) — 1.2.840.113549.1.1.1</remarks>
    public string SignatureAlgorithmOid => "1.2.840.113549.1.1.1";

    /// <inheritdoc/>
    /// <remarks>
    /// Signs using RSA PKCS#1 v1.5 with the digest algorithm matching <paramref name="digestAlgorithm"/>.
    /// The .NET RSA.SignHash method handles DigestInfo wrapping internally.
    /// </remarks>
    public byte[] SignDigest(byte[] digest, PdfDigestAlgorithm digestAlgorithm)
    {
        if (digest is null) throw new ArgumentNullException(nameof(digest));
        if (_disposed) throw new ObjectDisposedException(nameof(RsaSoftwareSignatureProvider));

        var hashAlgorithmName = ToHashAlgorithmName(digestAlgorithm);
        return _rsa.SignHash(digest, hashAlgorithmName, RSASignaturePadding.Pkcs1);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _rsa.Dispose();
            _disposed = true;
        }
    }

    private static HashAlgorithmName ToHashAlgorithmName(PdfDigestAlgorithm algorithm) => algorithm switch
    {
        PdfDigestAlgorithm.Sha256 => HashAlgorithmName.SHA256,
        PdfDigestAlgorithm.Sha384 => HashAlgorithmName.SHA384,
        PdfDigestAlgorithm.Sha512 => HashAlgorithmName.SHA512,
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported digest algorithm.")
    };
}
