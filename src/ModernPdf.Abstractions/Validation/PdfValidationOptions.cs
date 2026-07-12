// Original implementation based on public standards, no code copied from iText 5/7.

using System;

namespace ModernPdf.Abstractions.Validation
{
    /// <summary>
    /// Options that control which validation steps are performed.
    /// </summary>
    public sealed class PdfValidationOptions
    {
        /// <summary>
        /// When <c>true</c>, the signing certificate's validity period (NotBefore/NotAfter)
        /// is checked. Default: <c>true</c>.
        /// </summary>
        public bool ValidateCertificateChain { get; set; } = true;

        /// <summary>
        /// When <c>true</c> and an OCSP/CRL client is provided to the validator,
        /// the revocation status of the signing certificate is checked.
        /// Default: <c>true</c>.
        /// </summary>
        public bool ValidateRevocation { get; set; } = true;

        /// <summary>
        /// When <c>true</c>, a signature timestamp (RFC 3161) is verified if present.
        /// Default: <c>true</c>.
        /// </summary>
        public bool ValidateTimestamp { get; set; } = true;

        /// <summary>
        /// When <c>true</c>, an unknown/unavailable revocation status is treated as a
        /// warning rather than an error. Default: <c>false</c>.
        /// </summary>
        public bool AllowUnknownRevocationStatus { get; set; } = false;

        /// <summary>
        /// When <c>true</c>, the signing certificate is validated against the system trust
        /// store by building a full X.509 chain. Requires <see cref="ValidateCertificateChain"/>
        /// to also be <c>true</c>. Default: <c>false</c> (opt-in) because test and demo
        /// workflows commonly use self-signed certificates that are not in the trust store.
        /// </summary>
        public bool ValidateChainTrust { get; set; } = false;

        /// <summary>
        /// Reference time for validity period and revocation checks.
        /// Defaults to <see cref="DateTimeOffset.UtcNow"/> at validation time.
        /// </summary>
        public DateTimeOffset? ValidationTime { get; set; }

        // ── Timestamp options ────────────────────────────────────────────────

        /// <summary>
        /// When <c>true</c>, absence of a signature timestamp makes validation fail.
        /// Default: <c>false</c>.
        /// </summary>
        public bool RequireTimestamp { get; set; } = false;

        /// <summary>
        /// When <c>true</c> and a timestamp token is present, the RFC 3161 message
        /// imprint is verified against the CMS signature value. Cryptographically
        /// mandatory; disable only for interoperability diagnostics. Default: <c>true</c>.
        /// </summary>
        public bool ValidateTimestampMessageImprint { get; set; } = true;

        /// <summary>
        /// When <c>true</c>, the TSA certificate's validity period is checked at
        /// the token's <c>genTime</c>. Default: <c>true</c>.
        /// </summary>
        public bool ValidateTimestampCertificatePeriod { get; set; } = true;

        /// <summary>
        /// When <c>true</c>, a full X.509 chain is built for the TSA certificate to
        /// verify it chains to a system-trusted root. Default: <c>false</c>.
        /// </summary>
        public bool ValidateTimestampChainTrust { get; set; } = false;

        /// <summary>
        /// When <c>true</c>, the TSA certificate's revocation status is checked.
        /// Default: <c>false</c>.
        /// </summary>
        public bool ValidateTimestampRevocation { get; set; } = false;

        // ── DSS / revocation options ─────────────────────────────────────────

        /// <summary>
        /// When <c>true</c>, revocation data embedded in the PDF's DSS dictionary
        /// (/DSS, /VRI, /OCSPs, /CRLs) is used before contacting external servers.
        /// Default: <c>true</c>.
        /// </summary>
        public bool UseEmbeddedDss { get; set; } = true;

        /// <summary>
        /// When <c>false</c>, online OCSP/CRL clients are never called; only
        /// embedded DSS data is used for revocation. Default: <c>true</c>.
        /// </summary>
        public bool AllowOnlineRevocationFallback { get; set; } = true;

        /// <summary>
        /// When <c>true</c>, revocation is checked for each certificate in the
        /// signing chain, not just the end-entity signing certificate.
        /// Default: <c>false</c>.
        /// </summary>
        public bool ValidateEntireCertificateChainRevocation { get; set; } = false;
    }
}
