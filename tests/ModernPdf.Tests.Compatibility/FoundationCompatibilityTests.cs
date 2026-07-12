// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using ModernPdf.Abstractions.Crypto;
using ModernPdf.Abstractions.Signing;
using ModernPdf.Abstractions.Validation;
using ModernPdf.Crypto;
using ModernPdf.Signing;
using ModernPdf.Validation;

namespace ModernPdf.Tests.Compatibility;

/// <summary>
/// Compatibility tests: verifies that PDFs produced by the current
/// signing engine can be validated by <see cref="DefaultPdfSignatureValidator"/>,
/// and that new validation options do not regress on existing valid PDFs.
/// </summary>
public class FoundationCompatibilityTests : IDisposable
{
    private readonly DefaultDigestService         _digestService;
    private readonly BouncyCastleCmsSigner        _cmsSigner;
    private readonly PdfSigningEngine             _signingEngine;
    private readonly X509Certificate2             _cert;
    private readonly RsaSoftwareSignatureProvider _provider;

    public FoundationCompatibilityTests()
    {
        _digestService = new DefaultDigestService();
        _cmsSigner     = new BouncyCastleCmsSigner(_digestService);
        _signingEngine = new PdfSigningEngine(_cmsSigner, _digestService);

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=CompatSigner,O=PadesSharp,C=VN",
            rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        _cert     = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        _provider = new RsaSoftwareSignatureProvider(_cert);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _cert.Dispose();
    }

    private byte[] CreateSignedPdf(string sigName = "Sig1")
    {
        using var output = new MemoryStream();
        var request = new PdfSignRequest
        {
            OutputPdf            = output,
            DigestAlgorithm      = PdfDigestAlgorithm.Sha256,
            Certificate          = _cert,
            SignatureProvider    = _provider,
            SignatureName        = sigName,
            SignatureContentSize = 8192,
        };
        _signingEngine.SignAsync(request).GetAwaiter().GetResult();
        return output.ToArray();
    }

    private static Stream ToStream(byte[] b) => new MemoryStream(b);

    // -----------------------------------------------------------------------
    // Default options backward-compatibility
    // -----------------------------------------------------------------------

    [Fact]
    public void Compatibility_DefaultOptions_ValidPdf_StillValid()
    {
        byte[] pdf = CreateSignedPdf();
        var    sut = new DefaultPdfSignatureValidator();

        var report = sut.Validate(ToStream(pdf));

        report.IsValid.Should().BeTrue("default options must not regress existing valid PDFs");
    }

    [Fact]
    public void Compatibility_NewOptions_AllDefaults_NoRegression()
    {
        byte[] pdf = CreateSignedPdf();
        var    sut = new DefaultPdfSignatureValidator();
        // Explicitly set all new options to their documented defaults.
        var opts = new PdfValidationOptions
        {
            RequireTimestamp                     = false,
            ValidateTimestampMessageImprint      = true,
            ValidateTimestampCertificatePeriod   = true,
            ValidateTimestampChainTrust          = false,
            ValidateTimestampRevocation          = false,
            UseEmbeddedDss                       = true,
            AllowOnlineRevocationFallback        = true,
            ValidateEntireCertificateChainRevocation = false,
        };

        var report = sut.Validate(ToStream(pdf), opts);

        report.IsValid.Should().BeTrue("new options at defaults must not break existing valid PDFs");
    }

    // -----------------------------------------------------------------------
    // Structured result fields — new fields do not default to false
    // -----------------------------------------------------------------------

    [Fact]
    public void Compatibility_ValidPdf_AllResultBoolsAreTrue()
    {
        byte[] pdf = CreateSignedPdf();
        var    sut = new DefaultPdfSignatureValidator();

        var report = sut.Validate(ToStream(pdf));
        var sig    = report.Signatures[0];

        sig.DocumentIntegrityValid.Should().BeTrue();
        sig.CmsSignatureValid.Should().BeTrue();
        sig.CertificatePeriodValid.Should().BeTrue();
        sig.CertificateChainTrusted.Should().BeTrue();
        sig.CertificateChainValid.Should().BeTrue();
        sig.RevocationValid.Should().BeTrue();
        sig.TimestampMessageImprintValid.Should().BeTrue();
        sig.TimestampCertificateValid.Should().BeTrue();
        sig.TimestampChainTrusted.Should().BeTrue();
        sig.TimestampRevocationValid.Should().BeTrue();
        sig.TimestampValid.Should().BeTrue();
        sig.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Compatibility_ValidPdf_ErrorsAndWarningsStructure()
    {
        byte[] pdf = CreateSignedPdf();
        var    sut = new DefaultPdfSignatureValidator();

        var report = sut.Validate(ToStream(pdf));
        var sig    = report.Signatures[0];

        sig.Errors.Should().BeEmpty("no validation errors for a valid self-signed PDF");
        // Self-signed cert produces a revocation warning (not applicable).
        // That's acceptable — it's not an error.
    }

    // -----------------------------------------------------------------------
    // RequireTimestamp opt-in does not affect existing calls without the option
    // -----------------------------------------------------------------------

    [Fact]
    public void Compatibility_RequireTimestampDefault_IsOptIn()
    {
        byte[] pdf = CreateSignedPdf();
        var    sut = new DefaultPdfSignatureValidator();
        var    opts = new PdfValidationOptions();

        // RequireTimestamp defaults to false — no timestamp should not be an error.
        opts.RequireTimestamp.Should().BeFalse("opt-in default must not break callers");

        var report = sut.Validate(ToStream(pdf), opts);
        report.IsValid.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // TimestampValid defaults to true when no timestamp is present
    // -----------------------------------------------------------------------

    [Fact]
    public void Compatibility_NoTimestamp_TimestampValidDefaultsTrue()
    {
        byte[] pdf = CreateSignedPdf();
        var    sut = new DefaultPdfSignatureValidator();

        var report = sut.Validate(ToStream(pdf));

        report.Signatures[0].TimestampValid.Should().BeTrue(
            "backward-compatible: absent timestamp must not be treated as a failure");
    }
}
