// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.Security.Cryptography.X509Certificates;

namespace ModernPdf.Abstractions.Pkcs11
{
    /// <summary>
    /// Represents an active PKCS#11 session with a token/HSM.
    /// Implementations must be thread-safe if shared across threads.
    /// </summary>
    public interface IPkcs11Session : IDisposable
    {
        /// <summary>
        /// Signs <paramref name="dataToSign"/> using the private key on the token.
        /// </summary>
        /// <param name="dataToSign">
        /// For <see cref="Pkcs11SignMechanism.RsaPkcs"/>, this must be a DER-encoded
        /// DigestInfo structure. For hash-then-sign mechanisms (SHA256_RSA_PKCS etc.),
        /// this is the raw pre-hash message.
        /// </param>
        /// <param name="mechanism">The PKCS#11 signing mechanism to use.</param>
        /// <returns>Raw signature bytes (RSA signature, not DER-wrapped).</returns>
        byte[] Sign(byte[] dataToSign, Pkcs11SignMechanism mechanism);

        /// <summary>
        /// Retrieves the certificate stored on the token with the given label.
        /// </summary>
        /// <param name="certificateLabel">The <c>CKA_LABEL</c> of the certificate object.</param>
        X509Certificate2 GetCertificate(string certificateLabel);
    }
}
