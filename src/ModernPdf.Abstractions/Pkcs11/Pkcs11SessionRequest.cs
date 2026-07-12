// Original implementation based on public standards, no code copied from iText 5/7.

using ModernPdf.Abstractions.Crypto;

namespace ModernPdf.Abstractions.Pkcs11
{
    /// <summary>
    /// Parameters required to open a PKCS#11 session and locate a private key.
    /// </summary>
    /// <remarks>
    /// Security note: the <see cref="Pin"/> field is a plain string for interoperability.
    /// Callers should avoid storing this object beyond the lifetime of the signing operation.
    /// The pin value is never included in log output from the PadesSharp library.
    /// </remarks>
    [System.Diagnostics.DebuggerDisplay("Slot={SlotId}, Key={KeyAlias}, Library={LibraryPath}")]
    public sealed class Pkcs11SessionRequest
    {
        /// <summary>Full path to the native PKCS#11 shared library (.so / .dll).</summary>
        public string LibraryPath { get; set; } = string.Empty;

        /// <summary>
        /// Slot identifier as a string. If empty or null, the first available slot is used.
        /// </summary>
        public string? SlotId { get; set; }

        /// <summary>
        /// User PIN for <c>C_Login</c>. Leave null/empty to skip login (not recommended).
        /// This value is intentionally excluded from <c>DebuggerDisplay</c> and log output.
        /// </summary>
        public string? Pin { get; set; }

        /// <summary>
        /// <c>CKA_LABEL</c> of the private key object. Used to locate the key on the token.
        /// </summary>
        public string? KeyAlias { get; set; }

        /// <summary>
        /// <c>CKA_LABEL</c> of the certificate object. If null, <see cref="KeyAlias"/> is used.
        /// </summary>
        public string? CertificateAlias { get; set; }

        /// <summary>
        /// Maximum number of concurrent sessions in the session pool.
        /// Default is 4.
        /// </summary>
        public int MaxSessions { get; set; } = 4;

        /// <summary>
        /// PKCS#11 signing mechanism the token should use.
        /// Default is <see cref="Pkcs11SignMechanism.RsaPkcs"/>.
        /// </summary>
        public Pkcs11SignMechanism SignMechanism { get; set; } = Pkcs11SignMechanism.RsaPkcs;

        /// <summary>
        /// Digest algorithm to use when computing the hash passed to the token.
        /// Default is <see cref="PdfDigestAlgorithm.Sha256"/>.
        /// </summary>
        public PdfDigestAlgorithm DigestAlgorithm { get; set; } = PdfDigestAlgorithm.Sha256;
    }
}
