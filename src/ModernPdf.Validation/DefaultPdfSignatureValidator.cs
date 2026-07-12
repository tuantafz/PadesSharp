// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModernPdf.Abstractions.Revocation;
using ModernPdf.Abstractions.Validation;
using ModernPdf.Validation.Internal;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tsp;
using Org.BouncyCastle.X509;
using SystemX509 = System.Security.Cryptography.X509Certificates;

namespace ModernPdf.Validation
{
    /// <summary>
    /// Default implementation of <see cref="IPdfSignatureValidator"/>.
    /// Validates document integrity, CMS signature cryptography, certificate validity
    /// period, chain trust, timestamp binding, and revocation for all signatures in a PDF.
    /// </summary>
    public sealed class DefaultPdfSignatureValidator : IPdfSignatureValidator
    {
        // OID for id-aa-signatureTimeStampToken (RFC 5652 / CAdES)
        private static readonly DerObjectIdentifier TspUnsignedAttrOid =
            new DerObjectIdentifier("1.2.840.113549.1.9.16.2.14");

        // OID for id-kp-timeStamping (RFC 3161)
        private const string IdKpTimeStamping = "1.3.6.1.5.5.7.3.8";

        private readonly ILogger _logger;
        private readonly IOcspClient? _ocspClient;
        private readonly ICrlClient?  _crlClient;

        /// <param name="logger">Optional logger for diagnostic output.</param>
        /// <param name="ocspClient">
        /// When provided and <see cref="PdfValidationOptions.ValidateRevocation"/> is <c>true</c>,
        /// the signing certificate's revocation status is checked via OCSP (RFC 6960).
        /// </param>
        /// <param name="crlClient">
        /// Fallback revocation client used when OCSP is unavailable or not provided.
        /// </param>
        public DefaultPdfSignatureValidator(
            ILogger? logger = null,
            IOcspClient? ocspClient = null,
            ICrlClient?  crlClient  = null)
        {
            _logger     = logger ?? NullLogger.Instance;
            _ocspClient = ocspClient;
            _crlClient  = crlClient;
        }

        /// <inheritdoc/>
        public PdfValidationReport Validate(Stream pdfInput, PdfValidationOptions? options = null)
        {
            if (pdfInput == null) throw new ArgumentNullException(nameof(pdfInput));
            options ??= new PdfValidationOptions();

            byte[] pdfBytes = ReadAllBytes(pdfInput);

            var signatures = PdfSignatureExtractor.Extract(pdfBytes);
            _logger.LogDebug("Found {Count} signature(s) in PDF.", signatures.Count);

            if (signatures.Count == 0)
            {
                return new PdfValidationReport
                {
                    IsValid    = false,
                    Signatures = new List<PdfSignatureValidationResult>(),
                };
            }

            // Read the DSS once for the entire document — shared across all signatures.
            DssData dss = options.UseEmbeddedDss
                ? PdfDssReader.Read(pdfBytes)
                : new DssData();

            var results = new List<PdfSignatureValidationResult>(signatures.Count);
            foreach (var sig in signatures)
                results.Add(ValidateOne(pdfBytes, sig, options, dss));

            return new PdfValidationReport
            {
                IsValid    = results.All(r => r.IsValid),
                Signatures = results,
            };
        }

        // -----------------------------------------------------------------------
        // Per-signature validation
        // -----------------------------------------------------------------------

