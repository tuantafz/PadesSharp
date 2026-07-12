// Original implementation based on public standards, no code copied from iText 5/7.

namespace ModernPdf.Abstractions.Appearance;

/// <summary>
/// The rendered output of <see cref="IPdfSignatureAppearanceBuilder.Build"/>.
/// Contains all data needed to embed a Form XObject (and an optional Image XObject)
/// into a PDF signature annotation.
/// </summary>
public sealed class PdfSignatureAppearanceResult
{
    /// <summary>
    /// Uncompressed PDF content stream for the normal (/N) appearance.
    /// This is the body of the Form XObject stream object.
    /// </summary>
    public byte[] ContentStream { get; set; } = System.Array.Empty<byte>();

    /// <summary>Width of the Form XObject BBox (matches <see cref="PdfSignatureAppearanceRequest.Width"/>).</summary>
    public float Width { get; set; }

    /// <summary>Height of the Form XObject BBox.</summary>
    public float Height { get; set; }

    /// <summary>Raw JPEG bytes for the logo image XObject, or <c>null</c> if no image.</summary>
    public byte[]? ImageXObjectData { get; set; }

    /// <summary>Pixel width of the logo image (only meaningful when <see cref="HasImage"/> is <c>true</c>).</summary>
    public int ImagePixelWidth { get; set; }

    /// <summary>Pixel height of the logo image.</summary>
    public int ImagePixelHeight { get; set; }

    /// <summary>Returns <c>true</c> when a logo image is included.</summary>
    public bool HasImage => ImageXObjectData is { Length: > 0 };
}
