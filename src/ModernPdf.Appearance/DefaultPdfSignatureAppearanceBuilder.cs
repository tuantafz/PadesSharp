// Original implementation based on public standards, no code copied from iText 5/7.
// Reference: ISO 32000-1 §9 (text), §8.4 (graphics state), §12.5.5 (signature appearance)

using System;
using System.Globalization;
using System.Text;
using ModernPdf.Abstractions.Appearance;

namespace ModernPdf.Appearance;

/// <summary>
/// Default implementation of <see cref="IPdfSignatureAppearanceBuilder"/>.
/// Produces a Form XObject with:
/// <list type="bullet">
///   <item>Light-gray background and thin border.</item>
///   <item>Optional JPEG logo image on the left side.</item>
///   <item>Text lines: signer name, reason, location, date — using Helvetica.</item>
/// </list>
/// </summary>
/// <remarks>
/// Standard fonts (Helvetica) support WinAnsiEncoding (ISO 8859-1 subset).
/// Characters outside that range (e.g., full Vietnamese diacritics) require
/// an embedded TrueType/CID font.  This builder provides the infrastructure;
/// a custom <see cref="IPdfSignatureAppearanceBuilder"/> implementation with
/// font embedding can be substituted for full Unicode support.
/// </remarks>
public sealed class DefaultPdfSignatureAppearanceBuilder : IPdfSignatureAppearanceBuilder
{
    // --- layout constants ---------------------------------------------------
    private const string FontRes    = "Helv";   // resource name in /Font dict
    private const float  LabelSize  = 7.0f;     // pt — label lines
    private const float  NameSize   = 9.0f;     // pt — signer name
    private const float  LineGap    = 10.5f;    // pt between baselines
    private const float  Margin     = 4.0f;     // pt inner margin

    // ISO 8859-1 encoder — available on all target frameworks.
    private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public PdfSignatureAppearanceResult Build(PdfSignatureAppearanceRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (request.Width  <= 0) throw new ArgumentException("Width must be > 0.",  nameof(request));
        if (request.Height <= 0) throw new ArgumentException("Height must be > 0.", nameof(request));
        if (request.PageRotation % 90 != 0)
            throw new ArgumentException("PageRotation must be 0, 90, 180 or 270.", nameof(request));

        float w = request.Width;
        float h = request.Height;
        bool  hasImage = request.LogoImageBytes is { Length: > 0 };

        // Text column bounds
        float textX = hasImage ? w * 0.40f : Margin;
        // textRight = w - Margin  (used implicitly by max-char truncation)

        var cs = new StringBuilder();

        // ── Outer graphics-state save ──────────────────────────────────────
        cs.Append("q\n");

        // ── Background rectangle ───────────────────────────────────────────
        cs.Append("0.95 g\n");
        cs.Append(Inv($"0 0 {w:F2} {h:F2} re f\n"));
        cs.Append("0 g\n");

        // ── Border ────────────────────────────────────────────────────────
        cs.Append("0.65 G\n");
        cs.Append("0.5 w\n");
        cs.Append(Inv($"0.25 0.25 {w - 0.5f:F2} {h - 0.5f:F2} re S\n"));
        cs.Append("0 G\n");

        // ── Logo image ─────────────────────────────────────────────────────
        if (hasImage)
        {
            float imgAreaW = w * 0.37f - Margin;
            float imgAreaH = h - 2 * Margin;

            cs.Append("q\n");
            // Clip to image area
            cs.Append(Inv($"{Margin:F2} {Margin:F2} {imgAreaW:F2} {imgAreaH:F2} re W n\n"));
            // Place image: scale to fit the area
            cs.Append(Inv($"{imgAreaW:F2} 0 0 {imgAreaH:F2} {Margin:F2} {Margin:F2} cm\n"));
            cs.Append("/Img0 Do\n");
            cs.Append("Q\n");
        }

        // ── Text block ─────────────────────────────────────────────────────
        float topY = h - Margin - LabelSize;  // baseline of first line

        cs.Append("BT\n");
        bool firstTd = true;

        void WriteLine(string text, float fontSize)
        {
            cs.Append(Inv($"/{FontRes} {fontSize:F1} Tf\n"));
            if (firstTd)
            {
                cs.Append(Inv($"{textX:F2} {topY:F2} Td\n"));
                firstTd = false;
            }
            else
            {
                cs.Append(Inv($"0 {-LineGap:F2} Td\n"));
            }
            cs.Append($"({EscapePdfString(text)}) Tj\n");
        }

        // "Digitally signed by:"
        WriteLine("Digitally signed by:", LabelSize);

        // Signer name — rendered at larger size
        if (!string.IsNullOrEmpty(request.SignerName))
            WriteLine(Truncate(request.SignerName!, 42), NameSize);

        // Reason
        if (request.ShowReason && !string.IsNullOrEmpty(request.Reason))
            WriteLine(Truncate("Reason: " + request.Reason, 42), LabelSize);

        // Location
        if (request.ShowLocation && !string.IsNullOrEmpty(request.Location))
            WriteLine(Truncate("Location: " + request.Location, 42), LabelSize);

        // Date
        if (request.ShowDate)
            WriteLine($"Date: {request.SigningTime:yyyy-MM-dd HH:mm:ss} UTC", LabelSize);

        cs.Append("ET\n");
        cs.Append("Q\n");

        // ── Rotation matrix (applied via /Matrix in the Form XObject dict) ──
        // Handled by the signing engine using GetRotationMatrix().
        // No changes needed in the content stream itself.

        byte[] contentStream = Latin1.GetBytes(cs.ToString());

        // Parse JPEG dimensions if an image is present
        int imgW = 0, imgH = 0;
        if (hasImage)
            (imgW, imgH) = ReadJpegDimensions(request.LogoImageBytes!);

        return new PdfSignatureAppearanceResult
        {
            ContentStream    = contentStream,
            Width            = w,
            Height           = h,
            ImageXObjectData = hasImage ? request.LogoImageBytes : null,
            ImagePixelWidth  = imgW,
            ImagePixelHeight = imgH,
        };
    }