        private PdfSignatureValidationResult ValidateOne(
            byte[] pdfBytes,
            ExtractedSignature sig,
            PdfValidationOptions options,
            DssData dss)
        {
            var errors   = new List<string>();
            var warnings = new List<string>();
            var result   = new PdfSignatureValidationResult
            {
                SignatureName = sig.FieldName,
                Errors        = errors,
                Warnings      = warnings,
            };

            try
            {
                // ── Step 1: Validate ByteRange and assemble signed content ────
                byte[] signedContent;
                try
                {
                    signedContent = AssembleByteRange(pdfBytes, sig.ByteRange);
                }
                catch (ArgumentException ex)
                {
                    errors.Add($"ByteRange validation failed: {ex.Message}");
                    return Finalise(result, options);
                }

                // ── Step 2: Parse CMS and verify each signer ─────────────────
                CmsSignedData cmsData;
                try
                {
                    cmsData = new CmsSignedData(
                        new CmsProcessableByteArray(signedContent),
                        sig.ContentsBytes);
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to parse CMS SignedData: {ex.Message}");
                    return Finalise(result, options);
                }

                var certStore   = cmsData.GetCertificates();
                var allEmbedded = certStore.EnumerateMatches(null)
                                           .Cast<X509Certificate>().ToList();

                var signerList = cmsData.GetSignerInfos().GetSigners()
                                        .Cast<SignerInformation>().ToList();

                if (signerList.Count == 0)
                {
                    errors.Add("No SignerInfo entries found in CMS.");
                    return Finalise(result, options);
                }

                X509Certificate? signingCert = null;
                bool allSigned = true;

                foreach (SignerInformation signer in signerList)
                {
                    X509Certificate? cert =
                        allEmbedded.FirstOrDefault(c => signer.SignerID.Match(c))
                        ?? allEmbedded.FirstOrDefault();

                    if (cert == null)
                    {
                        errors.Add("Signing certificate not embedded in CMS.");
                        allSigned = false;
                        continue;
                    }
                    if (signingCert == null) signingCert = cert;

                    bool ok;
                    try { ok = signer.Verify(cert); }
                    catch (CmsException ex) { ok = false; errors.Add($"CMS verify error: {ex.Message}"); }
                    catch (Exception ex)    { ok = false; errors.Add($"CMS signature verification failed: {ex.Message}"); }

                    if (!ok)
                    {
                        allSigned = false;
                        if (!errors.Any(e => e.StartsWith("CMS")))
                            errors.Add("CMS signature verification failed (document may have been modified after signing).");
                    }

                    // ── Step 3: Timestamp ──────────────────────────────────
                    // Call when ValidateTimestamp OR RequireTimestamp so that
                    // RequireTimestamp=true cannot be bypassed by ValidateTimestamp=false.
                    if (options.ValidateTimestamp || options.RequireTimestamp)
                        CheckTimestamp(signer, result, options, errors, warnings, dss);
                }

                result.DocumentIntegrityValid = allSigned;
                result.CmsSignatureValid      = allSigned;

                // ── Step 4: Certificate validity period ───────────────────
                if (options.ValidateCertificateChain && signingCert != null)
                    CheckCertificatePeriod(signingCert, options, result, errors, warnings);

                // ── Step 5: X.509 chain trust ─────────────────────────────
                if (options.ValidateCertificateChain && options.ValidateChainTrust && signingCert != null)
                    CheckCertificateChainTrust(signingCert, allEmbedded, options, result, errors, warnings);

                // ── Step 6: Certificate revocation ────────────────────────
                if (options.ValidateRevocation && signingCert != null)
                    CheckRevocation(signingCert, allEmbedded, options, result, warnings, dss,
                                    sig.ContentsBytes);
                else if (!options.ValidateRevocation)
                    result.RevocationValid = true;
                // When ValidateRevocation=true but cert was self-signed or no issuer found,
                // RevocationValid is set inside CheckRevocation.
            }
            catch (Exception ex)
            {
                errors.Add($"Unexpected validation error: {ex.Message}");
                _logger.LogError(ex, "Unexpected error validating signature '{Name}'.", sig.FieldName);
            }

            return Finalise(result, options);
        }

        // -----------------------------------------------------------------------
        // Timestamp check — RFC 3161 message imprint binding + EKU + cert period
        // -----------------------------------------------------------------------

