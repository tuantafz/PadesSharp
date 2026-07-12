// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using ModernPdf.Abstractions.Crypto;
using ModernPdf.Abstractions.Dss;
using ModernPdf.Abstractions.Revocation;
using ModernPdf.Abstractions.Signing;
using ModernPdf.Crypto;
using ModernPdf.Pades;
using ModernPdf.Signing;

namespace ModernPdf.Tests.Unit.Dss;

// ===========================================================================
// Shared helpers
// ===========================================================================

/// <summary>
/// Minimal in-process PKI: Root CA → End-Entity certificate chain.
/// </summary>
internal sealed class DssFakePki : IDisposable
{
    public RSA             CaRsa  { get; }
    public RSA             EeRsa  { get; }
    public X509Certificate2 CaCert { get; }
    public X509Certificate2 EeCert { get; }

    public DssFakePki()
    {
        CaRsa = RSA.Create(2048);
        EeRsa = RSA.Create(2048);

        var caReq = new CertificateRequest("CN=DssTestCA", CaRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caReq.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        CaCert = caReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddYears(5));

        var eeReq = new CertificateRequest("CN=DssTestEE", EeRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        EeCert = eeReq.Create(CaCert,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1),
            new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04 });
    }

    public void Dispose()
    {
        CaRsa.Dispose();
        EeRsa.Dispose();
        CaCert.Dispose();
        EeCert.Dispose();
    }
}

/// <summary>
/// Builds a signed PDF using <see cref="PdfSigningEngine"/> for use in DSS tests.
/// </summary>
internal static class SignedPdfFactory
{
    public static (byte[] PdfBytes, PdfSignResult Result, X509Certificate2 Cert)
        CreateSigned(string reason = "DSS test")
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=DssSignerTest", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));

        // The cert must have a private key attached for signing; CreateSelfSigned does this.
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));

        var digestService = new DefaultDigestService();
        var cmsSigner     = new BouncyCastleCmsSigner(digestService);
        var engine        = new PdfSigningEngine(cmsSigner, digestService);
        using var signerProvider = new RsaSoftwareSignatureProvider(cert);

        using var output = new MemoryStream();
        var signRequest = new PdfSignRequest
        {
            OutputPdf            = output,
            DigestAlgorithm      = PdfDigestAlgorithm.Sha256,
            Certificate          = cert,
            SignatureProvider    = signerProvider,
            Reason               = reason,
            Location             = "Hanoi",
            SignatureName        = "Sig1",
            SignatureContentSize = 8192,
        };

        var result = engine.SignAsync(signRequest).GetAwaiter().GetResult();
        return (output.ToArray(), result, cert);
    }
}

// ===========================================================================
// LtvDataCollector Tests
// ===========================================================================

public class LtvDataCollectorTests : IDisposable
{
    private readonly DssFakePki _pki = new();
    private static readonly byte[] FakeCmsBytes = new byte[] { 0x30, 0x82, 0x01, 0x00, 0xFF, 0xEE };
    private static readonly byte[] FakeOcspBytes = new byte[] { 0x30, 0x10, 0x0A, 0x01, 0x00 };
    private static readonly byte[] FakeCrlBytes  = new byte[] { 0x30, 0x20, 0x0A, 0x01, 0x00 };

    public void Dispose() => _pki.Dispose();

    // ---- Guards --------------------------------------------------------

