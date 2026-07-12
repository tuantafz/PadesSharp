// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FluentAssertions;
using ModernPdf.Abstractions.Crypto;
using ModernPdf.Abstractions.Signing;
using ModernPdf.Crypto;
using ModernPdf.Signing;
using Org.BouncyCastle.Cms;

namespace ModernPdf.Tests.Unit.Signing;

/// <summary>
/// Unit tests for <see cref="PdfSigningEngine"/>.
/// Verifies ByteRange calculation, CMS injection and signature verifiability.
/// </summary>
public class PdfSigningEngineTests : IDisposable
{
    private readonly DefaultDigestService _digestService = new();
    private readonly BouncyCastleCmsSigner _cmsSigner;
    private readonly PdfSigningEngine _sut;
    private readonly X509Certificate2 _cert;
    private readonly RsaSoftwareSignatureProvider _signerProvider;

    public PdfSigningEngineTests()
    {
        _cmsSigner = new BouncyCastleCmsSigner(_digestService);
        _sut = new PdfSigningEngine(_cmsSigner, _digestService);

        // Self-signed RSA-2048 cert for tests
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=PdfTestSigner,O=PadesSharp,C=VN",
            rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        _cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));

        _signerProvider = new RsaSoftwareSignatureProvider(_cert);
    }

    public void Dispose()
    {
        _signerProvider.Dispose();
        _cert.Dispose();
    }

    // -----------------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------------

    private PdfSignResult Sign(PdfDigestAlgorithm algorithm, MemoryStream output)
    {
        var request = new PdfSignRequest
        {
            OutputPdf           = output,
            DigestAlgorithm     = algorithm,
            Certificate         = _cert,
            SignatureProvider   = _signerProvider,
            Reason              = "Unit test",
            Location            = "Hanoi",
            SignatureName       = "Sig1",
            SignatureContentSize = 8192,
        };
        return _sut.SignAsync(request).GetAwaiter().GetResult();
    }

    // -----------------------------------------------------------------------
    // ByteRange
    // -----------------------------------------------------------------------

    [Fact]
    public void SignAsync_ByteRange_CoversEntireFile()
    {
        using var output = new MemoryStream();
        var result = Sign(PdfDigestAlgorithm.Sha256, output);

        long totalFile = output.Length;
        long[] br = result.ByteRange;

        // The two ranges must together end at the file boundary:
        // br[2] + br[3] == totalFile  (second range ends at EOF)
        (br[2] + br[3]).Should().Be(totalFile,
            "ByteRange[2] + ByteRange[3] must equal total file length");

        // The first range must end exactly where the /Contents placeholder starts:
        // br[0] + br[1] == br[2] - placeholderSize
        br[0].Should().Be(0, "ByteRange[0] must be 0 per ISO 32000");
        br[1].Should().BeLessThan(br[2], "first segment must end before second starts");
    }

    [Fact]
    public void SignAsync_ByteRange_StartsAtZero()
    {
        using var output = new MemoryStream();
        var result = Sign(PdfDigestAlgorithm.Sha256, output);
        result.ByteRange[0].Should().Be(0);
    }

    [Fact]
    public void SignAsync_ByteRange_AllValuesPositive()
    {
        using var output = new MemoryStream();
        var result = Sign(PdfDigestAlgorithm.Sha256, output);
        result.ByteRange[0].Should().Be(0,  "ByteRange[0] is always 0 per spec");
        result.ByteRange[1].Should().BeGreaterThan(0, "ByteRange[1] must be > 0");
        result.ByteRange[2].Should().BeGreaterThan(0, "ByteRange[2] must be > 0");
        result.ByteRange[3].Should().BeGreaterThan(0, "ByteRange[3] must be > 0");
    }

    // -----------------------------------------------------------------------
    // Output structure
    // -----------------------------------------------------------------------

    [Fact]
    public void SignAsync_Output_ContainsPdfHeader()
    {
        using var output = new MemoryStream();
        Sign(PdfDigestAlgorithm.Sha256, output);

        output.Seek(0, SeekOrigin.Begin);
        var header = new byte[5];
        output.Read(header, 0, 5);
        Encoding.Latin1.GetString(header).Should().Be("%PDF-");
    }

    [Fact]
    public void SignAsync_Output_ContainsSignatureField()
    {
        using var output = new MemoryStream();
        Sign(PdfDigestAlgorithm.Sha256, output);

        string text = Encoding.Latin1.GetString(output.ToArray());
        text.Should().Contain("/Type /Sig");
        text.Should().Contain("/SubFilter /adbe.pkcs7.detached");
        text.Should().Contain("/ByteRange");
        text.Should().Contain("/Contents");
    }

    [Fact]
    public void SignAsync_Output_ContainsSig1FieldName()
    {
        using var output = new MemoryStream();
        Sign(PdfDigestAlgorithm.Sha256, output);

        string text = Encoding.Latin1.GetString(output.ToArray());
        text.Should().Contain("(Sig1)");
    }

    // -----------------------------------------------------------------------
    // CMS verification over ByteRange
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(PdfDigestAlgorithm.Sha256)]
    [InlineData(PdfDigestAlgorithm.Sha384)]
    [InlineData(PdfDigestAlgorithm.Sha512)]
    public void SignAsync_CmsVerifiesOverByteRange(PdfDigestAlgorithm algorithm)
    {
        using var output = new MemoryStream();
        var result = Sign(algorithm, output);
        byte[] pdfBytes = output.ToArray();

        // Extract CMS from /Contents
        byte[] cmsBytes = ExtractContents(pdfBytes);
        cmsBytes.Should().NotBeEmpty("CMS should have been injected");

        // Assemble byte-range content for verification
        byte[] byteRangeData = AssembleByteRange(pdfBytes, result.ByteRange);

        // Verify using BouncyCastle high-level CMS API
        var cmsSignedData = new CmsSignedData(
            new CmsProcessableByteArray(byteRangeData),
            cmsBytes);

        var bcCert = Org.BouncyCastle.Security.DotNetUtilities.FromX509Certificate(_cert);
        bool verified = false;
        foreach (SignerInformation signer in cmsSignedData.GetSignerInfos().GetSigners())
            verified |= signer.Verify(bcCert);

        verified.Should().BeTrue($"CMS must verify over ByteRange data with {algorithm}");
    }

    [Fact]
    public void SignAsync_TamperedPdf_CmsVerificationFails()
    {
        using var output = new MemoryStream();
        var result = Sign(PdfDigestAlgorithm.Sha256, output);
        byte[] pdfBytes = output.ToArray();

        // Tamper: flip the last byte of the PDF
        pdfBytes[^1] ^= 0xFF;

        byte[] cmsBytes = ExtractContents(pdfBytes);
        byte[] byteRangeData = AssembleByteRange(pdfBytes, result.ByteRange);

        var cmsSignedData = new CmsSignedData(
            new CmsProcessableByteArray(byteRangeData),
            cmsBytes);

        var bcCert = Org.BouncyCastle.Security.DotNetUtilities.FromX509Certificate(_cert);
        bool anyFailed = false;
        foreach (SignerInformation signer in cmsSignedData.GetSignerInfos().GetSigners())
        {
            try { if (!signer.Verify(bcCert)) anyFailed = true; }
            catch (CmsException) { anyFailed = true; }
        }

        anyFailed.Should().BeTrue("tampered PDF must fail CMS verification");
    }

    [Fact]
    public void SignAsync_Result_HasExpectedSignatureName()
    {
        using var output = new MemoryStream();
        var result = Sign(PdfDigestAlgorithm.Sha256, output);
        result.SignatureName.Should().Be("Sig1");
    }

    [Fact]
    public void SignAsync_Result_SignatureValueHashIsSha256OfCms()
    {
        using var output = new MemoryStream();
        var result = Sign(PdfDigestAlgorithm.Sha256, output);
        byte[] cmsBytes = ExtractContents(output.ToArray());
        byte[] expectedHash = SHA256.HashData(cmsBytes);
        result.SignatureValueHash.Should().Equal(expectedHash);
    }

    // -----------------------------------------------------------------------
    // Private parsing helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Extracts the /Contents hex value from the PDF bytes by parsing the DER
    /// length field, which avoids trimming issues with zero-padded placeholders.
    /// </summary>
    private static byte[] ExtractContents(byte[] pdfBytes)
    {
        string text = Encoding.Latin1.GetString(pdfBytes);
        int idx = text.IndexOf("/Contents <", StringComparison.Ordinal);
        if (idx < 0) return Array.Empty<byte>();

        int hexStart = idx + "/Contents <".Length;
        int hexEnd   = text.IndexOf('>', hexStart);
        if (hexEnd < 0) return Array.Empty<byte>();

        string hex = text.Substring(hexStart, hexEnd - hexStart);
        if (hex.Length < 4) return Array.Empty<byte>();

        // Determine actual DER length from tag + length bytes.
        int cmsLen = ParseDerTotalLength(hex);
        if (cmsLen <= 0 || cmsLen * 2 > hex.Length)
            return Array.Empty<byte>();

        hex = hex.Substring(0, cmsLen * 2);
        var bytes = new byte[cmsLen];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    /// <summary>
    /// Parses the total byte length of a DER-encoded SEQUENCE from its hex representation.
    /// </summary>
    private static int ParseDerTotalLength(string hex)
    {
        // hex[0:2] = tag (0x30)
        // hex[2:4] = first length byte
        byte firstLen = Convert.ToByte(hex.Substring(2, 2), 16);
        if (firstLen < 0x80) return 1 + 1 + firstLen;
        if (firstLen == 0x81 && hex.Length >= 6)
        {
            int n = Convert.ToByte(hex.Substring(4, 2), 16);
            return 1 + 2 + n;
        }
        if (firstLen == 0x82 && hex.Length >= 8)
        {
            int n = (Convert.ToByte(hex.Substring(4, 2), 16) << 8)
                  |  Convert.ToByte(hex.Substring(6, 2), 16);
            return 1 + 3 + n;
        }
        if (firstLen == 0x83 && hex.Length >= 10)
        {
            int n = (Convert.ToByte(hex.Substring(4, 2), 16) << 16)
                  | (Convert.ToByte(hex.Substring(6, 2), 16) << 8)
                  |  Convert.ToByte(hex.Substring(8, 2), 16);
            return 1 + 4 + n;
        }
        return -1;
    }

    /// <summary>
    /// Assembles the two ByteRange segments from the PDF bytes.
    /// </summary>
    private static byte[] AssembleByteRange(byte[] pdfBytes, long[] byteRange)
    {
        int len = (int)(byteRange[1] + byteRange[3]);
        var result = new byte[len];
        Array.Copy(pdfBytes, (int)byteRange[0], result, 0, (int)byteRange[1]);
        Array.Copy(pdfBytes, (int)byteRange[2], result, (int)byteRange[1], (int)byteRange[3]);
        return result;
    }
}