        private void CheckTimestamp(
            SignerInformation signer,
            PdfSignatureValidationResult result,
            PdfValidationOptions options,
            List<string> errors,
            List<string> warnings,
            DssData dss)
        {
            var unsignedAttrs = signer.UnsignedAttributes;
            if (unsignedAttrs == null)
            {
                result.TimestampPresent = false;
                if (options.RequireTimestamp)
                    errors.Add("Signature timestamp is required but not present.");
                return;
            }

            var tspAttrEntry = unsignedAttrs[TspUnsignedAttrOid];
            if (tspAttrEntry == null)
            {
                result.TimestampPresent = false;
                if (options.RequireTimestamp)
                    errors.Add("Signature timestamp is required but not present.");
                return;
            }

            result.TimestampPresent = true;

            try
            {
                var attrValues = tspAttrEntry.AttrValues;
                if (attrValues == null || attrValues.Count == 0)
                {
                    result.TimestampValid               = false;
                    result.TimestampMessageImprintValid = false;
                    errors.Add("Signature timestamp attribute is empty.");
                    return;
                }

                byte[] tstDer  = attrValues[0].ToAsn1Object().GetDerEncoded();
                var tstToken   = new TimeStampToken(new CmsSignedData(tstDer));

                // ── Find TSA cert ──────────────────────────────────────────
                var tstCertStore = tstToken.GetCertificates();
                var tstCerts     = tstCertStore.EnumerateMatches(null)
                                               .Cast<X509Certificate>().ToList();
                X509Certificate? tsaCert = tstCerts.FirstOrDefault(c => tstToken.SignerID.Match(c));

                if (tsaCert == null)
                {
                    result.TimestampValid            = false;
                    result.TimestampCertificateValid = false;
                    errors.Add("TSA certificate not embedded in timestamp token; cannot verify CMS signature.");
                    return;
                }

                // ── Verify CMS signature on the timestamp token ────────────
                try
                {
                    tstToken.Validate(tsaCert);
                }
                catch (Exception ex)
                {
                    result.TimestampValid            = false;
                    result.TimestampCertificateValid = false;
                    errors.Add($"TSA CMS signature invalid: {ex.Message}");
                    return;
                }

                // ── Message imprint binding ────────────────────────────────
                if (options.ValidateTimestampMessageImprint)
                {
                    var    tsaInfo        = tstToken.TimeStampInfo;
                    string hashAlgOid     = tsaInfo.MessageImprintAlgOid; // string OID in BouncyCastle
                    byte[] expectedDigest = tsaInfo.GetMessageImprintDigest();
                    byte[] sigValue       = signer.GetSignature();

                    byte[] actualDigest;
                    try
                    {
                        actualDigest = DigestUtilities.CalculateDigest(hashAlgOid, sigValue);
                    }
                    catch (Exception ex)
                    {
                        result.TimestampValid               = false;
                        result.TimestampMessageImprintValid = false;
                        errors.Add($"Cannot compute message imprint hash ({hashAlgOid}): {ex.Message}");
                        return;
                    }

                    bool imprintOk = CryptographicEquals(expectedDigest, actualDigest);
                    result.TimestampMessageImprintValid = imprintOk;
                    if (!imprintOk)
                    {
                        result.TimestampValid = false;
                        errors.Add("Timestamp message imprint does not match the CMS signature value.");
                        return;
                    }
                }

                // ── EKU: id-kp-timeStamping ────────────────────────────────
                // GetExtendedKeyUsage() returns IList whose items may be string OIDs or
                // DerObjectIdentifier depending on the BouncyCastle version — use ToString().
                var  eku = tsaCert.GetExtendedKeyUsage();
                bool hasTimestampingEku = eku != null &&
                    ((System.Collections.IEnumerable)eku).Cast<object>()
                        .Any(o => string.Equals(o?.ToString(), IdKpTimeStamping, StringComparison.Ordinal));

                if (!hasTimestampingEku)
                {
                    result.TimestampValid            = false;
                    result.TimestampCertificateValid = false;
                    errors.Add("TSA certificate is missing the id-kp-timeStamping extended key usage.");
                    return;
                }

                // genTime is always extracted — needed by period, chain trust, and revocation checks.
                DateTime genTime = tstToken.TimeStampInfo.GenTime;

                // ── TSA cert validity period at genTime ────────────────────
                if (options.ValidateTimestampCertificatePeriod)
                {
                    if (tsaCert.NotBefore > genTime || tsaCert.NotAfter < genTime)
                    {
                        result.TimestampValid            = false;
                        result.TimestampCertificateValid = false;
                        errors.Add(
                            $"TSA certificate was not valid at genTime {genTime:u} " +
                            $"(NotBefore: {tsaCert.NotBefore:u}, NotAfter: {tsaCert.NotAfter:u}).");
                        return;
                    }
                }

                // ── TSA chain trust ────────────────────────────────────────
                if (options.ValidateTimestampChainTrust)
                {
                    bool chainOk = CheckTsaChainTrust(tsaCert, tstCerts, genTime, errors, warnings);
                    result.TimestampChainTrusted = chainOk;
                    if (!chainOk)
                    {
                        result.TimestampValid = false;
                        return;
                    }
                }

                // ── TSA cert revocation ────────────────────────────────────
                if (options.ValidateTimestampRevocation)
                {
                    bool revokeOk = CheckTsaCertRevocation(tsaCert, tstCerts, genTime, options, dss, errors, warnings);
                    result.TimestampRevocationValid = revokeOk;
                    if (!revokeOk)
                    {
                        result.TimestampValid = false;
                        return;
                    }
                }

                result.TimestampCertificateValid = true;
                result.TimestampValid            = true;
            }
            catch (Exception ex)
            {
                result.TimestampValid = false;
                errors.Add($"Signature timestamp verification failed: {ex.Message}");
            }
        }