    [Fact]
    public async Task CollectAsync_NullCertChain_ThrowsArgumentNullException()
    {
        var sut = new LtvDataCollector();
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.CollectAsync(null!, FakeCmsBytes));
    }

    [Fact]
    public async Task CollectAsync_NullCmsBytes_ThrowsArgumentNullException()
    {
        var sut = new LtvDataCollector();
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.CollectAsync(new[] { _pki.EeCert }, null!));
    }

    // ---- Certificate pool -------------------------------------------

    [Fact]
    public async Task CollectAsync_SingleCert_IsAddedToCertPool()
    {
        var sut = new LtvDataCollector();
        var dss = await sut.CollectAsync(new[] { _pki.EeCert }, FakeCmsBytes);
        dss.Certificates.Should().HaveCount(1);
        dss.Certificates[0].Should().Equal(_pki.EeCert.RawData);
    }

    [Fact]
    public async Task CollectAsync_TwoCertChain_BothCertsAddedToPool()
    {
        var sut = new LtvDataCollector();
        var chain = new[] { _pki.EeCert, _pki.CaCert };
        var dss = await sut.CollectAsync(chain, FakeCmsBytes);
        dss.Certificates.Should().HaveCount(2);
    }

    // ---- OCSP collection -------------------------------------------

    [Fact]
    public async Task CollectAsync_WithOcsp_OcspBytesAddedToPool()
    {
        var ocspMock = new Mock<IOcspClient>();
        ocspMock
            .Setup(c => c.CheckRevocationAsync(
                It.IsAny<X509Certificate2>(),
                It.IsAny<X509Certificate2>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OcspResult
            {
                Status            = RevocationStatus.Good,
                OcspResponseBytes = FakeOcspBytes,
            });

        var sut = new LtvDataCollector(ocspMock.Object);
        var chain = new[] { _pki.EeCert, _pki.CaCert };
        var dss = await sut.CollectAsync(chain, FakeCmsBytes);

        dss.OcspResponses.Should().HaveCount(1);
        dss.OcspResponses[0].Should().Equal(FakeOcspBytes);
    }

    // ---- CRL fallback -----------------------------------------------

    [Fact]
    public async Task CollectAsync_OcspUnavailable_CrlUsedAsFallback()
    {
        var ocspMock = new Mock<IOcspClient>();
        ocspMock
            .Setup(c => c.CheckRevocationAsync(
                It.IsAny<X509Certificate2>(),
                It.IsAny<X509Certificate2>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OcspResult { Status = RevocationStatus.Unavailable });

        var crlMock = new Mock<ICrlClient>();
        crlMock
            .Setup(c => c.CheckRevocationAsync(
                It.IsAny<X509Certificate2>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CrlResult
            {
                Status   = RevocationStatus.Good,
                CrlBytes = FakeCrlBytes,
            });

        var sut = new LtvDataCollector(ocspMock.Object, crlMock.Object);
        var chain = new[] { _pki.EeCert, _pki.CaCert };
        var dss = await sut.CollectAsync(chain, FakeCmsBytes);

        dss.OcspResponses.Should().BeEmpty();
        dss.Crls.Should().HaveCount(1);
        dss.Crls[0].Should().Equal(FakeCrlBytes);
    }

    // ---- OCSP preferred over CRL -----------------------------------

    [Fact]
    public async Task CollectAsync_OcspAvailable_CrlNotQueried()
    {
        var ocspMock = new Mock<IOcspClient>();
        ocspMock
            .Setup(c => c.CheckRevocationAsync(
                It.IsAny<X509Certificate2>(),
                It.IsAny<X509Certificate2>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OcspResult
            {
                Status            = RevocationStatus.Good,
                OcspResponseBytes = FakeOcspBytes,
            });

        var crlMock = new Mock<ICrlClient>();

        var sut = new LtvDataCollector(ocspMock.Object, crlMock.Object);
        var chain = new[] { _pki.EeCert, _pki.CaCert };
        var dss = await sut.CollectAsync(chain, FakeCmsBytes);

        dss.OcspResponses.Should().HaveCount(1);
        dss.Crls.Should().BeEmpty();
        // CRL client was never called.
        crlMock.Verify(c => c.CheckRevocationAsync(
            It.IsAny<X509Certificate2>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- VRI key ----------------------------------------------------

    [Fact]
    public async Task CollectAsync_VriKeyIsSha1HexOfCmsBytes()
    {
        var sut = new LtvDataCollector();
        var dss = await sut.CollectAsync(new[] { _pki.EeCert }, FakeCmsBytes);

#pragma warning disable CA5350
        using var sha1 = SHA1.Create();
#pragma warning restore CA5350
        byte[] hash = sha1.ComputeHash(FakeCmsBytes);
        string expectedKey = BitConverter.ToString(hash).Replace("-", string.Empty).ToUpperInvariant();

        dss.Vri.Should().ContainKey(expectedKey);
    }

    [Fact]
    public async Task CollectAsync_VriCertIndicesCoverAllCerts()
    {
        var sut = new LtvDataCollector();
        var chain = new[] { _pki.EeCert, _pki.CaCert };
        var dss = await sut.CollectAsync(chain, FakeCmsBytes);

        dss.Vri.Should().HaveCount(1);
        var vri = dss.Vri.Values.Should().ContainSingle().Subject;
        vri.CertificateIndices.Should().HaveCount(2);
        vri.CertificateIndices.Should().Contain(0).And.Contain(1);
    }

    [Fact]
    public async Task CollectAsync_VriOcspIndexPointsToGlobalPool()
    {
        var ocspMock = new Mock<IOcspClient>();
        ocspMock
            .Setup(c => c.CheckRevocationAsync(
                It.IsAny<X509Certificate2>(),
                It.IsAny<X509Certificate2>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OcspResult
            {
                Status            = RevocationStatus.Good,
                OcspResponseBytes = FakeOcspBytes,
            });

        var sut = new LtvDataCollector(ocspMock.Object);
        var chain = new[] { _pki.EeCert, _pki.CaCert };
        var dss = await sut.CollectAsync(chain, FakeCmsBytes);

        var vri = dss.Vri.Values.Should().ContainSingle().Subject;
        vri.OcspIndices.Should().ContainSingle().Which.Should().Be(0);
    }
}

// ===========================================================================
// DssIncrementalWriter Tests
// ===========================================================================

public class DssIncrementalWriterTests
{
    private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

    private static readonly byte[] FakeCertBytes = new byte[] { 0x30, 0x82, 0x01, 0xAA, 0x01, 0x02, 0x03 };
    private static readonly byte[] FakeOcspBytes = new byte[] { 0x30, 0x10, 0xAB, 0xCD, 0xEF };
    private static readonly byte[] FakeCrlBytes  = new byte[] { 0x30, 0x20, 0xDE, 0xAD, 0xBE };

    private readonly DssIncrementalWriter _sut = new DssIncrementalWriter();

    // ---- Guards --------------------------------------------------------

    [Fact]
    public void AppendDss_NullPdfBytes_Throws()
    {
        Action act = () => _sut.AppendDss(null!, new DssData());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AppendDss_NullDssData_Throws()
    {
        var (pdfBytes, _, _) = SignedPdfFactory.CreateSigned();
        Action act = () => _sut.AppendDss(pdfBytes, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ---- Structural checks -----------------------------------------

    [Fact]
    public void AppendDss_ExistingBytesUnmodified()
    {
        var (pdfBytes, _, _) = SignedPdfFactory.CreateSigned();
        var dssData = new DssData();

        byte[] result = _sut.AppendDss(pdfBytes, dssData);

        result.Length.Should().BeGreaterThan(pdfBytes.Length, "incremental update must have been appended");
        result.Should().StartWith(pdfBytes, "original signed bytes must be intact at the start");
    }

    [Fact]
    public void AppendDss_ContainsDssDictionary()
    {
        var (pdfBytes, _, _) = SignedPdfFactory.CreateSigned();
        var dssData = new DssData();

        byte[] result = _sut.AppendDss(pdfBytes, dssData);
        string text = Latin1.GetString(result);

        text.Should().Contain("/Type /DSS");
    }

    [Fact]
    public void AppendDss_CatalogUpdatedWithDssEntry()
    {
        var (pdfBytes, _, _) = SignedPdfFactory.CreateSigned();
        var dssData = new DssData();

        byte[] result = _sut.AppendDss(pdfBytes, dssData);
        string text = Latin1.GetString(result);

        // The updated catalog in the incremental section must reference /DSS
        text.Should().Contain("/DSS ");
    }

    [Fact]
    public void AppendDss_IncrementalTrailerHasPrevEntry()
    {
        var (pdfBytes, _, _) = SignedPdfFactory.CreateSigned();
        var dssData = new DssData();

        byte[] result = _sut.AppendDss(pdfBytes, dssData);
        string text = Latin1.GetString(result);

        text.Should().Contain("/Prev ");
    }

    // ---- Content embedding ------------------------------------------

    [Fact]
    public void AppendDss_CertStreamEmbedded()
    {
        var (pdfBytes, _, _) = SignedPdfFactory.CreateSigned();
        var dssData = new DssData();
        dssData.Certificates.Add(FakeCertBytes);

        byte[] result = _sut.AppendDss(pdfBytes, dssData);
        string text = Latin1.GetString(result);

        text.Should().Contain($"/Length {FakeCertBytes.Length}",
            "a stream object with the cert must have been written");
        text.Should().Contain("/Certs [");
    }

    [Fact]
    public void AppendDss_OcspStreamEmbedded()
    {
        var (pdfBytes, _, _) = SignedPdfFactory.CreateSigned();
        var dssData = new DssData();
        dssData.OcspResponses.Add(FakeOcspBytes);

        byte[] result = _sut.AppendDss(pdfBytes, dssData);
        string text = Latin1.GetString(result);

        text.Should().Contain($"/Length {FakeOcspBytes.Length}");
        text.Should().Contain("/OCSPs [");
    }

    [Fact]
    public void AppendDss_CrlStreamEmbedded()
    {
        var (pdfBytes, _, _) = SignedPdfFactory.CreateSigned();
        var dssData = new DssData();
        dssData.Crls.Add(FakeCrlBytes);

        byte[] result = _sut.AppendDss(pdfBytes, dssData);
        string text = Latin1.GetString(result);

        text.Should().Contain($"/Length {FakeCrlBytes.Length}");
        text.Should().Contain("/CRLs [");
    }

    // ---- VRI key ----------------------------------------------------

    [Fact]
    public void AppendDss_VriKeyPresentInDss()
    {
        var (pdfBytes, result, _) = SignedPdfFactory.CreateSigned();

#pragma warning disable CA5350
        using var sha1 = SHA1.Create();
#pragma warning restore CA5350
        byte[] hash = sha1.ComputeHash(result.SignatureCmsBytes);
        string expectedKey = BitConverter.ToString(hash).Replace("-", string.Empty).ToUpperInvariant();

        var dssData = new DssData();
        var vri = new VriData();
        dssData.Vri[expectedKey] = vri;

        byte[] output = _sut.AppendDss(pdfBytes, dssData);
        string text = Latin1.GetString(output);

        text.Should().Contain($"/{expectedKey}");
    }

    // ---- Multiple items --------------------------------------------

    [Fact]
    public void AppendDss_MultipleCertsAndOcsps_AllStreamsPresent()
    {
        var (pdfBytes, _, _) = SignedPdfFactory.CreateSigned();
        var cert1 = new byte[] { 0x30, 0x05, 0x01 };
        var cert2 = new byte[] { 0x30, 0x06, 0x02 };
        var ocsp1 = new byte[] { 0x30, 0x07, 0x03 };

        var dssData = new DssData();
        dssData.Certificates.Add(cert1);
        dssData.Certificates.Add(cert2);
        dssData.OcspResponses.Add(ocsp1);

        byte[] result = _sut.AppendDss(pdfBytes, dssData);
        string text = Latin1.GetString(result);

        text.Should().Contain($"/Length {cert1.Length}");
        text.Should().Contain($"/Length {cert2.Length}");
        text.Should().Contain($"/Length {ocsp1.Length}");
    }

    // ---- Idempotence guard (no double-%%EOF confusion) -----------

    [Fact]
    public void AppendDss_EmptyDss_OutputLargerThanInput()
    {
        var (pdfBytes, _, _) = SignedPdfFactory.CreateSigned();
        var dssData = new DssData();

        byte[] result = _sut.AppendDss(pdfBytes, dssData);

        result.Length.Should().BeGreaterThan(pdfBytes.Length);
    }
}
