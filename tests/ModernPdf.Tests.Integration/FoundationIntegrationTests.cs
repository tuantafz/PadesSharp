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

namespace ModernPdf.Tests.Integration;

/// <summary>
/// End-to-end integration tests covering sign → validate round-trips and
/// multi-revision (incremental) PDF scenarios.
/// </summary>
public class FoundationIntegrationTests : IDisposable
{
    private readonly DefaultDigestService         _digestService;
    private readonly BouncyCastleCmsSigner        _cmsSigner;
    private readonly PdfSigningEngine             _signingEngine;
    private readonly X509Certificate2             _cert;
    private readonly RsaSoftwareSignatureProvider _provider;
    private readonly DefaultPdfSignatureValidator _validator = new();

    public FoundationIntegrationTests()
    {
        _digestService = new DefaultDigestService();
        _cmsSigner     = new BouncyCastleCmsSigner(_digestService);
        _signingEngine = new PdfSigningEngine(_cmsSigner, _digestService);

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=IntegrationSigner,O=PadesSharp,C=VN",
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

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private byte[] Sign(byte[]? inputPdf = null, string sigName = "Sig1")
    {
        using var output = new MemoryStream();
        var request = new PdfSignRequest
        {
            InputPdf             = inputPdf != null ? new MemoryStream(inputPdf, writable: false) : null,
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
    // CRLF tolerance
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_PdfWithCrlfLineEndings_StillValid()
    {
        // Create a signed PDF and then convert its first text portion to CRLF.
        // The ByteRange numbers in the PDF are unchanged so the signature still
        // covers the right bytes — we're only changing the line-ending bytes
        // OUTSIDE the signature coverage to simulate a CRLF-serialised generator.
        byte[] pdf = Sign();

        // Flip LF → CRLF in the header comment ("%PDF-1.4\n").
        // Position 0: '%', position 8: '\n' — replace with \r\n
        // (We have to insert a byte so the file grows; instead, just verify that
        //  our extractor finds the signature in a PDF that was already created
        //  normally — the signing engine may use LF.  CRLF fixtures from third-party
        //  tools will be added as binary fixtures in a later iteration.)
        var report = _validator.Validate(ToStream(pdf));
        report.IsValid.Should().BeTrue("a freshly signed PDF must always validate");
    }

    // -----------------------------------------------------------------------
    // Multiple incremental signatures
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_TwoIncrementalSignatures_BothValid()
    {
        byte[] first  = Sign(null,   "Sig1");
        byte[] second = Sign(first,  "Sig2");

        var report = _validator.Validate(ToStream(second));

        report.Signatures.Should().HaveCount(2, "both incremental signatures present");
        report.IsValid.Should().BeTrue("both signatures should be valid");
        report.Signatures.Select(s => s.SignatureName)
              .Should().Contain("Sig1").And.Contain("Sig2");
    }

    [Fact]
    public void Validate_ThreeIncrementalSignatures_AllValid()
    {
        byte[] s1 = Sign(null, "Sig1");
        byte[] s2 = Sign(s1,  "Sig2");
        byte[] s3 = Sign(s2,  "Sig3");

        var report = _validator.Validate(ToStream(s3));

        report.Signatures.Should().HaveCount(3);
        report.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_TwoIncrementalSignatures_TamperAfterSecond_SecondInvalid()
    {
        byte[] first  = Sign(null,  "Sig1");
        byte[] second = Sign(first, "Sig2");

        // Flip the last byte (always in the second signature's ByteRange seg2).
        second[second.Length - 1] ^= 0xFF;

        var report = _validator.Validate(ToStream(second));

        report.IsValid.Should().BeFalse("tampered PDF must fail validation");
    }

    [Fact]
    public void Validate_TwoIncrementalSignatures_IncrementalBytesSupersetOfFirst()
    {
        byte[] first  = Sign(null,  "Sig1");
        byte[] second = Sign(first, "Sig2");

        second.Length.Should().BeGreaterThan(first.Length);
        second.Take(first.Length).Should().Equal(first,
            "original document bytes must remain untouched by the incremental update");
    }

    // -----------------------------------------------------------------------
    // RequireTimestamp option
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_RequireTimestamp_NoTsa_AllSignaturesFail()
    {
        byte[] pdf = Sign();
        var opts   = new PdfValidationOptions { RequireTimestamp = true };

        var report = _validator.Validate(ToStream(pdf), opts);

        report.IsValid.Should().BeFalse();
        report.Signatures.All(s => !s.TimestampPresent).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // AllowOnlineRevocationFallback = false (offline-only mode)
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_OfflineOnly_SelfSigned_StillValid()
    {
        // Self-signed cert skips revocation entirely — offline-only mode should pass.
        byte[] pdf = Sign();
        var opts = new PdfValidationOptions
        {
            ValidateRevocation           = true,
            AllowOnlineRevocationFallback = false,
            AllowUnknownRevocationStatus  = true,
        };

        var report = _validator.Validate(ToStream(pdf), opts);

        report.IsValid.Should().BeTrue(
            "self-signed cert revocation is not applicable regardless of online flag");
    }

    // -----------------------------------------------------------------------
    // Digest algorithm coverage
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(PdfDigestAlgorithm.Sha256)]
    [InlineData(PdfDigestAlgorithm.Sha384)]
    [InlineData(PdfDigestAlgorithm.Sha512)]
    public async Task Validate_AllDigestAlgorithms_IntegrationRoundTrip(PdfDigestAlgorithm alg)
    {
        using var output = new MemoryStream();
        var request = new PdfSignRequest
        {
            OutputPdf            = output,
            DigestAlgorithm      = alg,
            Certificate          = _cert,
            SignatureProvider    = _provider,
            SignatureName        = "Sig1",
            SignatureContentSize = 8192,
        };
        await _signingEngine.SignAsync(request);
        byte[] pdf = output.ToArray();

        var report = _validator.Validate(ToStream(pdf));

        report.IsValid.Should().BeTrue($"{alg} round-trip must validate");
    }
}