        private bool CheckTsaCertRevocation(
            X509Certificate tsaCert,
            IList<X509Certificate> tsaChainCerts,
            DateTime genTime,
            PdfValidationOptions options,
            DssData dss,
            List<string> errors,
            List<string> warnings)
        {
            // Self-signed TSA cert: revocation not applicable.
            if (tsaCert.IssuerDN.Equivalent(tsaCert.SubjectDN))
            {
                warnings.Add("TSA certificate is self-signed; revocation checking not applicable.");
                return true;
            }

            // Find TSA cert's issuer from the certs embedded in the timestamp token.
            X509Certificate? tsaIssuer = tsaChainCerts.FirstOrDefault(
                c => c.SubjectDN.Equivalent(tsaCert.IssuerDN) && !c.Equals(tsaCert));

            if (tsaIssuer == null)
            {
                bool allowed = options.AllowUnknownRevocationStatus;
                string msg = $"TSA certificate issuer not found; revocation cannot be determined for '{tsaCert.SubjectDN}'.";
                if (allowed) warnings.Add(msg); else errors.Add(msg);
                return allowed;
            }

            // Use DSS global pools (no VRI key — we don't have TSA sig bytes).
            var dssResult = DssRevocationChecker.Check(
                tsaCert, tsaIssuer, dss, signatureBytes: null, referenceTime: genTime);

            switch (dssResult.Status)
            {
                case DssRevocationStatus.Good:    return true;
                case DssRevocationStatus.Revoked:
                    errors.Add($"TSA certificate '{tsaCert.SubjectDN}' is revoked (DSS): {dssResult.Message}");
                    return false;
            }

            // Unknown from DSS — try online if allowed.
            if (!options.AllowOnlineRevocationFallback || (_ocspClient == null && _crlClient == null))
            {
                bool allowed = options.AllowUnknownRevocationStatus;
                string msg = $"TSA certificate revocation status unknown for '{tsaCert.SubjectDN}'" +
                             (_ocspClient == null && _crlClient == null
                                 ? " — no OCSP/CRL client provided." : " — online fallback disabled.");
                if (allowed) warnings.Add(msg); else errors.Add(msg);
                return allowed;
            }

            SystemX509.X509Certificate2? subj2  = null;
            SystemX509.X509Certificate2? issuer2 = null;
            try
            {
                subj2   = new SystemX509.X509Certificate2(tsaCert.GetEncoded());
                issuer2 = new SystemX509.X509Certificate2(tsaIssuer.GetEncoded());

                RevocationStatus status = RevocationStatus.Unavailable;
                if (_ocspClient != null)
                {
                    try
                    {
                        var r = _ocspClient.CheckRevocationAsync(subj2, issuer2).GetAwaiter().GetResult();
                        status = r.Status;
                    }
                    catch (Exception ex) { _logger.LogWarning("TSA OCSP failed: {M}", ex.Message); }
                }
                if (status == RevocationStatus.Unavailable && _crlClient != null)
                {
                    try
                    {
                        var r = _crlClient.CheckRevocationAsync(subj2).GetAwaiter().GetResult();
                        status = r.Status;
                    }
                    catch (Exception ex) { _logger.LogWarning("TSA CRL failed: {M}", ex.Message); }
                }

                if (status == RevocationStatus.Good)    return true;
                if (status == RevocationStatus.Revoked)
                {
                    errors.Add($"TSA certificate '{tsaCert.SubjectDN}' has been revoked.");
                    return false;
                }

                bool ok = options.AllowUnknownRevocationStatus;
                string m = $"TSA certificate revocation status unknown for '{tsaCert.SubjectDN}'.";
                if (ok) warnings.Add(m); else errors.Add(m);
                return ok;
            }
            finally
            {
                subj2?.Dispose();
                issuer2?.Dispose();
            }
        }

