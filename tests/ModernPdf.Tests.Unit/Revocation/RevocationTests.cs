// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ModernPdf.Abstractions.Revocation;
using ModernPdf.Crypto.Revocation;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Ocsp;
using Org.BouncyCastle.Asn1.Oiw;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Ocsp;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.X509.Extension;

namespace ModernPdf.Tests.Unit.Revocation;

// ===========================================================================
// Shared certificate / test infrastructure
// ===========================================================================

/// <summary>
/// Creates an in-memory PKI hierarchy (Root CA → End-Entity) suitable for
/// building OCSP responses and CRLs inside unit tests.
/// </summary>
internal sealed class FakePki : IDisposable
{
    public RSA         CaRsa      { get; }
    public RSA         EeRsa      { get; }
    public X509Certificate2 CaCert { get; }
    public X509Certificate2 EeCert { get; }

    public Org.BouncyCastle.X509.X509Certificate BcCaCert { get; }
    public Org.BouncyCastle.X509.X509Certificate BcEeCert { get; }
    public Org.BouncyCastle.Crypto.AsymmetricKeyParameter BcCaPrivate { get; }

    public FakePki()
    {
        CaRsa = RSA.Create(2048);
        EeRsa = RSA.Create(2048);

        var caReq = new CertificateRequest("CN=FakeCA", CaRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caReq.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        CaCert = caReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddYears(5));

        var eeReq = new CertificateRequest("CN=FakeEE", EeRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        EeCert = eeReq.Create(
            CaCert,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1),
            new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });  // serial bytes

        BcCaCert   = new X509CertificateParser().ReadCertificate(CaCert.RawData);
        BcEeCert   = new X509CertificateParser().ReadCertificate(EeCert.RawData);
        BcCaPrivate = DotNetUtilities.GetRsaKeyPair(CaRsa).Private;
    }

    /// <summary>Creates a valid (Good) OCSP response DER bytes for the EE cert.</summary>
    public byte[] CreateOcspResponseGood()
    {
        var hashAlg = new AlgorithmIdentifier(OiwObjectIdentifiers.IdSha1);
        var certId  = new CertificateID(hashAlg, BcCaCert, BcEeCert.SerialNumber);
        return BuildBasicOcspResponse(certId, CertificateStatus.Good);
    }

    /// <summary>Creates a Revoked OCSP response DER bytes for the EE cert.</summary>
    public byte[] CreateOcspResponseRevoked(DateTime revokedAt)
    {
        var hashAlg = new AlgorithmIdentifier(OiwObjectIdentifiers.IdSha1);
        var certId  = new CertificateID(hashAlg, BcCaCert, BcEeCert.SerialNumber);
        var rStatus = new RevokedStatus(revokedAt, 1); // reason: keyCompromise
        return BuildBasicOcspResponse(certId, rStatus);
    }

    private byte[] BuildBasicOcspResponse(CertificateID certId, CertificateStatus status)
    {
        var respGen = new BasicOcspRespGenerator(
            new RespID(BcCaCert.SubjectDN));
        respGen.AddResponse(certId, status);

        var basicResp = respGen.Generate(
            "SHA256withRSA", BcCaPrivate, null, DateTime.UtcNow);

        var ocspRespGen = new OCSPRespGenerator();
        var ocspResp    = ocspRespGen.Generate(OcspRespStatus.Successful, basicResp);
        return ocspResp.GetEncoded();
    }

    /// <summary>Creates a CRL with no revoked certificates (empty).</summary>
    public byte[] CreateEmptyCrl()
    {
        var gen = new X509V2CrlGenerator();
        gen.SetIssuerDN(BcCaCert.SubjectDN);
        gen.SetThisUpdate(DateTime.UtcNow.AddMinutes(-5));
        gen.SetNextUpdate(DateTime.UtcNow.AddDays(7));
        var crl = gen.Generate(new Asn1SignatureFactory("SHA256withRSA", BcCaPrivate));
        return crl.GetEncoded();
    }

    /// <summary>Creates a CRL with the EE cert listed as revoked.</summary>
    public byte[] CreateCrlWithRevoked(DateTime revokedAt)
    {
        var gen = new X509V2CrlGenerator();
        gen.SetIssuerDN(BcCaCert.SubjectDN);
        gen.SetThisUpdate(DateTime.UtcNow.AddMinutes(-5));
        gen.SetNextUpdate(DateTime.UtcNow.AddDays(7));
        gen.AddCrlEntry(BcEeCert.SerialNumber, revokedAt, CrlReason.KeyCompromise);
        var crl = gen.Generate(new Asn1SignatureFactory("SHA256withRSA", BcCaPrivate));
        return crl.GetEncoded();
    }

    public void Dispose()
    {
        CaRsa.Dispose();
        EeRsa.Dispose();
        CaCert.Dispose();
        EeCert.Dispose();
    }
}

