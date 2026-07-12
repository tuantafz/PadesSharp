// Original implementation based on public standards, no code copied from iText 5/7.

using System.Collections.Generic;

namespace ModernPdf.Abstractions.Validation
{
    /// <summary>
    /// Detailed validation result for a single PDF signature field.
    /// </summary>
    public sealed class PdfSignatureValidationResult
    {
        /// <summary>Name of the AcroForm signature field (e.g. "Signature1").</summary>
        public string SignatureName { get; set; } = string.Empty;

        /// <summary>Distinguished name of the signing certificate subject, when embedded in CMS.</summary>
        public string SignerSubject { get; set; } = string.Empty;

        /// <summary>Distinguished name of the signing certificate issuer, when embedded in CMS.</summary>
        public string SignerIssuer { get; set; } = string.Empty;

        /// <summary>Length, in bytes, of the PDF revision covered by this signature.</summary>
        public long SignedRevisionLength { get; set; }

        /// <summary>
        /// <c>true</c> when the signed revision reaches the current end of the PDF.
        /// A false value can be normal for an earlier signature in a multi-signature document.
        /// </summary>
        public bool CoversWholeDocument { get; set; }

        // ── Core cryptographic checks ────────────────────────────────────────

        /// <summary>
        /// <c>true</c> when the ByteRange content, hashed and verified via CMS, matches
        /// the embedded digest — i.e. the document was not altered after signing.
        /// </summary>
        public bool DocumentIntegrityValid { get; set; }

        /// <summary>
        /// <c>true</c> when the CMS RSA/ECDSA signature over the signed attributes is
        /// cryptographically valid for the signing certificate's public key.
        /// </summary>
        public bool CmsSignatureValid { get; set; }

        // ── Certificate checks ───────────────────────────────────────────────

        /// <summary>
        /// <c>true</c> when the signing certificate's NotBefore/NotAfter range covers
        /// the validation time. Set even when <see cref="CertificateChainTrusted"/> is
        /// not checked.
        /// </summary>
        public bool CertificatePeriodValid { get; set; } = true;

        /// <summary>
        /// <c>true</c> when a full X.509 chain was built successfully to a trusted root,
        /// OR when chain trust validation was not requested (opt-in).
        /// </summary>
        public bool CertificateChainTrusted { get; set; } = true;

        /// <summary>
        /// Composite: <c>true</c> when <see cref="CertificatePeriodValid"/> and
        /// <see cref="CertificateChainTrusted"/> are both <c>true</c>.
        /// Kept for API compatibility.
        /// </summary>
        public bool CertificateChainValid { get; set; }

        // ── Revocation ───────────────────────────────────────────────────────

        /// <summary>
        /// <c>true</c> when revocation status was checked and the certificate is not revoked.
        /// </summary>
        public bool RevocationValid { get; set; }

        /// <summary>Where the revocation data used during validation came from.</summary>
        public RevocationSource RevocationSource { get; set; } = RevocationSource.None;

        // ── Timestamp checks ─────────────────────────────────────────────────

        /// <summary><c>true</c> when an RFC 3161 timestamp attribute was found.</summary>
        public bool TimestampPresent { get; set; }

        /// <summary>
        /// <c>true</c> when the timestamp message imprint matches the CMS signature
        /// value, or when the check was not performed (no token present / disabled).
        /// </summary>
        public bool TimestampMessageImprintValid { get; set; } = true;

        /// <summary>
        /// <c>true</c> when the TSA certificate is present, its CMS signature is valid,
        /// it carries the id-kp-timeStamping EKU, and its period covers genTime.
        /// </summary>
        public bool TimestampCertificateValid { get; set; } = true;

        /// <summary>
        /// <c>true</c> when the TSA chain was built to a trusted root, or when the
        /// chain trust check was not requested.
        /// </summary>
        public bool TimestampChainTrusted { get; set; } = true;

        /// <summary>
        /// <c>true</c> when the TSA certificate revocation status was checked and is
        /// not revoked, or when the check was not requested.
        /// </summary>
        public bool TimestampRevocationValid { get; set; } = true;

        /// <summary>
        /// Composite timestamp validity. <c>true</c> when all enabled timestamp checks
        /// pass, or when no timestamp is present and none is required.
        /// </summary>
        public bool TimestampValid { get; set; } = true;

        // ── Overall result ───────────────────────────────────────────────────

        /// <summary>Overall validity: <c>true</c> only when all enabled checks pass.</summary>
        public bool IsValid { get; set; }

        /// <summary>Errors that caused one or more checks to fail.</summary>
        public IReadOnlyList<string> Errors { get; set; } = new List<string>();

        /// <summary>Non-fatal observations (e.g. revocation status unknown).</summary>
        public IReadOnlyList<string> Warnings { get; set; } = new List<string>();
    }
}