        // -----------------------------------------------------------------------
        // TSA chain trust (mirrors signing-cert chain trust)
        // -----------------------------------------------------------------------

        private static bool CheckTsaChainTrust(
            X509Certificate tsaCertBc,
            IList<X509Certificate> tsaChainBc,
            DateTime genTime,       // verification at TST genTime, not wall-clock now
            List<string> errors,
            List<string> warnings)
        {
            var extra = new List<SystemX509.X509Certificate2>();
            try
            {
                byte[] tsaDer = tsaCertBc.GetEncoded();
                using var tsaCert2 = new SystemX509.X509Certificate2(tsaDer);

                foreach (var c in tsaChainBc)
                {
                    try { extra.Add(new SystemX509.X509Certificate2(c.GetEncoded())); }
                    catch { /* skip */ }
                }

                using var chain = new SystemX509.X509Chain();
                chain.ChainPolicy.RevocationMode   = SystemX509.X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationTime = genTime;  // historical validation at genTime
                foreach (var c in extra) chain.ChainPolicy.ExtraStore.Add(c);

                bool valid = chain.Build(tsaCert2);
                if (!valid)
                {
                    foreach (var status in chain.ChainStatus)
                    {
                        string info = status.StatusInformation?.Trim() ?? status.Status.ToString();
                        errors.Add($"TSA certificate chain error: {info}");
                    }
                }
                return valid;
            }
            catch (Exception ex)
            {
                // Fail-closed: if chain trust was requested and threw, treat as untrusted.
                errors.Add($"TSA certificate chain check failed: {ex.Message}");
                return false;
            }
            finally
            {
                foreach (var c in extra) c.Dispose();
            }
        }

        // -----------------------------------------------------------------------
        // X.509 chain trust check for signing certificate
        // -----------------------------------------------------------------------

