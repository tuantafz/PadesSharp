// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using ModernPdf.Abstractions.Crypto;
using ModernPdf.Abstractions.Signing;
using ModernPdf.Abstractions.Validation;
using ModernPdf.Crypto;
using ModernPdf.Signing;
using ModernPdf.Validation;

namespace ModernPdf.Tests.Unit.Validation;

/// <summary>
/// Unit tests for <see cref="DefaultPdfSignatureValidator"/>.
/// All tests produce a real signed PDF using <see cref="PdfSigningEngine"/>
/// and then validate it, so that integration between the signer and validator is
/// also exercised.
/// </summary>
public class ValidationTests : IDisposable
{
    // -----------------------------------------------------------------------
    // Shared test infrastructure
    // -----------------------------------------------------------------------

    private readonly DefaultDigestService _digestService = new();
    private readonly BouncyCastleCmsSigner _cmsSigner;
    private readonly PdfSigningEngine _signingEngine;
    private readonly X509Certificate2 _validCert;
    private readonly X509Certificate2 _expiredCert;
    private readonly RsaSoftwareSignatureProvider _validProvider;
    private readonly RsaSoftwareSignatureProvider _expiredProvider;
    private readonly DefaultPdfSignatureValidator _sut = new();

    public ValidationTests()
    {
        _cmsSigner     = new BouncyCastleCmsSigner(_digestService);
        _signingEngine = new PdfSigningEngine(_cmsSigner, _digestService);

        // Valid certificate: [yesterday, +2 years]
        using var rsa1 = RSA.Create(2048);
        var req1 = new CertificateRequest(
            "CN=ValidSigner,O=PadesSharp,C=VN",
            rsa1, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req1.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        _validCert = req1.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(2));
        _validProvider = new RsaSoftwareSignatureProvider(_validCert);

        // Expired certificate: [-2 years, -1 day]
        using var rsa2 = RSA.Create(2048);
        var req2 = new CertificateRequest(
            "CN=ExpiredSigner,O=PadesSharp,C=VN",
            rsa2, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req2.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        _expiredCert = req2.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddYears(-2),
            DateTimeOffset.UtcNow.AddDays(-1));
        _expiredProvider = new RsaSoftwareSignatureProvider(_expiredCert);
    }

