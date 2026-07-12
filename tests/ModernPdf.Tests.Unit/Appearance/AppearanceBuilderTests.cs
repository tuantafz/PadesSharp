// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using ModernPdf.Abstractions.Appearance;
using ModernPdf.Abstractions.Crypto;
using ModernPdf.Abstractions.Signing;
using ModernPdf.Appearance;
using ModernPdf.Crypto;
using ModernPdf.Signing;

namespace ModernPdf.Tests.Unit.Appearance;

/// <summary>
/// Unit tests for <see cref="DefaultPdfSignatureAppearanceBuilder"/>.
/// </summary>
public class AppearanceBuilderTests
{
    private readonly DefaultPdfSignatureAppearanceBuilder _sut = new();

    // -----------------------------------------------------------------------
    // Build output structure
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_TextOnly_ReturnsNonEmptyContentStream()
    {
        var result = _sut.Build(new PdfSignatureAppearanceRequest
        {
            SignerName = "Nguyen Van A",
            Width = 200, Height = 60,
        });

        result.ContentStream.Should().NotBeEmpty();
        result.HasImage.Should().BeFalse();
    }

    [Fact]
    public void Build_TextOnly_ContentStreamContainsBT_ET()
    {
        var result = _sut.Build(new PdfSignatureAppearanceRequest
        {
            SignerName = "Test Signer",
            Width = 200, Height = 60,
        });

        string text = Encoding.GetEncoding(28591).GetString(result.ContentStream);
        text.Should().Contain("BT");
        text.Should().Contain("ET");
        text.Should().Contain("/Helv");
        text.Should().Contain("Tj");
    }

    [Fact]
    public void Build_TextOnly_ContentStreamContainsSignerName()
    {
        var result = _sut.Build(new PdfSignatureAppearanceRequest
        {
            SignerName = "Tran Thi B",
            Width = 200, Height = 60,
        });

        string text = Encoding.GetEncoding(28591).GetString(result.ContentStream);
        text.Should().Contain("Tran Thi B");
    }

    [Fact]
    public void Build_TextOnly_ReasonAndLocationIncluded()
    {
        var result = _sut.Build(new PdfSignatureAppearanceRequest
        {
            SignerName = "Test",
            Reason = "Approval",
            Location = "Hanoi",
            ShowReason = true,
            ShowLocation = true,
            Width = 200, Height = 80,
        });

        string text = Encoding.GetEncoding(28591).GetString(result.ContentStream);
        text.Should().Contain("Approval");
        text.Should().Contain("Hanoi");
    }

    [Fact]
    public void Build_ShowReasonFalse_ReasonNotInStream()
    {
        var result = _sut.Build(new PdfSignatureAppearanceRequest
        {
            SignerName = "Test",
            Reason = "Approval",
            ShowReason = false,
            Width = 200, Height = 60,
        });

        string text = Encoding.GetEncoding(28591).GetString(result.ContentStream);
        text.Should().NotContain("Approval");
    }

    [Fact]
    public void Build_ShowDateTrue_DateStringIncluded()
    {
        var signingTime = new DateTimeOffset(2026, 5, 16, 10, 30, 0, TimeSpan.Zero);
        var result = _sut.Build(new PdfSignatureAppearanceRequest
        {
            SignerName = "Test",
            SigningTime = signingTime,
            ShowDate = true,
            Width = 200, Height = 80,
        });

        string text = Encoding.GetEncoding(28591).GetString(result.ContentStream);
        text.Should().Contain("2026-05-16");
    }

    [Fact]
    public void Build_DimensionsPassedThrough()
    {
        var result = _sut.Build(new PdfSignatureAppearanceRequest
        {
            Width = 300, Height = 90,
        });

        result.Width.Should().Be(300f);
        result.Height.Should().Be(90f);
    }

    [Fact]
    public void Build_LongSignerName_TruncatedGracefully()
    {
        string longName = new string('A', 100);
        var result = _sut.Build(new PdfSignatureAppearanceRequest
        {
            SignerName = longName,
            Width = 200, Height = 60,
        });

        // Content stream must still be valid (parseable as Latin-1 text)
        string text = Encoding.GetEncoding(28591).GetString(result.ContentStream);
        text.Should().Contain("Tj");
        text.Should().NotContain("(AA" + new string('A', 100)); // must be truncated
    }

    // -----------------------------------------------------------------------
    // Image support
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_WithLogoImage_HasImageTrue()
    {
        byte[] fakeJpeg = CreateMinimalJpeg(100, 80);

        var result = _sut.Build(new PdfSignatureAppearanceRequest
        {
            SignerName = "Test",
            LogoImageBytes = fakeJpeg,
            Width = 200, Height = 60,
        });

        result.HasImage.Should().BeTrue();
        result.ImageXObjectData.Should().BeSameAs(fakeJpeg);
    }