        private static void CheckCertificateChainTrust(
            X509Certificate signingCertBc,
            IList<X509Certificate> allEmbedded,
            PdfValidationOptions options,
            PdfSignatureValidationResult result,
            List<string> errors,
            List<string> warnings)
        {
            var extraCerts = new List<SystemX509.X509Certificate2>();
            try
            {
                byte[] sigDer = signingCertBc.GetEncoded();
                using var signingCert2 = new SystemX509.X509Certificate2(sigDer);

                foreach (var embedded in allEmbedded)
                {
                    try { extraCerts.Add(new SystemX509.X509Certificate2(embedded.GetEncoded())); }
                    catch { /* skip unparseable cert */ }
                }

                using var chain = new SystemX509.X509Chain();
                chain.ChainPolicy.RevocationMode    = SystemX509.X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationTime  = (options.ValidationTime ?? DateTimeOffset.UtcNow).DateTime;
                foreach (var c in extraCerts) chain.ChainPolicy.ExtraStore.Add(c);

                bool valid = chain.Build(signingCert2);
                if (!valid)
                {
                    // All statuses from a failed Build() are errors — not warnings.
                    foreach (var status in chain.ChainStatus)
                    {
                        string info = status.StatusInformation?.Trim() ?? status.Status.ToString();
                        errors.Add($"Signing certificate chain error: {info}");
                    }
                    result.CertificateChainTrusted = false;
                }
                else
                {
                    result.CertificateChainTrusted = true;
                }
            }
            catch (Exception ex)
            {
                // Fail-closed: if trust validation was requested and threw, treat as untrusted.
                errors.Add($"Signing certificate chain check failed: {ex.Message}");
                result.CertificateChainTrusted = false;
            }
            finally
            {
                foreach (var c in extraCerts) c.Dispose();
            }
        }

        // -----------------------------------------------------------------------
        // Certificate period check
        // -----------------------------------------------------------------------

        private static void CheckCertificatePeriod(
            X509Certificate cert,
            PdfValidationOptions options,
            PdfSignatureValidationResult result,
            List<string> errors,
            List<string> warnings)
        {
            DateTimeOffset validAt    = options.ValidationTime ?? DateTimeOffset.UtcNow;
            DateTime       validAtUtc = validAt.UtcDateTime;

            if (cert.NotBefore > validAtUtc)
            {
                result.CertificatePeriodValid = false;
                errors.Add($"Signing certificate is not yet valid (NotBefore: {cert.NotBefore:u}).");
            }
            else if (cert.NotAfter < validAtUtc)
            {
                result.CertificatePeriodValid = false;
                errors.Add($"Signing certificate has expired (NotAfter: {cert.NotAfter:u}).");
            }
            else
            {
                result.CertificatePeriodValid = true;
            }
        }

        // -----------------------------------------------------------------------
        // Revocation check
        // -----------------------------------------------------------------------

        private void CheckRevocation(
            X509Certificate signingCertBc,
            IList<X509Certificate> allEmbedded,
            PdfValidationOptions options,
            PdfSignatureValidationResult result,
            List<string> warnings,
            DssData dss,
            byte[]? sigContents)
        {
            var errors = (List<string>)result.Errors;

            // Self-signed certificates cannot be revoked by a CA.
            if (signingCertBc.IssuerDN.Equivalent(signingCertBc.SubjectDN))
            {
                result.RevocationValid  = true;
                result.RevocationSource = RevocationSource.None;
                warnings.Add("Certificate is self-signed; revocation checking is not applicable.");
                return;
            }

            // Build the chain of certs to check.
            // When ValidateEntireCertificateChainRevocation, include intermediates; otherwise just the EE.
            var certsToCheck = BuildRevocationChain(signingCertBc, allEmbedded, dss,
                                                    options.ValidateEntireCertificateChainRevocation);

            if (certsToCheck.Count == 0)
            {
                // Non-self-signed EE cert but its issuer could not be located.
                // Revocation cannot be determined — do not silently pass.
                bool allowed = options.AllowUnknownRevocationStatus;
                string msg = $"Issuer certificate for '{signingCertBc.SubjectDN}' not found; " +
                             "revocation status cannot be determined.";
                if (allowed) warnings.Add(msg); else errors.Add(msg);
                result.RevocationValid = allowed;
                return;
            }

            bool allGood = true;

            foreach (var (subject, issuer) in certsToCheck)
            {
                bool certOk = CheckSingleCertRevocation(
                    subject, issuer, options, result, warnings, errors, dss, sigContents);
                if (!certOk) allGood = false;
            }

            result.RevocationValid = allGood;
        }

