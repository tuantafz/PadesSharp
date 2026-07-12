// Original implementation based on public standards, no code copied from iText 5/7.
// Reference: RFC 3161 §3.3, RFC 5652 §5.3 (unsigned attributes), ETSI EN 319 122

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModernPdf.Abstractions.Tsa;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Cms;
using Org.BouncyCastle.Cms;
using BCAttribute = Org.BouncyCastle.Asn1.Cms.Attribute;

namespace ModernPdf.Crypto.Tsa;

/// <summary>
/// Appends an RFC 3161 signature-time-stamp as an unsigned attribute
/// (id-aa-signatureTimeStampToken, OID 1.2.840.113549.1.9.16.2.14)
/// to an existing CMS SignedData byte array.
/// </summary>
public static class TsaAttributeStamper
{
    // id-aa-signatureTimeStampToken OID (RFC 3161 §3.3)
    private static readonly DerObjectIdentifier SignatureTimeStampOid =
        new DerObjectIdentifier("1.2.840.113549.1.9.16.2.14");

    /// <summary>
    /// Parses <paramref name="cmsBytes"/> as a <c>SignedData</c> structure,
    /// timestamps the SignerInfo's <c>signature</c> bytes via <paramref name="tsaClient"/>,
    /// and returns a new DER-encoded <c>SignedData</c> with the
    /// <c>id-aa-signatureTimeStampToken</c> unsigned attribute added.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the TSA call fails or the CMS structure is invalid.
    /// </exception>
    public static async Task<byte[]> AddSignatureTimestampAsync(
        byte[] cmsBytes,
        ITsaClient tsaClient,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        if (cmsBytes is null || cmsBytes.Length == 0)
            throw new ArgumentException("cmsBytes must not be empty.", nameof(cmsBytes));
        if (tsaClient is null)
            throw new ArgumentNullException(nameof(tsaClient));

        logger ??= NullLogger.Instance;

        // Parse the existing CMS SignedData.
        CmsSignedData signedData;
        try
        {
            signedData = new CmsSignedData(cmsBytes);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to parse CMS SignedData for timestamping.", ex);
        }

        // Get the first (and typically only) SignerInfo.
        var signers = signedData.GetSignerInfos().GetSigners().Cast<SignerInformation>().ToList();
        if (signers.Count == 0)
            throw new InvalidOperationException("CMS SignedData contains no SignerInfo entries.");

        // Build updated signer list with timestamp unsigned attribute.
        var updatedSigners = new System.Collections.Generic.List<SignerInformation>(signers.Count);

        foreach (var si in signers)
        {
            byte[] signatureValue = si.GetSignature();
            logger.LogDebug("Timestamping signature value ({Bytes} bytes).", signatureValue.Length);

            TsaTokenResult tsaResult =
                await tsaClient.GetTimestampAsync(signatureValue, cancellationToken)
                    .ConfigureAwait(false);

            if (!tsaResult.Success || tsaResult.TokenBytes is null)
                throw new InvalidOperationException(
                    "TSA returned an error: " + (tsaResult.ErrorMessage ?? "(no detail)"));

            logger.LogDebug("TSA token received, genTime={Time}, {Bytes} bytes.",
                tsaResult.TimestampTime, tsaResult.TokenBytes.Length);

            // Parse the TimeStampToken DER bytes as a raw ASN.1 object.
            Asn1Object tstAsn1;
            try
            {
                tstAsn1 = Asn1Object.FromByteArray(tsaResult.TokenBytes);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to parse TimeStampToken ASN.1.", ex);
            }

            // Build the new unsigned attribute:
            // id-aa-signatureTimeStampToken { OID, SET { TimeStampToken } }
            var tstAttr = new BCAttribute(
                SignatureTimeStampOid,
                new DerSet(tstAsn1));

            // Merge with existing unsigned attributes.
            AttributeTable existingUnsigned = si.UnsignedAttributes ?? new AttributeTable(new DerSet());
            AttributeTable newUnsigned      = existingUnsigned.Add(SignatureTimeStampOid, tstAttr.AttrValues[0]);

            SignerInformation updatedSi = SignerInformation.ReplaceUnsignedAttributes(si, newUnsigned);
            updatedSigners.Add(updatedSi);
        }

        // Replace signers in the original SignedData and re-encode.
        CmsSignedData updatedCms = CmsSignedData.ReplaceSigners(
            signedData,
            new SignerInformationStore(updatedSigners));

        return updatedCms.GetEncoded();
    }
}