    // -----------------------------------------------------------------------
    // Public helper: rotation matrix for page-rotate compensation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns the six-element PDF CTM (a b c d e f) that, when set as
    /// the Form XObject's /Matrix, compensates for the given page rotation
    /// so the appearance content appears upright.
    /// </summary>
    /// <param name="rotation">Page /Rotate value: 0, 90, 180, or 270.</param>
    /// <param name="width">Appearance width (PDF units).</param>
    /// <param name="height">Appearance height (PDF units).</param>
    public static string GetRotationMatrix(int rotation, float width, float height)
    {
        // PDF rotation is counter-clockwise.  To compensate, rotate CW.
        return rotation switch
        {
            90  => Inv($"0 -1 1 0 0 {width:F2}"),           // 90° CCW → CW: rotate and translate
            180 => Inv($"-1 0 0 -1 {width:F2} {height:F2}"),
            270 => Inv($"0 1 -1 0 {height:F2} 0"),
            _   => "1 0 0 1 0 0",  // 0° — identity
        };
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>Produces an invariant-culture formatted string.</summary>
    private static string Inv(FormattableString fs) =>
        fs.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Escapes PDF literal string special characters: \, (, ).
    /// Characters outside ISO 8859-1 are replaced with '?' so the content
    /// stream remains valid Latin-1.  For full Unicode, embed a Unicode font.
    /// </summary>
    private static string EscapePdfString(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            if (c > 0xFF) { sb.Append('?'); continue; }
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '(':  sb.Append("\\(");  break;
                case ')':  sb.Append("\\)");  break;
                default:   sb.Append(c);      break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Truncates a string to at most <paramref name="maxChars"/> characters.</summary>
    private static string Truncate(string s, int maxChars) =>
        s.Length <= maxChars ? s : s.Substring(0, maxChars - 1) + "\u2026";  // U+2026 → '?' in Latin-1

    /// <summary>
    /// Extracts pixel dimensions from a JPEG byte stream by scanning for the
    /// SOF0 (0xFFC0) or SOF2 (0xFFC2) marker.
    /// </summary>
    private static (int width, int height) ReadJpegDimensions(byte[] jpeg)
    {
        for (int i = 0; i < jpeg.Length - 8; i++)
        {
            if (jpeg[i] != 0xFF) continue;
            byte marker = jpeg[i + 1];
            if (marker == 0xC0 || marker == 0xC2)
            {
                // SOF: FF Cn [length 2B] [precision 1B] [height 2B] [width 2B]
                int h = (jpeg[i + 5] << 8) | jpeg[i + 6];
                int w = (jpeg[i + 7] << 8) | jpeg[i + 8];
                return (w, h);
            }
        }
        return (100, 100); // safe fallback
    }
}