        private bool CheckSingleCertRevocation(
            X509Certificate subject,
            X509Certificate issuer,
            PdfValidationOptions options,
            PdfSignatureValidationResult result,
            List<string> warnings,
            List<string> errors,
            DssData dss,
            byte[]? sigContents)
        {
            // ── 1. Try embedded DSS data first ────────────────────────────
            DateTime refTime = (options.ValidationTime ?? DateTimeOffset.UtcNow).UtcDateTime;
            if (options.UseEmbeddedDss)
            {
                var dssResult = DssRevocationChecker.Check(subject, issuer, dss, sigContents, refTime);
                switch (dssResult.Status)
                {
                    case DssRevocationStatus.Good:
                        result.RevocationSource = RevocationSource.Dss;
                        return true;
                    case DssRevocationStatus.Revoked:
                        errors.Add($"Certificate '{subject.SubjectDN}' is revoked (DSS): {dssResult.Message}");
                        result.RevocationSource = RevocationSource.Dss;
                        return false;
                    // Unknown → fall through to online check.
                }
            }

            // ── 2. Online fallback (if allowed and clients provided) ──────
            if (!options.AllowOnlineRevocationFallback || (_ocspClient == null && _crlClient == null))
            {
                // Cannot check.
                bool allowed = options.AllowUnknownRevocationStatus;
                string msg = $"Revocation status unknown for '{subject.SubjectDN}'" +
                             (_ocspClient == null && _crlClient == null
                                 ? " — no OCSP/CRL client provided."
                                 : " — online fallback disabled.");
                if (allowed) warnings.Add(msg);
                else         errors.Add(msg);
                return allowed;
            }

            SystemX509.X509Certificate2? subj2  = null;
            SystemX509.X509Certificate2? issuer2 = null;
            try
            {
                subj2   = new SystemX509.X509Certificate2(subject.GetEncoded());
                issuer2 = new SystemX509.X509Certificate2(issuer.GetEncoded());

                RevocationStatus status = RevocationStatus.Unavailable;
                string? detail          = null;

                if (_ocspClient != null)
                {
                    try
                    {
                        var r = _ocspClient.CheckRevocationAsync(subj2, issuer2).GetAwaiter().GetResult();
                        status = r.Status;
                        detail = r.ErrorMessage;
                        if (status != RevocationStatus.Unavailable)
                            result.RevocationSource = RevocationSource.OcspOnline;
                    }
                    catch (Exception ex) { _logger.LogWarning("OCSP failed: {M}", ex.Message); }
                }

                if (status == RevocationStatus.Unavailable && _crlClient != null)
                {
                    try
                    {
                        var r = _crlClient.CheckRevocationAsync(subj2).GetAwaiter().GetResult();
                        status = r.Status;
                        detail = r.ErrorMessage;
                        if (status != RevocationStatus.Unavailable)
                            result.RevocationSource = RevocationSource.CrlOnline;
                    }
                    catch (Exception ex) { _logger.LogWarning("CRL failed: {M}", ex.Message); }
                }

                switch (status)
                {
                    case RevocationStatus.Good:
                        return true;
                    case RevocationStatus.Revoked:
                        errors.Add($"Certificate '{subject.SubjectDN}' has been revoked.");
                        return false;
                    default:
                        bool allowed = options.AllowUnknownRevocationStatus;
                        string msg = $"Revocation status unknown for '{subject.SubjectDN}'" +
                                     (detail != null ? $": {detail}" : ".");
                        if (allowed) warnings.Add(msg);
                        else         errors.Add(msg);
                        return allowed;
                }
            }
            finally
            {
                subj2?.Dispose();
                issuer2?.Dispose();
            }
        }

