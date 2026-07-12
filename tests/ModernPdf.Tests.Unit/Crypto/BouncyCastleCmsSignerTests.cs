// Original implementation based on public standards, no code copied from iText 5/7.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using ModernPdf.Abstractions.Crypto;
using ModernPdf.Crypto;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Cms;
using Org.BouncyCastle.Cms;

namespace ModernPdf.Tests.Unit.Crypto;

/// <summary>
/// Unit tests for <see cref="BouncyCastleCmsSigner"/>.
/// Verifies that the produced CMS SignedData can be verified by BouncyCastle.
/// </summary>
public class BouncyCastleCmsSignerTests : IDisposable
{
    private readonly DefaultDigestService _digestService = new();
    private readonly BouncyCastleCmsSigner _sut;
    private readonly X509Certificate2 _signerCert;
    private readonly RsaSoftwareSignatureProvider _signerProvider;

    public BouncyCastleCmsSignerTests()
    {
        _sut = new BouncyCastleCmsSigner(_digestService);

        // Generate a self-signed RSA 2048 cert for testing
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=PadesSharpTest, O=PadesSharp, C=VN",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: false));

        _signerCert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(2));

        _signerProvider = new RsaSoftwareSignatureProvider(_signerCert);
    }

    // --- Detached CMS is valid DER ---
    [Theory]
    [InlineData(PdfDigestAlgorithm.Sha256)]
    [InlineData(PdfDigestAlgorithm.Sha384)]
    [InlineData(PdfDigestAlgorithm.Sha512)]
    public void CreateDetachedSignature_Produces_ValidDer(PdfDigestAlgorithm algorithm)
    {
        var content = System.Text.Encoding.UTF8.GetBytes("test PDF content");
        var digest = _digestService.ComputeDigest(content, algorithm);

        var request = new CmsSigningRequest
        {
            ContentDigest = digest,
            SigningCertificate = _signerCert,
            CertificateChain = new[] { _signerCert },
            DigestAlgorithm = algorithm,
            SignatureProvider = _signerProvider,
            IncludeSigningCertificateV2 = true
        };

        var cmsBytes = _sut.CreateDetachedSignature(request);

        cmsBytes.Should().NotBeNull().And.NotBeEmpty();

        // Must parse as valid ASN.1 DER
        var act = () => Asn1Object.FromByteArray(cmsBytes);
        act.Should().NotThrow();
    }

    // --- CMS is detached: eContent must be absent ---
    [Fact]
    public void CreateDetachedSignature_EContent_IsAbsent()
    {
        var content = System.Text.Encoding.UTF8.GetBytes("detached test");
        var digest = _digestService.ComputeDigest(content, PdfDigestAlgorithm.Sha256);

        var request = new CmsSigningRequest
        {
            ContentDigest = digest,
            SigningCertificate = _signerCert,
            CertificateChain = new[] { _signerCert },
            DigestAlgorithm = PdfDigestAlgorithm.Sha256,
            SignatureProvider = _signerProvider
        };

        var cmsBytes = _sut.CreateDetachedSignature(request);
        var contentInfo = ContentInfo.GetInstance(Asn1Object.FromByteArray(cmsBytes));
        var signedData = SignedData.GetInstance(contentInfo.Content);

        // Detached: eContent (Content field of EncapContentInfo) must be null/absent
        signedData.EncapContentInfo.Content.Should().BeNull(
            "CMS must be detached — eContent absent per RFC 5652 §5.2");
    }

    // --- Signature verifies with BouncyCastle CmsSignedData ---
    [Theory]
    [InlineData(PdfDigestAlgorithm.Sha256)]
    [InlineData(PdfDigestAlgorithm.Sha384)]
    [InlineData(PdfDigestAlgorithm.Sha512)]
    public void CreateDetachedSignature_SignatureVerifies_WithBouncyCastle(PdfDigestAlgorithm algorithm)
    {
        var content = System.Text.Encoding.UTF8.GetBytes("PDF byte range data to sign");
        var digest = _digestService.ComputeDigest(content, algorithm);

        var request = new CmsSigningRequest
        {
            ContentDigest = digest,
            SigningCertificate = _signerCert,
            CertificateChain = new[] { _signerCert },
            DigestAlgorithm = algorithm,
            SignatureProvider = _signerProvider,
            IncludeSigningCertificateV2 = true
        };

        var cmsBytes = _sut.CreateDetachedSignature(request);

        // Verify using BouncyCastle CmsSignedData with the original content
        var cmsSignedData = new CmsSignedData(
            new CmsProcessableByteArray(content),
            cmsBytes);

        var signers = cmsSignedData.GetSignerInfos();
        signers.GetSigners().Should().HaveCount(1, "exactly one signer expected");

        var bcCert = Org.BouncyCastle.Security.DotNetUtilities.FromX509Certificate(_signerCert);
        foreach (SignerInformation signer in signers.GetSigners())
        {
            var verifies = signer.Verify(bcCert);
            verifies.Should().BeTrue($"CMS signature must verify for {algorithm}");
        }
    }

    // --- Tampered content: signature must NOT verify ---
    [Fact]
    public void CreateDetachedSignature_TamperedContent_VerificationFails()
    {
        var originalContent = System.Text.Encoding.UTF8.GetBytes("original PDF content");
        var tamperedContent = System.Text.Encoding.UTF8.GetBytes("TAMPERED PDF content!");
        var digest = _digestService.ComputeDigest(originalContent, PdfDigestAlgorithm.Sha256);

        var request = new CmsSigningRequest
        {
            ContentDigest = digest,
            SigningCertificate = _signerCert,
            CertificateChain = new[] { _signerCert },
            DigestAlgorithm = PdfDigestAlgorithm.Sha256,
            SignatureProvider = _signerProvider
        };

        var cmsBytes = _sut.CreateDetachedSignature(request);

        // Verify against TAMPERED content
        var cmsSignedData = new CmsSignedData(
            new CmsProcessableByteArray(tamperedContent),
            cmsBytes);

        var bcCert = Org.BouncyCastle.Security.DotNetUtilities.FromX509Certificate(_signerCert);
        var signers = cmsSignedData.GetSignerInfos();
        bool anyFailed = false;
        foreach (SignerInformation signer in signers.GetSigners())
        {
            try
            {
                var verifies = signer.Verify(bcCert);
                if (!verifies) anyFailed = true;
            }
            catch (Org.BouncyCastle.Cms.CmsException)
            {
                anyFailed = true; // BC throws on digest mismatch
            }
        }
        anyFailed.Should().BeTrue("signature over tampered content must NOT verify");
    }

    // --- Certificate chain is embedded ---
    [Fact]
    public void CreateDetachedSignature_EmbedsCertificateChain()
    {
        var content = System.Text.Encoding.UTF8.GetBytes("cert chain test");
        var digest = _digestService.ComputeDigest(content, PdfDigestAlgorithm.Sha256);

        var request = new CmsSigningRequest
        {
            ContentDigest = digest,
            SigningCertificate = _signerCert,
            CertificateChain = new[] { _signerCert },
            DigestAlgorithm = PdfDigestAlgorithm.Sha256,
            SignatureProvider = _signerProvider
        };

        var cmsBytes = _sut.CreateDetachedSignature(request);
        var contentInfo = ContentInfo.GetInstance(Asn1Object.FromByteArray(cmsBytes));
        var signedData = SignedData.GetInstance(contentInfo.Content);

        var certCount = signedData.Certificates?.Count ?? 0;
        certCount.Should().BeGreaterThan(0, "at least the signing certificate must be embedded");
    }

    public void Dispose()
    {
        _signerProvider.Dispose();
        _signerCert.Dispose();
    }
}
