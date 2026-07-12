// Original implementation based on public standards, no code copied from iText 5/7.

namespace ModernPdf.Abstractions.Appearance;

/// <summary>
/// Defines the position and size of a visible signature field
/// within a PDF page, in PDF user units (1/72 inch).
/// Origin is the lower-left corner of the page.
/// </summary>
public sealed class PdfSignatureRectangle
{
    /// <summary>X coordinate of the lower-left corner.</summary>
    public float X { get; set; }

    /// <summary>Y coordinate of the lower-left corner.</summary>
    public float Y { get; set; }

    /// <summary>Width of the signature rectangle.</summary>
    public float Width { get; set; }

    /// <summary>Height of the signature rectangle.</summary>
    public float Height { get; set; }
}
