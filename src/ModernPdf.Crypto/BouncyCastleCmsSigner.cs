// Original implementation based on public standards, no code copied from iText 5/7.
// Based on: RFC 5652 (CMS), RFC 5035 (ESS signingCertificateV2), RFC 5126 (CAdES).

using System.Security.Cryptography.X509Certificates;
using ModernPdf.Abstractions.Crypto;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Ess;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Security;
using BCAttribute = Org.BouncyCastle.Asn1.Cms.Attribute;
using BCCmsAttr = Org.BouncyCastle.Asn1.Cms.CmsAttributes;
using BCContentInfo = Org.BouncyCastle.Asn1.Cms.ContentInfo;
using BCCmsOid = Org.BouncyCastle.Asn1.Cms.CmsObjectIdentifiers;
using BCIssuerAndSerial = Org.BouncyCastle.Asn1.Cms.IssuerAndSerialNumber;
using BCSignedData = Org.BouncyCastle.Asn1.Cms.SignedData;
using BCSignerIdentifier = Org.BouncyCastle.Asn1.Cms.SignerIdentifier;
using BCSignerInfo = Org.BouncyCastle.Asn1.Cms.SignerInfo;
using BCX509 = Org.BouncyCastle.X509;

namespace ModernPdf.Crypto;

/// <summary>
/// Creates CMS detached signatures (SignedData) using BouncyCastle ASN.1 primitives,
/// as defined in RFC 5652. Supports CAdES-BES via the signingCertificateV2 attribute
/// (RFC 5035 / ETSI EN 319 122).
/// </summary>
public sealed class BouncyCastleCmsSigner : ICmsSigner
{
    // id-smime-aa-signingCertificateV2 OID (RFC 5035)
    private static readonly DerObjectIdentifier SigningCertificateV2Oid =
        new DerObjectIdentifier("1.2.840.113549.1.9.16.2.47");

    private readonly IDigestService _digestService;

    /// <summary>
    /// Initialises a new instance of <see cref="BouncyCastleCmsSigner"/>.
    /// </summary>
    /// <param name="digestService">Used to compute the digest of signed attributes.</param>
    public BouncyCastleCmsSigner(IDigestService digestService)
    {
        _digestService = digestService ?? throw new ArgumentNullException(nameof(digestService));
    }

    /// <inheritdoc/>
    public byte[] CreateDetachedSignature(CmsSigningRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (request.ContentDigest is null || request.ContentDigest.Length == 0)
            throw new ArgumentException("ContentDigest must be set.", nameof(request));
        if (request.SigningCertificate is null)
            throw new ArgumentException("SigningCertificate must be set.", nameof(request));
        if (request.SignatureProvider is null)
            throw new ArgumentException("SignatureProvider must be set.", nameof(request));

        var certSource = request.CertificateChain
            ?? (IReadOnlyList<X509Certificate2>)new[] { request.SigningCertificate };
        var bcCerts = certSource.Select(c => DotNetUtilities.FromX509Certificate(c)).ToList();
        var signerCert = DotNetUtilities.FromX509Certificate(request.SigningCertificate);

        // Build signed attributes vector
        var signedAttrsVec = BuildSignedAttributes(request, signerCert);

        // Encode as DER SET — this is the data that gets signed (RFC 5652 §5.4)
        var encodedSignedAttrs = new DerSet(signedAttrsVec).GetEncoded(Asn1Encodable.Der);

        // Digest then sign
        var signedAttrsDigest = _digestService.ComputeDigest(encodedSignedAttrs, request.DigestAlgorithm);
        var signatureBytes = request.SignatureProvider.SignDigest(signedAttrsDigest, request.DigestAlgorithm);

        return BuildSignedData(request, bcCerts, signedAttrsVec, signatureBytes);
    }

    // -----------------------------------------------------------------------