// ===========================================================================
// RevocationStatus enum tests
// ===========================================================================

public class RevocationStatusTests
{
    [Fact]
    public void Enum_HasExpectedValues()
    {
        Enum.IsDefined(typeof(RevocationStatus), RevocationStatus.Good).Should().BeTrue();
        Enum.IsDefined(typeof(RevocationStatus), RevocationStatus.Revoked).Should().BeTrue();
        Enum.IsDefined(typeof(RevocationStatus), RevocationStatus.Unknown).Should().BeTrue();
        Enum.IsDefined(typeof(RevocationStatus), RevocationStatus.Unavailable).Should().BeTrue();
    }
}

// ===========================================================================
// DefaultOcspClient tests
// ===========================================================================

public class DefaultOcspClientTests : IDisposable
{
    private readonly FakePki _pki = new();

    public void Dispose() => _pki.Dispose();

    private static DefaultOcspClient Build(byte[] responseBody, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHttpHandler(responseBody, status);
        return new DefaultOcspClient(new HttpClient(handler));
    }

    // -----------------------------------------------------------------------
    [Fact]
    public async Task CheckRevocation_GoodStatus_ReturnsGood()
    {
        byte[] ocspResp = _pki.CreateOcspResponseGood();
        var client = Build(ocspResp);

        var result = await client.CheckRevocationAsync(
            _pki.EeCert, _pki.CaCert, "http://ocsp.example.test/");

        result.Status.Should().Be(RevocationStatus.Good);
        result.OcspResponseBytes.Should().NotBeNull();
        result.ResponderUrl.Should().Be("http://ocsp.example.test/");
    }