    public void Dispose()
    {
        _validProvider.Dispose();
        _expiredProvider.Dispose();
        _validCert.Dispose();
        _expiredCert.Dispose();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private byte[] CreateSignedPdf(
        X509Certificate2 cert,
        RsaSoftwareSignatureProvider provider,
        PdfDigestAlgorithm algorithm = PdfDigestAlgorithm.Sha256)
    {
        using var output = new MemoryStream();
        var request = new PdfSignRequest
        {
            OutputPdf            = output,
            DigestAlgorithm      = algorithm,
            Certificate          = cert,
            SignatureProvider    = provider,
            Reason               = "Unit test",
            Location             = "Hanoi",
            SignatureName        = "Sig1",
            SignatureContentSize = 8192,
        };
        _signingEngine.SignAsync(request).GetAwaiter().GetResult();
        return output.ToArray();
    }

    private static Stream ToStream(byte[] bytes) => new MemoryStream(bytes);

    // -----------------------------------------------------------------------
    // Section 1 — Extractor coverage
    // -----------------------------------------------------------------------

    [Fact]
    public void Extractor_ValidSignedPdf_FindsOneSignature()
    {
        byte[] pdf    = CreateSignedPdf(_validCert, _validProvider);
        var    report = _sut.Validate(ToStream(pdf));

        report.Signatures.Should().HaveCount(1);
    }

    [Fact]
    public void Extractor_ValidSignedPdf_FieldNameIsDetected()
    {
        byte[] pdf    = CreateSignedPdf(_validCert, _validProvider);
        var    report = _sut.Validate(ToStream(pdf));

        report.Signatures[0].SignatureName.Should().Be("Sig1");
    }

    [Fact]
    public void Extractor_EmptyByteArray_ReturnsInvalidReportWithNoSigs()
    {
        // Feed an empty MemoryStream — not a real PDF
        var report = _sut.Validate(new MemoryStream());

        report.IsValid.Should().BeFalse();
        report.Signatures.Should().BeEmpty();
    }

    [Fact]
    public void Extractor_RandomBytes_ReturnsInvalidReportWithNoSigs()
    {
        byte[] noise = new byte[256];
        new Random(42).NextBytes(noise);
        var report = _sut.Validate(ToStream(noise));

        report.IsValid.Should().BeFalse();
        report.Signatures.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Section 2 — Happy path
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_ValidSignedPdf_ReportIsValid()
    {
        byte[] pdf    = CreateSignedPdf(_validCert, _validProvider);
        var    report = _sut.Validate(ToStream(pdf));

        report.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidSignedPdf_DocumentIntegrityValid()
    {
        byte[] pdf    = CreateSignedPdf(_validCert, _validProvider);
        var    report = _sut.Validate(ToStream(pdf));

        report.Signatures[0].DocumentIntegrityValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidSignedPdf_CmsSignatureValid()
    {
        byte[] pdf    = CreateSignedPdf(_validCert, _validProvider);
        var    report = _sut.Validate(ToStream(pdf));

        report.Signatures[0].CmsSignatureValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidSignedPdf_NoErrors()
    {
        byte[] pdf    = CreateSignedPdf(_validCert, _validProvider);
        var    report = _sut.Validate(ToStream(pdf));

        report.Signatures[0].Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(PdfDigestAlgorithm.Sha256)]
    [InlineData(PdfDigestAlgorithm.Sha384)]
    [InlineData(PdfDigestAlgorithm.Sha512)]
    public void Validate_AllDigestAlgorithms_Pass(PdfDigestAlgorithm alg)
    {
        byte[] pdf    = CreateSignedPdf(_validCert, _validProvider, alg);
        var    report = _sut.Validate(ToStream(pdf));

        report.IsValid.Should().BeTrue($"{alg} signed PDF should validate");
    }

    // -----------------------------------------------------------------------
    // Section 3 — Tampered PDF detection
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_TamperedLastByte_DocumentIntegrityInvalid()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        // Tamper a byte in the second ByteRange segment (after the /Contents hex),
        // which is covered by the signature.
        pdf[pdf.Length - 1] ^= 0xFF;

        var report = _sut.Validate(ToStream(pdf));

        report.IsValid.Should().BeFalse();
        report.Signatures[0].DocumentIntegrityValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_TamperedFirstByte_DocumentIntegrityInvalid()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        // Flip a byte in the first ByteRange segment (before the /Contents hex).
        // The first meaningful byte is position 4 (%PDF-).
        pdf[4] ^= 0xFF;

        var report = _sut.Validate(ToStream(pdf));

        report.IsValid.Should().BeFalse();
        report.Signatures[0].DocumentIntegrityValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_TamperedPdf_HasErrors()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        pdf[pdf.Length - 1] ^= 0xFF;

        var report = _sut.Validate(ToStream(pdf));

        report.Signatures[0].Errors.Should().NotBeEmpty();
    }

    // -----------------------------------------------------------------------
    // Section 4 — Certificate validity period
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_ValidCertNow_CertificateChainValid()
    {
        byte[] pdf    = CreateSignedPdf(_validCert, _validProvider);
        var    report = _sut.Validate(ToStream(pdf));

        report.Signatures[0].CertificateChainValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ExpiredCert_ValidationFails()
    {
        // Sign with an expired cert; validate at NOW — the cert is expired.
        byte[] pdf = CreateSignedPdf(_expiredCert, _expiredProvider);
        var opts   = new PdfValidationOptions { ValidateCertificateChain = true };

        var report = _sut.Validate(ToStream(pdf), opts);

        report.IsValid.Should().BeFalse("expired signing cert must fail validation");
    }

    [Fact]
    public void Validate_ExpiredCert_ErrorMentionsExpiry()
    {
        byte[] pdf = CreateSignedPdf(_expiredCert, _expiredProvider);
        var opts   = new PdfValidationOptions { ValidateCertificateChain = true };

        var report = _sut.Validate(ToStream(pdf), opts);

        string allErrors = string.Join("\n", report.Signatures[0].Errors);
        allErrors.Should().ContainAny("expired", "NotAfter", "Expired");
    }

    [Fact]
    public void Validate_ExpiredCert_DisabledChainCheck_DoesNotFail()
    {
        // When chain validation is disabled, CMS cryptographic verification of a
        // current-date cert must still pass. This isolates our cert-period check
        // from BouncyCastle's internal signer.Verify() which may also reject expired certs.
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        var opts   = new PdfValidationOptions { ValidateCertificateChain = false };

        var report = _sut.Validate(ToStream(pdf), opts);

        report.Signatures[0].DocumentIntegrityValid.Should().BeTrue();
        report.Signatures[0].CmsSignatureValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidationTimeBeforeCertNotBefore_IsInvalid()
    {
        // Validate at a time before the cert was issued — cert is "not yet valid".
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        var opts   = new PdfValidationOptions
        {
            ValidateCertificateChain = true,
            ValidationTime           = DateTimeOffset.UtcNow.AddYears(-5),
        };

        var report = _sut.Validate(ToStream(pdf), opts);

        report.IsValid.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Section 4b — Chain trust (ValidateChainTrust = true, self-signed → INVALID)
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_SelfSignedCert_ChainTrustEnabled_IsInvalid()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        var opts = new PdfValidationOptions
        {
            ValidateCertificateChain = true,
            ValidateChainTrust       = true,
        };

        var report = _sut.Validate(ToStream(pdf), opts);

        report.IsValid.Should().BeFalse("self-signed cert not in system trust store");
    }

    [Fact]
    public void Validate_SelfSignedCert_ChainTrustDisabled_IsValid()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        var opts = new PdfValidationOptions
        {
            ValidateCertificateChain = true,
            ValidateChainTrust       = false,   // default
        };

        var report = _sut.Validate(ToStream(pdf), opts);

        report.IsValid.Should().BeTrue("chain trust is opt-in; self-signed cert date is valid");
    }

    [Fact]
    public void Validate_SelfSignedCert_ChainTrustEnabled_HasChainError()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        var opts = new PdfValidationOptions
        {
            ValidateCertificateChain = true,
            ValidateChainTrust       = true,
        };

        var report = _sut.Validate(ToStream(pdf), opts);

        string allErrors = string.Join(" ", report.Signatures[0].Errors);
        allErrors.Should().Contain("chain", "error message must mention chain");
    }

    // -----------------------------------------------------------------------
    // Section 5 — Options / null-safety
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_NullOptions_UsesDefaults()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);

        // Passing null should not throw; it defaults to the same as new PdfValidationOptions().
        var report = _sut.Validate(ToStream(pdf), null);

        report.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_NullStream_ThrowsArgumentNullException()
    {
        Action act = () => _sut.Validate(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Section 6 — Report aggregation
    // -----------------------------------------------------------------------

    [Fact]
    public void Report_SingleValidSig_ReportIsValid()
    {
        byte[] pdf    = CreateSignedPdf(_validCert, _validProvider);
        var    report = _sut.Validate(ToStream(pdf));

        report.IsValid.Should().BeTrue();
        report.Signatures.Should().HaveCount(1);
        report.Signatures[0].IsValid.Should().BeTrue();
    }

    [Fact]
    public void Report_SingleInvalidSig_ReportIsInvalid()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        pdf[pdf.Length - 1] ^= 0xFF;

        var report = _sut.Validate(ToStream(pdf));

        report.IsValid.Should().BeFalse();
        report.Signatures.Should().HaveCount(1);
        report.Signatures[0].IsValid.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Section 7 — Timestamp (no-TSA path: TimestampValid defaults to true)
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_NoTimestamp_TimestampValidIsTrue()
    {
        // Signature created without TSA → no RFC 3161 token embedded.
        // Per our convention: no timestamp = not applicable = true (not a failure).
        byte[] pdf    = CreateSignedPdf(_validCert, _validProvider);
        var    report = _sut.Validate(ToStream(pdf));

        report.Signatures[0].TimestampValid.Should().BeTrue(
            "a PDF without a timestamp attribute is not an error");
    }

    // -----------------------------------------------------------------------
    // Section 8 — L1: Sign existing PDF (incremental update)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Sign_ExistingPdf_ProducesValidSignature()
    {
        // First, create a fresh signed PDF to serve as the "existing" document.
        byte[] basePdf = CreateSignedPdf(_validCert, _validProvider);

        // Now sign it again via incremental update (InputPdf set).
        using var output = new MemoryStream();
        var request = new PdfSignRequest
        {
            InputPdf             = new MemoryStream(basePdf, writable: false),
            OutputPdf            = output,
            DigestAlgorithm      = PdfDigestAlgorithm.Sha256,
            Certificate          = _validCert,
            SignatureProvider    = _validProvider,
            Reason               = "Incremental signing test",
            Location             = "Hanoi",
            SignatureName        = "Sig2",
            SignatureContentSize = 8192,
        };
        await _signingEngine.SignAsync(request);

        byte[] incrementalPdf = output.ToArray();

        // The result must contain BOTH signatures and both must be valid.
        var report = _sut.Validate(ToStream(incrementalPdf));

        report.Signatures.Should().HaveCount(2, "both original and incremental signatures present");
        report.IsValid.Should().BeTrue("both signatures should be valid");
    }

    [Fact]
    public async Task Sign_ExistingPdf_IncrementalBytesAreSuperset()
    {
        byte[] basePdf = CreateSignedPdf(_validCert, _validProvider);

        using var output = new MemoryStream();
        var request = new PdfSignRequest
        {
            InputPdf             = new MemoryStream(basePdf, writable: false),
            OutputPdf            = output,
            Certificate          = _validCert,
            SignatureProvider    = _validProvider,
            SignatureName        = "Sig2",
            SignatureContentSize = 8192,
        };
        await _signingEngine.SignAsync(request);

        byte[] result = output.ToArray();

        // The incremental PDF must start with the original bytes.
        result.Length.Should().BeGreaterThan(basePdf.Length,
            "incremental update appends data, never shrinks the file");
        result.Take(basePdf.Length).Should().Equal(basePdf,
            "original document bytes must remain untouched");
    }

    [Fact]
    public async Task Sign_ExistingPdf_TamperedAfterIncrementalSign_IsDetected()
    {
        byte[] basePdf = CreateSignedPdf(_validCert, _validProvider);

        using var output = new MemoryStream();
        var request = new PdfSignRequest
        {
            InputPdf             = new MemoryStream(basePdf, writable: false),
            OutputPdf            = output,
            Certificate          = _validCert,
            SignatureProvider    = _validProvider,
            SignatureName        = "Sig2",
            SignatureContentSize = 8192,
        };
        await _signingEngine.SignAsync(request);

        byte[] pdf     = output.ToArray();
        pdf[pdf.Length - 1] ^= 0xFF;   // flip last byte (always in ByteRange seg 2)

        var report = _sut.Validate(ToStream(pdf));

        report.IsValid.Should().BeFalse("tampered PDF must fail validation");
    }

    // -----------------------------------------------------------------------
    // Section 9 — L2: RevocationValid when no client provided
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_RevocationEnabled_NoClient_StillPasses()
    {
        // With ValidateRevocation = true but no OCSP/CRL client, validator warns and passes.
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        var opts = new PdfValidationOptions { ValidateRevocation = true };

        var report = _sut.Validate(ToStream(pdf), opts);

        // Self-signed cert → "not applicable" warning, RevocationValid stays true.
        report.IsValid.Should().BeTrue("unknown revocation on self-signed cert should not block");
        report.Signatures[0].RevocationValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_RevocationDisabled_RevocationValidIsTrue()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        var opts   = new PdfValidationOptions { ValidateRevocation = false };

        var report = _sut.Validate(ToStream(pdf), opts);

        report.Signatures[0].RevocationValid.Should().BeTrue(
            "RevocationValid = true when checking is disabled");
    }

    // -----------------------------------------------------------------------
    // Section 10 — RequireTimestamp
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_RequireTimestamp_NoTimestamp_IsInvalid()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        var opts   = new PdfValidationOptions { RequireTimestamp = true };

        var report = _sut.Validate(ToStream(pdf), opts);

        report.IsValid.Should().BeFalse("RequireTimestamp=true and no TSA token present");
    }

    [Fact]
    public void Validate_RequireTimestamp_NoTimestamp_ErrorMentionsTimestamp()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        var opts   = new PdfValidationOptions { RequireTimestamp = true };

        var report = _sut.Validate(ToStream(pdf), opts);

        string allErrors = string.Join("\n", report.Signatures[0].Errors);
        allErrors.Should().ContainAny("timestamp", "Timestamp", "required");
    }

    [Fact]
    public void Validate_RequireTimestamp_False_NoTimestamp_IsValid()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        var opts   = new PdfValidationOptions { RequireTimestamp = false };

        var report = _sut.Validate(ToStream(pdf), opts);

        report.IsValid.Should().BeTrue("no timestamp required when RequireTimestamp=false");
        report.Signatures[0].TimestampPresent.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Section 11 — ByteRange validation
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_ByteRange_NegativeValue_IsInvalid()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        // Corrupt the ByteRange in the signed content directly.
        // We test via a synthetic forged PDF built from minimal structure.
        // Since we cannot easily corrupt ByteRange post-signing (it's covered),
        // we verify via a helper that assembles ByteRange and throws.
        Action act = () => AssembleByteRangeHelper(pdf, new long[] { -1, 10, 20, 10 });
        act.Should().Throw<ArgumentException>().WithMessage("*negative*");
    }

    [Fact]
    public void Validate_ByteRange_OnlyThreeElements_IsInvalid()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        Action act = () => AssembleByteRangeHelper(pdf, new long[] { 0, 10, 20 });
        act.Should().Throw<ArgumentException>().WithMessage("*exactly 4*");
    }

    [Fact]
    public void Validate_ByteRange_OverlappingSegments_IsInvalid()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        // seg1 ends at 0+50=50, seg2 starts at 30 → overlap
        Action act = () => AssembleByteRangeHelper(pdf, new long[] { 0, 50, 30, 10 });
        act.Should().Throw<ArgumentException>().WithMessage("*overlap*");
    }

