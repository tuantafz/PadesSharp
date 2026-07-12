// Original implementation based on public standards, no code copied from iText 5/7.

namespace ModernPdf.Abstractions.Pkcs11
{
    /// <summary>
    /// PKCS#11 signing mechanism used when calling <see cref="IPkcs11Session.Sign"/>.
    /// </summary>
    public enum Pkcs11SignMechanism
    {
        /// <summary>
        /// CKM_RSA_PKCS — raw RSA PKCS#1 v1.5.
        /// The caller must supply a properly formatted DigestInfo structure as input data.
        /// This is the most widely supported mechanism.
        /// </summary>
        RsaPkcs = 0,

        /// <summary>
        /// CKM_SHA256_RSA_PKCS — SHA-256 hash and RSA PKCS#1 v1.5 performed by the token.
        /// </summary>
        Sha256RsaPkcs = 1,

        /// <summary>
        /// CKM_SHA384_RSA_PKCS — SHA-384 hash and RSA PKCS#1 v1.5 performed by the token.
        /// </summary>
        Sha384RsaPkcs = 2,

        /// <summary>
        /// CKM_SHA512_RSA_PKCS — SHA-512 hash and RSA PKCS#1 v1.5 performed by the token.
        /// </summary>
        Sha512RsaPkcs = 3,
    }
}