    private Asn1EncodableVector BuildSignedAttributes(
        CmsSigningRequest request,
        BCX509.X509Certificate signerCert)
    {
        var attrs = new Asn1EncodableVector();

        // contentType (RFC 5652 §11.1)
        attrs.Add(new BCAttribute(BCCmsAttr.ContentType, new DerSet(PkcsObjectIdentifiers.Data)));

        // signingTime (RFC 5652 §11.3)
        attrs.Add(new BCAttribute(BCCmsAttr.SigningTime, new DerSet(new DerUtcTime(request.SigningTime.UtcDateTime, 2049))));

        // messageDigest (RFC 5652 §11.2)
        attrs.Add(new BCAttribute(BCCmsAttr.MessageDigest, new DerSet(new DerOctetString(request.ContentDigest))));

        // signingCertificateV2 / CAdES-BES (RFC 5035)
        if (request.IncludeSigningCertificateV2)
            attrs.Add(BuildSigningCertificateV2Attribute(request.DigestAlgorithm, signerCert));

        return attrs;
    }

    private BCAttribute BuildSigningCertificateV2Attribute(
        PdfDigestAlgorithm digestAlgorithm,
        BCX509.X509Certificate signerCert)
    {
        var certDer = signerCert.GetEncoded();
        var certHash = _digestService.ComputeDigest(certDer, digestAlgorithm);
        var algId = new AlgorithmIdentifier(
            new DerObjectIdentifier(_digestService.GetDigestOid(digestAlgorithm)),
            DerNull.Instance);
        var essCertId = new EssCertIDv2(algId, certHash);
        var signingCertV2 = new SigningCertificateV2(new[] { essCertId });
        return new BCAttribute(SigningCertificateV2Oid, new DerSet(signingCertV2));
    }

    private static byte[] BuildSignedData(
        CmsSigningRequest request,
        IList<BCX509.X509Certificate> bcCerts,
        Asn1EncodableVector signedAttrs,
        byte[] signatureBytes)
    {
        var digestAlgId = new AlgorithmIdentifier(
            new DerObjectIdentifier(GetDigestOidForAlgorithm(request.DigestAlgorithm)),
            DerNull.Instance);

        var sigAlgId = new AlgorithmIdentifier(
            new DerObjectIdentifier(request.SignatureProvider.SignatureAlgorithmOid),
            DerNull.Instance);

        var signerCertBc = DotNetUtilities.FromX509Certificate(request.SigningCertificate);
        var issuerAndSerial = new BCIssuerAndSerial(signerCertBc.IssuerDN, signerCertBc.SerialNumber);

        var signerInfo = new BCSignerInfo(
            new BCSignerIdentifier(issuerAndSerial),
            digestAlgId,
            new DerSet(signedAttrs),
            sigAlgId,
            new DerOctetString(signatureBytes),
            null);  // unsignedAttrs: TSA token added later

        var digestAlgsVec = new Asn1EncodableVector();
        digestAlgsVec.Add(digestAlgId);

        var certVector = new Asn1EncodableVector();
        foreach (var cert in bcCerts)
            certVector.Add(X509CertificateStructure.GetInstance(Asn1Object.FromByteArray(cert.GetEncoded())));

        // Detached: eContent is ABSENT
        var encapContentInfo = new BCContentInfo(PkcsObjectIdentifiers.Data, null);

        var signedData = new BCSignedData(
            new DerSet(digestAlgsVec),
            encapContentInfo,
            new BerSet(certVector),
            null,
            new DerSet(signerInfo));

        var contentInfo = new BCContentInfo(BCCmsOid.SignedData, signedData);
        return contentInfo.GetEncoded(Asn1Encodable.Der);
    }

    private static string GetDigestOidForAlgorithm(PdfDigestAlgorithm algorithm) => algorithm switch
    {
        PdfDigestAlgorithm.Sha256 => "2.16.840.1.101.3.4.2.1",
        PdfDigestAlgorithm.Sha384 => "2.16.840.1.101.3.4.2.2",
        PdfDigestAlgorithm.Sha512 => "2.16.840.1.101.3.4.2.3",
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
    };
}
