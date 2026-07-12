// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.IO;
using FluentAssertions;
using ModernPdf.Abstractions.Validation;
using ModernPdf.Validation;

namespace ModernPdf.Tests.Compatibility;

/// <summary>
/// Interoperability tests: verifies that <see cref="DefaultPdfSignatureValidator"/>
/// can correctly parse and validate PDFs produced by third-party tools
/// (Adobe Acrobat/Reader, iText 7), not just PDFs signed by PadesSharp itself.
/// Fixtures live in tests/Resources and are copied to the test output directory.
/// </summary>
public class InteroperabilityTests
{
    private static string ResourcePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Resources", fileName);

    private static Stream OpenResource(string fileName) =>
        File.OpenRead(ResourcePath(fileName));

    // -----------------------------------------------------------------------
    // iText 7 — fully valid signature with embedded timestamp
    // -----------------------------------------------------------------------

    [Fact]
    public void Interop_ITextSignedPdf_IsFullyValid()
    {
        using var stream = OpenResource("itext_sample_signed.pdf");
        var sut = new DefaultPdfSignatureValidator();

        var report = sut.Validate(stream);

        report.Signatures.Should().HaveCount(1, "iText produced exactly one signature field");
        var sig = report.Signatures[0];
        sig.DocumentIntegrityValid.Should().BeTrue("byte-range digest must match for an untouched iText PDF");
        sig.CmsSignatureValid.Should().BeTrue("PadesSharp must verify CMS signatures produced by iText 7");
        sig.CertificateChainValid.Should().BeTrue();
        sig.TimestampPresent.Should().BeTrue("the iText fixture embeds an RFC 3161 timestamp");
        sig.TimestampValid.Should().BeTrue();
        sig.Errors.Should().BeEmpty();
        report.IsValid.Should().BeTrue("a genuine, untampered iText-signed PDF must validate cleanly");
    }

    // -----------------------------------------------------------------------
    // Adobe Acrobat/Reader — cryptographically valid, revocation unresolvable offline
    // -----------------------------------------------------------------------

    [Fact]
    public void Interop_AdobeSignedPdf_CryptographicFieldsAreValid()
    {
        using var stream = OpenResource("abode_sample_signed.pdf");
        var sut = new DefaultPdfSignatureValidator();

        var report = sut.Validate(stream);

        report.Signatures.Should().HaveCount(1, "the Adobe fixture has exactly one signature field");
        var sig = report.Signatures[0];
        sig.DocumentIntegrityValid.Should().BeTrue("PadesSharp must parse Adobe's byte-range layout correctly");
        sig.CmsSignatureValid.Should().BeTrue("PadesSharp must verify CMS signatures produced by Adobe");
        sig.CertificatePeriodValid.Should().BeTrue();
        sig.CertificateChainValid.Should().BeTrue();
        sig.TimestampPresent.Should().BeTrue("Adobe embeds an RFC 3161 timestamp by default");
        sig.TimestampValid.Should().BeTrue("PadesSharp must verify Adobe's embedded TSA timestamp token");
    }

    [Fact]
    public void Interop_AdobeSignedPdf_DefaultOptions_RevocationUnknownMakesItInvalid()
    {
        using var stream = OpenResource("abode_sample_signed.pdf");
        var sut = new DefaultPdfSignatureValidator();

        // No OCSP/CRL client injected and no offline DSS revocation data embedded:
        // strict (fail-closed) defaults correctly refuse to call this "valid".
        var report = sut.Validate(stream);

        report.Signatures[0].RevocationValid.Should().BeFalse();
        report.Signatures[0].Errors.Should().ContainSingle(e => e.Contains("Revocation status unknown"));
        report.IsValid.Should().BeFalse("fail-closed default must not silently treat unresolved revocation as valid");
    }

    [Fact]
    public void Interop_AdobeSignedPdf_AllowUnknownRevocation_IsFullyValid()
    {
        using var stream = OpenResource("abode_sample_signed.pdf");
        var sut = new DefaultPdfSignatureValidator();
        var options = new PdfValidationOptions { AllowUnknownRevocationStatus = true };

        var report = sut.Validate(stream, options);

        report.IsValid.Should().BeTrue(
            "once unresolved revocation is explicitly allowed, a genuine Adobe signature must validate");
    }

    // -----------------------------------------------------------------------
    // A PDF from a failed/aborted signing attempt — no signature embedded at all
    // -----------------------------------------------------------------------

    [Fact]
    public void Interop_FailedSigningAttemptPdf_NoSignaturesFound_ReportsInvalid()
    {
        using var stream = OpenResource("sample_failed.pdf");
        var sut = new DefaultPdfSignatureValidator();

        var report = sut.Validate(stream);

        report.Signatures.Should().BeEmpty("this fixture has no /Sig or /ByteRange at all");
        report.IsValid.Should().BeFalse();
    }
}