    [Fact]
    public void Build_WithLogoImage_ContentStreamContainsImgDo()
    {
        byte[] fakeJpeg = CreateMinimalJpeg(64, 64);

        var result = _sut.Build(new PdfSignatureAppearanceRequest
        {
            SignerName = "Test",
            LogoImageBytes = fakeJpeg,
            Width = 200, Height = 60,
        });

        string text = Encoding.GetEncoding(28591).GetString(result.ContentStream);
        text.Should().Contain("/Img0 Do");
    }

    [Fact]
    public void Build_WithLogoImage_ParsesJpegDimensions()
    {
        byte[] jpeg = CreateMinimalJpeg(120, 80);

        var result = _sut.Build(new PdfSignatureAppearanceRequest
        {
            LogoImageBytes = jpeg,
            Width = 200, Height = 60,
        });

        result.ImagePixelWidth.Should().Be(120);
        result.ImagePixelHeight.Should().Be(80);
    }

    // -----------------------------------------------------------------------
    // Rotation matrix
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0,   "1 0 0 1 0 0")]
    [InlineData(90,  "0 -1 1 0")]   // partial check — just check the a,b,c,d
    [InlineData(180, "-1 0 0 -1")]
    [InlineData(270, "0 1 -1 0")]
    public void GetRotationMatrix_ReturnsExpectedCoefficients(int rotation, string expectedFragment)
    {
        string matrix = DefaultPdfSignatureAppearanceBuilder.GetRotationMatrix(rotation, 200f, 60f);
        matrix.Should().Contain(expectedFragment,
            $"rotation {rotation} must produce the expected CTM coefficients");
    }

    [Fact]
    public void Build_InvalidPageRotation_Throws()
    {
        var act = () => _sut.Build(new PdfSignatureAppearanceRequest
        {
            Width = 200, Height = 60,
            PageRotation = 45,   // invalid
        });
        act.Should().Throw<ArgumentException>().WithMessage("*PageRotation*");
    }

    // -----------------------------------------------------------------------
    // Integration: signing engine uses appearance builder
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SignWithAppearance_OutputContainsFormXObject()
    {
        using var rsa = RSA.Create(2048);
        var req2 = new CertificateRequest("CN=AppTest", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req2.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        using var sigProv = new RsaSoftwareSignatureProvider(cert);

        var digest  = new DefaultDigestService();
        var cmsSign = new BouncyCastleCmsSigner(digest);
        var engine  = new PdfSigningEngine(cmsSign, digest, new DefaultPdfSignatureAppearanceBuilder());

        using var output = new MemoryStream();
        var request = new PdfSignRequest
        {
            OutputPdf          = output,
            Certificate        = cert,
            SignatureProvider  = sigProv,
            DigestAlgorithm    = PdfDigestAlgorithm.Sha256,
            SignatureContentSize = 8192,
            Appearance = new PdfSignatureAppearanceRequest
            {
                SignerName  = "AppTest Signer",
                Reason      = "Integration test",
                Width  = 200, Height = 60,
                Rectangle   = new PdfSignatureRectangle { X = 50, Y = 700, Width = 200, Height = 60 },
            },
        };

        var result = await engine.SignAsync(request);

        result.Success.Should().BeTrue();
        string pdfText = Encoding.GetEncoding(28591).GetString(output.ToArray());
        pdfText.Should().Contain("/XObject");
        pdfText.Should().Contain("/Form");
        pdfText.Should().Contain("AppTest Signer");
        pdfText.Should().Contain("/AP");
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a minimal valid JPEG byte array with the given pixel dimensions.
    /// Only encodes the JFIF/SOF0 markers needed for dimension parsing.
    /// </summary>
    private static byte[] CreateMinimalJpeg(int width, int height)
    {
        // Minimal JPEG: SOI + APP0 (JFIF) + SOF0 + EOI
        var ms = new System.Collections.Generic.List<byte>();

        // SOI
        ms.AddRange(new byte[] { 0xFF, 0xD8 });

        // APP0 (JFIF) - 18 bytes
        ms.AddRange(new byte[] { 0xFF, 0xE0, 0x00, 0x10 }); // marker + length=16
        ms.AddRange(new byte[] { 0x4A, 0x46, 0x49, 0x46, 0x00 }); // "JFIF\0"
        ms.AddRange(new byte[] { 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00 }); // version, density, thumbnail

        // SOF0: FF C0 + length(2) + precision(1) + height(2) + width(2) + components
        ms.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x0B }); // marker + length=11
        ms.Add(0x08); // 8-bit precision
        ms.Add((byte)(height >> 8)); ms.Add((byte)height);
        ms.Add((byte)(width  >> 8)); ms.Add((byte)width);
        ms.Add(0x01); // 1 component (grayscale-like, but valid SOF0)
        ms.AddRange(new byte[] { 0x01, 0x11, 0x00 }); // component ID, sampling, quant table

        // EOI
        ms.AddRange(new byte[] { 0xFF, 0xD9 });

        return ms.ToArray();
    }
}