    [Fact]
    public async Task CheckRevocation_RevokedStatus_ReturnsRevoked()
    {
        var revokedAt = DateTime.UtcNow.AddDays(-1);
        byte[] ocspResp = _pki.CreateOcspResponseRevoked(revokedAt);
        var client = Build(ocspResp);

        var result = await client.CheckRevocationAsync(
            _pki.EeCert, _pki.CaCert, "http://ocsp.example.test/");

        result.Status.Should().Be(RevocationStatus.Revoked);
        result.RevocationTime.Should().NotBeNull();
        result.RevocationReason.Should().Be("keyCompromise");
        result.OcspResponseBytes.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckRevocation_NoOcspUrl_ReturnsUnavailable()
    {
        // EeCert has no AIA; no override URL supplied.
        var client = Build(Array.Empty<byte>());

        var result = await client.CheckRevocationAsync(
            _pki.EeCert, _pki.CaCert, ocspUrl: null);

        result.Status.Should().Be(RevocationStatus.Unavailable);
        result.ErrorMessage.Should().Contain("OCSP");
    }

    [Fact]
    public async Task CheckRevocation_HttpError_ReturnsUnavailable()
    {
        var client = Build(Array.Empty<byte>(), HttpStatusCode.ServiceUnavailable);

        var result = await client.CheckRevocationAsync(
            _pki.EeCert, _pki.CaCert, "http://ocsp.example.test/");

        result.Status.Should().Be(RevocationStatus.Unavailable);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CheckRevocation_InvalidResponseBytes_ReturnsUnavailable()
    {
        var client = Build(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

        var result = await client.CheckRevocationAsync(
            _pki.EeCert, _pki.CaCert, "http://ocsp.example.test/");

        result.Status.Should().Be(RevocationStatus.Unavailable);
    }

    [Fact]
    public void Constructor_NullHttp_Throws()
    {
        var act = () => new DefaultOcspClient(null!);
        act.Should().Throw<ArgumentNullException>().WithMessage("*httpClient*");
    }

    [Fact]
    public async Task CheckRevocation_NullCertificate_Throws()
    {
        var client = Build(Array.Empty<byte>());
        var act    = async () => await client.CheckRevocationAsync(null!, _pki.CaCert, "http://x/");
        await act.Should().ThrowAsync<ArgumentNullException>().WithMessage("*certificate*");
    }

    [Fact]
    public async Task CheckRevocation_NullIssuer_Throws()
    {
        var client = Build(Array.Empty<byte>());
        var act    = async () => await client.CheckRevocationAsync(_pki.EeCert, null!, "http://x/");
        await act.Should().ThrowAsync<ArgumentNullException>().WithMessage("*issuer*");
    }
}

// ===========================================================================
// DefaultCrlClient tests
// ===========================================================================

public class DefaultCrlClientTests : IDisposable
{
    private readonly FakePki _pki = new();

    public void Dispose() => _pki.Dispose();

    private static DefaultCrlClient Build(byte[] responseBody, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHttpHandler(responseBody, status);
        return new DefaultCrlClient(new HttpClient(handler));
    }

    // -----------------------------------------------------------------------
    [Fact]
    public async Task CheckRevocation_EmptyCrl_ReturnsGood()
    {
        byte[] crl    = _pki.CreateEmptyCrl();
        var client    = Build(crl);

        var result = await client.CheckRevocationAsync(_pki.EeCert, "http://crl.example.test/ca.crl");

        result.Status.Should().Be(RevocationStatus.Good);
        result.CrlBytes.Should().NotBeNull();
        result.CrlUrl.Should().Be("http://crl.example.test/ca.crl");
        result.ThisUpdate.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckRevocation_RevokedCert_ReturnsRevoked()
    {
        var revokedAt = DateTime.UtcNow.AddDays(-1);
        byte[] crl    = _pki.CreateCrlWithRevoked(revokedAt);
        var client    = Build(crl);

        var result = await client.CheckRevocationAsync(_pki.EeCert, "http://crl.example.test/ca.crl");

        result.Status.Should().Be(RevocationStatus.Revoked);
        result.RevocationTime.Should().NotBeNull();
        result.RevocationReason.Should().Be("keyCompromise");
    }

    [Fact]
    public async Task CheckRevocation_NoCdpAndNoUrlSupplied_ReturnsUnavailable()
    {
        var client = Build(Array.Empty<byte>());

        // EeCert has no CDP extension, and we pass no URL.
        var result = await client.CheckRevocationAsync(_pki.EeCert, crlUrl: null);

        result.Status.Should().Be(RevocationStatus.Unavailable);
        result.ErrorMessage.Should().Contain("CRL distribution point");
    }

    [Fact]
    public async Task CheckRevocation_HttpError_ReturnsUnavailable()
    {
        var client = Build(Array.Empty<byte>(), HttpStatusCode.NotFound);

        var result = await client.CheckRevocationAsync(_pki.EeCert, "http://crl.example.test/ca.crl");

        result.Status.Should().Be(RevocationStatus.Unavailable);
    }

    [Fact]
    public async Task CheckRevocation_InvalidCrlBytes_ReturnsUnavailable()
    {
        var client = Build(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE });

        var result = await client.CheckRevocationAsync(_pki.EeCert, "http://crl.example.test/ca.crl");

        result.Status.Should().Be(RevocationStatus.Unavailable);
    }

    [Fact]
    public void Constructor_NullHttp_Throws()
    {
        var act = () => new DefaultCrlClient(null!);
        act.Should().Throw<ArgumentNullException>().WithMessage("*httpClient*");
    }

    [Fact]
    public async Task CheckRevocation_NullCertificate_Throws()
    {
        var client = Build(Array.Empty<byte>());
        var act    = async () => await client.CheckRevocationAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithMessage("*certificate*");
    }

    [Fact]
    public async Task CheckRevocation_Override_UrlUsed()
    {
        byte[] crl = _pki.CreateEmptyCrl();
        string? capturedUrl = null;

        var handler = new CapturingHttpHandler(crl, u => capturedUrl = u);
        var client  = new DefaultCrlClient(new HttpClient(handler));

        await client.CheckRevocationAsync(_pki.EeCert, "http://override.example.test/my.crl");

        capturedUrl.Should().Be("http://override.example.test/my.crl");
    }
}

// ===========================================================================
// Fake HTTP handlers
// ===========================================================================

file sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly byte[]         _body;
    private readonly HttpStatusCode _status;

    public FakeHttpHandler(byte[] body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _body   = body;
        _status = status;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var r = new HttpResponseMessage(_status)
        {
            Content = new ByteArrayContent(_body),
        };
        r.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        return Task.FromResult(r);
    }
}

file sealed class CapturingHttpHandler : HttpMessageHandler
{
    private readonly byte[]          _body;
    private readonly Action<string>  _capture;

    public CapturingHttpHandler(byte[] body, Action<string> capture)
    {
        _body    = body;
        _capture = capture;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        _capture(request.RequestUri!.ToString());
        var r = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(_body),
        };
        return Task.FromResult(r);
    }
}
