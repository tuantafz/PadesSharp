// Original implementation based on public standards, no code copied from iText 5/7.

using System;

namespace ModernPdf.Abstractions.Appearance;

/// <summary>
/// Parameters for building a visible PDF signature appearance (Form XObject).
/// </summary>
public sealed class PdfSignatureAppearanceRequest
{
    /// <summary>Name of the signer, displayed prominently.</summary>
    public string? SignerName { get; set; }

    /// <summary>Signing reason text.</summary>
    public string? Reason { get; set; }

    /// <summary>Signing location text.</summary>
    public string? Location { get; set; }

    /// <summary>Signing time used for the "Date:" line.</summary>
    public DateTimeOffset SigningTime { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Width of the appearance in PDF user units (pts). Default 200.</summary>
    public float Width { get; set; } = 200f;

    /// <summary>Height of the appearance in PDF user units (pts). Default 60.</summary>
    public float Height { get; set; } = 60f;

    /// <summary>Whether to render the date line. Default <c>true</c>.</summary>
    public bool ShowDate { get; set; } = true;

    /// <summary>Whether to render the reason line. Default <c>true</c>.</summary>
    public bool ShowReason { get; set; } = true;

    /// <summary>Whether to render the location line. Default <c>true</c>.</summary>
    public bool ShowLocation { get; set; } = true;

    /// <summary>
    /// Optional JPEG logo image bytes to display on the left side.
    /// When set, text is pushed to the right half of the rectangle.
    /// </summary>
    public byte[]? LogoImageBytes { get; set; }

    /// <summary>
    /// Placement of the signature field on the target page (lower-left origin).
    /// Required when the appearance is applied to a signed PDF.
    /// </summary>
    public PdfSignatureRectangle? Rectangle { get; set; }

    /// <summary>1-based page number on which to show the signature.</summary>
    public int PageNumber { get; set; } = 1;

    /// <summary>
    /// Page rotation in degrees (0, 90, 180, 270).  Used to apply a compensating
    /// rotation matrix to the Form XObject so the content appears upright.
    /// </summary>
    public int PageRotation { get; set; } = 0;
}
