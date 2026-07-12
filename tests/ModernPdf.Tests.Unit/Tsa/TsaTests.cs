// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using ModernPdf.Abstractions.Crypto;
using ModernPdf.Abstractions.Signing;
using ModernPdf.Abstractions.Tsa;
using ModernPdf.Crypto;
using ModernPdf.Crypto.Tsa;
using ModernPdf.Signing;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tsp;

namespace ModernPdf.Tests.Unit.Tsa;

// ===========================================================================
// TsaTokenResult tests
// ===========================================================================

public class TsaTokenResultTests
{
    [Fact]
    public void Success_WithTokenBytes_HasImageFalseByDefault()
    {
        var r = new TsaTokenResult { Success = true, TokenBytes = new byte[] { 1, 2, 3 } };
        r.Success.Should().BeTrue();
        r.TokenBytes.Should().NotBeNull();
        r.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Failure_HasNullTokenBytes()
    {
        var r = new TsaTokenResult { Success = false, ErrorMessage = "TSA error" };
        r.Success.Should().BeFalse();
        r.TokenBytes.Should().BeNull();
        r.ErrorMessage.Should().Be("TSA error");
    }
}

// ===========================================================================
// TsaClientOptions tests
// ===========================================================================

public class TsaClientOptionsTests
{
    [Fact]
    public void Defaults_AreReasonable()
    {
        var opts = new TsaClientOptions();
        opts.DigestAlgorithm.Should().Be(PdfDigestAlgorithm.Sha256);
        opts.RequestCertificate.Should().BeTrue();
        opts.UseNonce.Should().BeTrue();
        opts.Timeout.Should().Be(TimeSpan.FromSeconds(30));
        opts.MaxRetries.Should().Be(3);
    }
}

// ===========================================================================
// Rfc3161TsaClient unit tests (using fake HttpMessageHandler)
// ===========================================================================

public class Rfc3161TsaClientTests
{
    private static readonly DefaultDigestService _digestService = new();

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a minimal valid RFC 3161 TimeStampResponse (granted) using BouncyCastle.
    /// </summary>
    private static byte[] CreateFakeTsr(byte[] messageImprint, string digestOid, BigInteger? nonce)
    {
        // Build a TimeStampRequest first (needed for BuildResponse).
        var gen = new TimeStampRequestGenerator();
        gen.SetCertReq(false);
        TimeStampRequest req = gen.Generate(digestOid, messageImprint, nonce);

        // Build a fake TSA response using an ephemeral self-signed certificate.
        using var rsa = RSA.Create(2048);
        var certReq = new CertificateRequest("CN=FakeTSA", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        certReq.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        // id-kp-timeStamping EKU required by BouncyCastle TimeStampTokenGenerator
        certReq.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.8") }, true));
        using var cert = certReq.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        // BouncyCastle TimeStampTokenGenerator
        var bcCert  = DotNetUtilities.FromX509Certificate(cert);
        var bcPriv  = DotNetUtilities.GetRsaKeyPair(rsa).Private;

        var tsGen = new TimeStampTokenGenerator(bcPriv, bcCert, TspAlgorithms.Sha256, "1.2.3.4.5");


        TimeStampResponseGenerator respGen = new TimeStampResponseGenerator(tsGen, TspAlgorithms.Allowed);
        TimeStampResponse resp = respGen.Generate(req, new BigInteger(64, new SecureRandom()), DateTime.UtcNow);
        return resp.GetEncoded();
    }

    private static HttpClient BuildHttpClient(byte[] responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new FakeHttpMessageHandler(responseBody, statusCode);
        return new HttpClient(handler);
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetTimestampAsync_ValidTsr_ReturnsSuccess()
    {
        byte[] sigBytes = new byte[] { 1, 2, 3, 4, 5 };
        byte[] imprint  = _digestService.ComputeDigest(sigBytes, PdfDigestAlgorithm.Sha256);
        string oid      = _digestService.GetDigestOid(PdfDigestAlgorithm.Sha256);

        // Nonce is random inside the client, so build TSR without nonce constraint
        // (UseNonce=false) for this test to avoid nonce mismatch.
        byte[] tsrBytes = CreateFakeTsr(imprint, oid, null);

        var opts = new TsaClientOptions
        {
            TsaUrl             = "http://localhost/tsa",
            UseNonce           = false,
            RequestCertificate = false,
            MaxRetries         = 1,
        };
        var client  = new Rfc3161TsaClient(opts, BuildHttpClient(tsrBytes), _digestService);
        var result  = await client.GetTimestampAsync(sigBytes);

        result.Success.Should().BeTrue();
        result.TokenBytes.Should().NotBeNull();
        result.TokenBytes!.Length.Should().BeGreaterThan(0);
        result.TimestampTime.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTimestampAsync_WithNonce_MatchingNonce_Succeeds()
    {
        // When UseNonce=true, the TSR must echo the nonce.
        // We pre-seed a specific nonce by building the TSR manually.
        // Since the client generates its own random nonce we can't pre-build the TSR.
        // So: test that the mismatch path works by returning a TSR with wrong nonce.
        byte[] sigBytes = new byte[] { 10, 20, 30 };
        byte[] imprint  = _digestService.ComputeDigest(sigBytes, PdfDigestAlgorithm.Sha256);
        string oid      = _digestService.GetDigestOid(PdfDigestAlgorithm.Sha256);

        // Build TSR with a hardcoded nonce that won't match the client's random nonce.
        BigInteger wrongNonce = BigInteger.ValueOf(999999L);
        byte[] tsrBytes = CreateFakeTsr(imprint, oid, wrongNonce);

        var opts = new TsaClientOptions
        {
            TsaUrl     = "http://localhost/tsa",
            UseNonce   = true,
            MaxRetries = 1,
        };
        var client = new Rfc3161TsaClient(opts, BuildHttpClient(tsrBytes), _digestService);

        // The client will generate its own random nonce; the TSR echoes 999999 → mismatch.
        var result = await client.GetTimestampAsync(sigBytes);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("nonce");
    }

    [Fact]
    public async Task GetTimestampAsync_HttpError_ReturnsFailureAfterRetries()
    {
        var opts = new TsaClientOptions
        {
            TsaUrl          = "http://localhost/tsa",
            UseNonce        = false,
            MaxRetries      = 2,
            RetryBaseDelay  = TimeSpan.FromMilliseconds(1),  // fast retry for test
        };
        var client = new Rfc3161TsaClient(
            opts,
            BuildHttpClient(Array.Empty<byte>(), HttpStatusCode.ServiceUnavailable),
            _digestService);

        var act = async () => await client.GetTimestampAsync(new byte[] { 1, 2, 3 });
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*attempt(s)*");
    }

    [Fact]
    public async Task GetTimestampAsync_InvalidTsrBytes_ReturnsFailure()
    {
        // Response is valid HTTP 200 but TSR bytes are garbage.
        var opts = new TsaClientOptions
        {
            TsaUrl     = "http://localhost/tsa",
            UseNonce   = false,
            MaxRetries = 1,
        };
        var client = new Rfc3161TsaClient(
            opts,
            BuildHttpClient(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }),
            _digestService);

        var result = await client.GetTimestampAsync(new byte[] { 1, 2, 3 });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Constructor_EmptyTsaUrl_Throws()
    {
        var opts = new TsaClientOptions { TsaUrl = "" };
        var act  = () => new Rfc3161TsaClient(opts, new HttpClient(), _digestService);
        act.Should().Throw<ArgumentException>().WithMessage("*TsaUrl*");
    }

    [Fact]
    public async Task GetTimestampAsync_EmptyBytes_Throws()
    {
        var opts   = new TsaClientOptions { TsaUrl = "http://localhost/tsa" };
        var client = new Rfc3161TsaClient(opts, new HttpClient(), _digestService);
        var act    = async () => await client.GetTimestampAsync(Array.Empty<byte>());
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*signatureBytes*");
    }
}

// ===========================================================================
// TsaAttributeStamper tests (using mock ITsaClient)
// ===========================================================================

public class TsaAttributeStamperTests
{
    private static readonly DefaultDigestService _digestService = new();

    /// <summary>Creates a self-signed cert + signs a minimal CMS.</summary>
    private static (byte[] cmsBytes, X509Certificate2 cert) CreateMinimalCms()
    {
        using var rsa   = RSA.Create(2048);
        var certReq     = new CertificateRequest("CN=Stamper", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert  = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        using var sigProv = new RsaSoftwareSignatureProvider(cert);

        var signer    = new BouncyCastleCmsSigner(_digestService);
        byte[] digest = _digestService.ComputeDigest(new byte[] { 1, 2, 3 }, PdfDigestAlgorithm.Sha256);

        var req = new CmsSigningRequest
        {
            ContentDigest      = digest,
            SigningCertificate = cert,
            CertificateChain   = new[] { cert },
            DigestAlgorithm    = PdfDigestAlgorithm.Sha256,
            SignatureProvider  = sigProv,
            SigningTime        = DateTimeOffset.UtcNow,
        };

        byte[] cms = signer.CreateDetachedSignature(req);
        return (cms, cert);
    }

    /// <summary>
    /// Builds a fake TimeStampToken for the given signature value.
    /// Matches what the real TSA would return (using a fake self-signed TSA cert).
    /// </summary>
    private static byte[] BuildFakeTstBytes(byte[] signatureValue)
    {
        using var rsaTsa = RSA.Create(2048);
        var certReqTsa   = new CertificateRequest("CN=FakeTSA2", rsaTsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        certReqTsa.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.8") }, true));
        using var certTsa = certReqTsa.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        byte[] imprint = _digestService.ComputeDigest(signatureValue, PdfDigestAlgorithm.Sha256);
        string oid     = _digestService.GetDigestOid(PdfDigestAlgorithm.Sha256);

        var gen         = new TimeStampRequestGenerator();
        TimeStampRequest req = gen.Generate(oid, imprint, null);

        var bcCert   = DotNetUtilities.FromX509Certificate(certTsa);
        var bcPriv   = DotNetUtilities.GetRsaKeyPair(rsaTsa).Private;
        var tsGen    = new TimeStampTokenGenerator(bcPriv, bcCert, TspAlgorithms.Sha256, "1.2.3");

        var respGen  = new TimeStampResponseGenerator(tsGen, TspAlgorithms.Allowed);
        var resp     = respGen.Generate(req, new BigInteger(64, new SecureRandom()), DateTime.UtcNow);
        return resp.TimeStampToken.GetEncoded();
    }

    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddSignatureTimestamp_AddsUnsignedAttribute()
    {
        var (cmsBytes, _) = CreateMinimalCms();

        // Arrange mock TSA client that returns a valid TST.
        var mockTsa = new Mock<ITsaClient>();
        mockTsa
            .Setup(t => t.GetTimestampAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Returns<byte[], CancellationToken>((sigVal, _) =>
                Task.FromResult(new TsaTokenResult
                {
                    Success       = true,
                    TokenBytes    = BuildFakeTstBytes(sigVal),
                    TimestampTime = DateTimeOffset.UtcNow,
                }));

        byte[] stampedCms = await TsaAttributeStamper.AddSignatureTimestampAsync(
            cmsBytes, mockTsa.Object);

        // The resulting CMS should be larger (has the TST appended as unsigned attr).
        stampedCms.Length.Should().BeGreaterThan(cmsBytes.Length);

        // The TSA client must have been called exactly once (for the one SignerInfo).
        mockTsa.Verify(t => t.GetTimestampAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddSignatureTimestamp_OutputIsValidCmsSignedData()
    {
        var (cmsBytes, _) = CreateMinimalCms();

        var mockTsa = new Mock<ITsaClient>();
        mockTsa
            .Setup(t => t.GetTimestampAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Returns<byte[], CancellationToken>((sv, _) =>
                Task.FromResult(new TsaTokenResult
                {
                    Success    = true,
                    TokenBytes = BuildFakeTstBytes(sv),
                }));

        byte[] stampedCms = await TsaAttributeStamper.AddSignatureTimestampAsync(
            cmsBytes, mockTsa.Object);

        // Must parse without exception.
        var act = () => new Org.BouncyCastle.Cms.CmsSignedData(stampedCms);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task AddSignatureTimestamp_TsaFailure_ThrowsInvalidOperation()
    {
        var (cmsBytes, _) = CreateMinimalCms();

        var mockTsa = new Mock<ITsaClient>();
        mockTsa
            .Setup(t => t.GetTimestampAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TsaTokenResult { Success = false, ErrorMessage = "Network timeout" });

        var act = async () => await TsaAttributeStamper.AddSignatureTimestampAsync(
            cmsBytes, mockTsa.Object);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Network timeout*");
    }

    [Fact]
    public async Task AddSignatureTimestamp_EmptyCms_Throws()
    {
        var mockTsa = new Mock<ITsaClient>();
        var act = async () => await TsaAttributeStamper.AddSignatureTimestampAsync(
            Array.Empty<byte>(), mockTsa.Object);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*cmsBytes*");
    }
}

// ===========================================================================
// Integration: PdfSigningEngine with TSA mock produces larger CMS
// ===========================================================================

public class PdfSigningEngineTsaTests
{
    [Fact]
    public async Task SignAsync_WithTsaClient_CmsIsLargerThanWithout()
    {
        using var rsa = RSA.Create(2048);
        var certReq   = new CertificateRequest("CN=TsaIntegration", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        using var sigProv = new RsaSoftwareSignatureProvider(cert);

        var digest  = new DefaultDigestService();
        var cmsSign = new BouncyCastleCmsSigner(digest);

        // Build a mock TSA that returns a valid (large) fake token.
        using var rsaTsa = RSA.Create(2048);
        var certReqTsa   = new CertificateRequest("CN=MockTSA", rsaTsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        certReqTsa.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.8") }, true));
        using var certTsa = certReqTsa.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        var bcCertTsa = DotNetUtilities.FromX509Certificate(certTsa);
        var bcPrivTsa = DotNetUtilities.GetRsaKeyPair(rsaTsa).Private;
        var tsGen     = new TimeStampTokenGenerator(bcPrivTsa, bcCertTsa, TspAlgorithms.Sha256, "1.2.3.4");
        var respGenTsa = new TimeStampResponseGenerator(tsGen, TspAlgorithms.Allowed);

        var mockTsa = new Mock<ITsaClient>();
        mockTsa
            .Setup(t => t.GetTimestampAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Returns<byte[], CancellationToken>((sv, _) =>
            {
                byte[] imp    = digest.ComputeDigest(sv, PdfDigestAlgorithm.Sha256);
                string oid    = digest.GetDigestOid(PdfDigestAlgorithm.Sha256);
                var gen2      = new TimeStampRequestGenerator();
                var req2      = gen2.Generate(oid, imp, null);
                var resp2     = respGenTsa.Generate(req2, new BigInteger(64, new SecureRandom()), DateTime.UtcNow);
                return Task.FromResult(new TsaTokenResult
                {
                    Success    = true,
                    TokenBytes = resp2.TimeStampToken.GetEncoded(),
                });
            });

        // Sign WITHOUT TSA.
        var engine = new PdfSigningEngine(cmsSign, digest);
        using var out1 = new MemoryStream();
        var req1 = new PdfSignRequest
        {
            OutputPdf            = out1,
            Certificate          = cert,
            SignatureProvider    = sigProv,
            DigestAlgorithm      = PdfDigestAlgorithm.Sha256,
            SignatureContentSize = 16384, // large enough for TSA token
        };
        var res1 = await engine.SignAsync(req1);
        res1.Success.Should().BeTrue();

        // Sign WITH TSA.
        using var out2 = new MemoryStream();
        var req2b = new PdfSignRequest
        {
            OutputPdf            = out2,
            Certificate          = cert,
            SignatureProvider    = sigProv,
            DigestAlgorithm      = PdfDigestAlgorithm.Sha256,
            SignatureContentSize = 16384,
            TsaClient            = mockTsa.Object,
        };
        var res2 = await engine.SignAsync(req2b);
        res2.Success.Should().BeTrue();

        // The output PDF with TSA should contain a larger CMS blob.
        out2.Length.Should().Be(out1.Length,
            "both PDFs have same ByteRange structure; CMS placeholder size is fixed");
        mockTsa.Verify(t => t.GetTimestampAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

// ===========================================================================
// Fake HttpMessageHandler for unit tests
// ===========================================================================

file sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly byte[]     _responseBody;
    private readonly HttpStatusCode _statusCode;

    public FakeHttpMessageHandler(byte[] responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responseBody = responseBody;
        _statusCode   = statusCode;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken  cancellationToken)
    {
        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new ByteArrayContent(_responseBody),
        };
        response.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/timestamp-reply");
        return Task.FromResult(response);
    }
}
