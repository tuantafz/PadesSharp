// Original implementation based on public standards, no code copied from iText 5/7.

namespace ModernPdf.Abstractions.Crypto;

/// <summary>
/// Creates CMS detached signatures (SignedData) as defined in RFC 5652.
/// The signature is suitable for embedding in a PDF /Contents entry.
/// </summary>
public interface ICmsSigner
{
    /// <summary>
    /// Creates a DER-encoded CMS SignedData structure with a detached signature.
    /// </summary>
    /// <param name="request">All parameters needed to build the signature.</param>
    /// <returns>DER-encoded CMS SignedData bytes to be placed in PDF /Contents.</returns>
    byte[] CreateDetachedSignature(CmsSigningRequest request);
}