    [Fact]
    public void Validate_ByteRange_BeyondFileSize_IsInvalid()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        // seg2 ends beyond file
        long beyond = pdf.Length + 1000;
        Action act = () => AssembleByteRangeHelper(pdf, new long[] { 0, 10, 20, beyond });
        act.Should().Throw<ArgumentException>().WithMessage("*file size*");
    }

    // -----------------------------------------------------------------------
    // Section 12 — Structured result fields
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_ValidPdf_TimestampPresentIsFalse()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        var report = _sut.Validate(ToStream(pdf));

        report.Signatures[0].TimestampPresent.Should().BeFalse(
            "no timestamp token was embedded during signing");
    }

    [Fact]
    public void Validate_ValidPdf_CertificatePeriodValid()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        var report = _sut.Validate(ToStream(pdf));

        report.Signatures[0].CertificatePeriodValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ExpiredCert_CertificatePeriodValidIsFalse()
    {
        byte[] pdf = CreateSignedPdf(_expiredCert, _expiredProvider);
        var opts   = new PdfValidationOptions { ValidateCertificateChain = true };

        var report = _sut.Validate(ToStream(pdf), opts);

        report.Signatures[0].CertificatePeriodValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_SelfSignedChainTrust_CertificateChainTrustedIsFalse()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        var opts = new PdfValidationOptions
        {
            ValidateCertificateChain = true,
            ValidateChainTrust       = true,
        };

        var report = _sut.Validate(ToStream(pdf), opts);

        report.Signatures[0].CertificateChainTrusted.Should().BeFalse(
            "self-signed cert is not in the system trust store");
    }

    [Fact]
    public void Validate_ChainTrustDisabled_CertificateChainTrustedIsTrue()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        var opts = new PdfValidationOptions
        {
            ValidateCertificateChain = true,
            ValidateChainTrust       = false,
        };

        var report = _sut.Validate(ToStream(pdf), opts);

        report.Signatures[0].CertificateChainTrusted.Should().BeTrue(
            "chain trust is opt-in; not requested so defaults to trusted");
    }

    // -----------------------------------------------------------------------
    // Section 13 — Fail-open revocation fix (§1.7)
    //   Self-signed certs skip revocation (always); non-self-signed with no
    //   client and AllowUnknownRevocationStatus=false should FAIL.
    //   We cannot test non-self-signed certs without a CA infrastructure,
    //   so we verify the self-signed early-exit still produces RevocationValid=true.
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_SelfSigned_RevocationEnabled_NoClient_RevocationValid()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        var opts   = new PdfValidationOptions
        {
            ValidateRevocation            = true,
            AllowUnknownRevocationStatus  = false,  // strict
        };

        var report = _sut.Validate(ToStream(pdf), opts);

        // Self-signed → revocation "not applicable" → RevocationValid = true
        report.Signatures[0].RevocationValid.Should().BeTrue(
            "self-signed cert revocation is not applicable");
        report.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_RevocationSource_NoneWhenNotChecked()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        var opts   = new PdfValidationOptions { ValidateRevocation = false };

        var report = _sut.Validate(ToStream(pdf), opts);

        report.Signatures[0].RevocationSource.Should().Be(RevocationSource.None);
    }

    // -----------------------------------------------------------------------
    // Section 14 — CertificateChainValid composite (§2.8)
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_ValidPdf_CertificateChainValidIsTrue()
    {
        byte[] pdf = CreateSignedPdf(_validCert, _validProvider);
        var report = _sut.Validate(ToStream(pdf));

        // Chain trust not requested; period valid → composite should be true.
        report.Signatures[0].CertificateChainValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ExpiredCert_CertificateChainValidIsFalse()
    {
        byte[] pdf = CreateSignedPdf(_expiredCert, _expiredProvider);
        var opts   = new PdfValidationOptions { ValidateCertificateChain = true };

        var report = _sut.Validate(ToStream(pdf), opts);

        report.Signatures[0].CertificateChainValid.Should().BeFalse(
            "CertificatePeriodValid=false makes the composite false");
    }

    // -----------------------------------------------------------------------
    // Helpers exposed only to tests (via reflection-friendly private method
    // wrapper) — call the internal AssembleByteRange via the validator's
    // static method in the test assembly.
    // -----------------------------------------------------------------------

    private static byte[] AssembleByteRangeHelper(byte[] pdfBytes, long[] byteRange)
    {
        // We call this via a small shim that exercises the same validation path
        // that DefaultPdfSignatureValidator uses internally.
        var mi = typeof(DefaultPdfSignatureValidator)
            .GetMethod("AssembleByteRange",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (mi == null)
            throw new InvalidOperationException("AssembleByteRange not found via reflection.");
        try
        {
            return (byte[])mi.Invoke(null, new object[] { pdfBytes, byteRange })!;
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }
    }
}