        /// <summary>
        /// Returns (subject, issuer) pairs to check.
        /// When <paramref name="fullChain"/> is false, only the EE signing cert is returned.
        /// When true, every non-root certificate in the chain is included.
        /// </summary>
        private static List<(X509Certificate Subject, X509Certificate Issuer)> BuildRevocationChain(
            X509Certificate signingCert,
            IList<X509Certificate> allEmbedded,
            DssData dss,
            bool fullChain)
        {
            var result = new List<(X509Certificate, X509Certificate)>();

            // Merge CMS-embedded certs with DSS certs for issuer resolution.
            var pool = allEmbedded.Concat(dss.Certs).ToList();

            X509Certificate? current = signingCert;
            var seen = new HashSet<string>();

            while (current != null)
            {
                string subjectKey = current.SubjectDN.ToString();
                if (!seen.Add(subjectKey)) break;

                // Self-signed = trust anchor, stop here.
                if (current.IssuerDN.Equivalent(current.SubjectDN)) break;

                X509Certificate? issuer = pool.FirstOrDefault(
                    c => c.SubjectDN.Equivalent(current!.IssuerDN));

                if (issuer == null) break;

                result.Add((current, issuer));

                if (!fullChain) break;  // only EE cert

                current = issuer;
            }

            return result;
        }

        // -----------------------------------------------------------------------
        // Finalise
        // -----------------------------------------------------------------------

        private static PdfSignatureValidationResult Finalise(
            PdfSignatureValidationResult result,
            PdfValidationOptions options)
        {
            var errors = (List<string>)result.Errors;

            // CertificateChainValid = period + (chain trust if checked)
            result.CertificateChainValid = result.CertificatePeriodValid && result.CertificateChainTrusted;

            // TimestampValid is computed structurally; also ensure RequireTimestamp is honoured.
            // (Errors were already added by CheckTimestamp when required but missing.)

            result.IsValid = result.DocumentIntegrityValid
                          && result.CmsSignatureValid
                          && result.RevocationValid
                          && result.TimestampValid
                          && errors.Count == 0;
            return result;
        }

        // -----------------------------------------------------------------------
        // ByteRange helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Validates the ByteRange array and assembles the signed-content bytes.
        /// Throws <see cref="ArgumentException"/> with a descriptive message for any
        /// constraint violation so the caller can add it as a validation error.
        /// </summary>
        private static byte[] AssembleByteRange(byte[] pdfBytes, long[] byteRange)
        {
            if (byteRange == null || byteRange.Length != 4)
                throw new ArgumentException("ByteRange must contain exactly 4 elements.");

            long b0 = byteRange[0], b1 = byteRange[1], b2 = byteRange[2], b3 = byteRange[3];

            if (b0 < 0 || b1 < 0 || b2 < 0 || b3 < 0)
                throw new ArgumentException(
                    $"ByteRange contains a negative value [{b0} {b1} {b2} {b3}].");

            // Check that individual segments fit within long arithmetic before adding.
            if (b0 > long.MaxValue - b1)
                throw new ArgumentException("ByteRange segment 0+1 overflows.");
            if (b2 > long.MaxValue - b3)
                throw new ArgumentException("ByteRange segment 2+3 overflows.");

            long end1 = b0 + b1; // end of first covered segment
            long end2 = b2 + b3; // end of second covered segment

            // Segments must not overlap: the gap [end1, b2) is the /Contents field.
            if (end1 > b2)
                throw new ArgumentException(
                    $"ByteRange segments overlap (end of seg1={end1} > start of seg2={b2}).");

            if (end2 > pdfBytes.Length)
                throw new ArgumentException(
                    $"ByteRange extends beyond file size (file={pdfBytes.Length}, required end={end2}).");

            // Check that total signed content fits in an array.
            long totalLen = b1 + b3;
            if (totalLen > int.MaxValue)
                throw new ArgumentException("ByteRange signed content exceeds addressable memory.");

            var result = new byte[(int)totalLen];
            Array.Copy(pdfBytes, (int)b0, result, 0,        (int)b1);
            Array.Copy(pdfBytes, (int)b2, result, (int)b1,  (int)b3);
            return result;
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        /// <summary>Constant-time byte array comparison to avoid timing side channels.</summary>
        private static bool CryptographicEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static byte[] ReadAllBytes(Stream stream)
        {
            if (stream is MemoryStream ms)
                return ms.ToArray();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
    }
}
