// Original implementation based on public standards, no code copied from iText 5/7.

namespace ModernPdf.Abstractions.Signing;

/// <summary>
/// Result returned after a successful PDF signing operation.
/// </summary>
public sealed class PdfSignResult
{
    /// <summary>Whether signing succeeded without errors.</summary>
    public bool Success { get; set; }

    /// <summary>Name of the signature field that was created.</summary>
    public string SignatureName { get; set; } = string.Empty;

    /// <summary>
    /// The four ByteRange values written into the signature dictionary:
    /// [offset1, length1, offset2, length2].
    /// </summary>
    public long[] ByteRange { get; set; } = System.Array.Empty<long>();

    /// <summary>SHA-256 hash of the raw CMS signature bytes, for use as a VRI key.</summary>
    public byte[] SignatureValueHash { get; set; } = System.Array.Empty<byte>();

    /// <summary>
    /// The raw DER-encoded CMS SignedData bytes that were embedded in /Contents.
    /// Used by LtvDataCollector to compute the SHA-1 VRI key.
    /// </summary>
    public byte[] SignatureCmsBytes { get; set; } = System.Array.Empty<byte>();
}
