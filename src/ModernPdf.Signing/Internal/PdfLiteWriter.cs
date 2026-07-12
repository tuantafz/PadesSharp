// Original implementation based on public standards, no code copied from iText 5/7.
// Reference: ISO 32000-1 §7 (syntax), §12.8 (digital signatures)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ModernPdf.Signing.Internal;

/// <summary>
/// State captured while writing the signature placeholder.
/// </summary>
internal sealed class SignaturePlaceholderState
{
    /// <summary>Byte offset of the opening '[' of the /ByteRange value.</summary>
    public long ByteRangeValueOffset { get; set; }

    /// <summary>Total byte length of the fixed-width /ByteRange value placeholder.</summary>
    public int ByteRangeValueLength { get; set; }

    /// <summary>
    /// Byte offset of the '&lt;' character that opens the /Contents hex string.
    /// </summary>
    public long ContentsHexStart { get; set; }

    /// <summary>Number of hex characters (not bytes) in the /Contents placeholder.</summary>
    public int ContentsHexLength { get; set; }
}

/// <summary>
/// Writes a minimal, standards-conformant PDF structure directly to a stream,
/// reserving fixed-width placeholders for /ByteRange and /Contents so that
/// they can be overwritten with real values during the second signing pass.
/// </summary>
/// <remarks>
/// Output format is compatible with ISO 32000-1 / PDF 1.6.
/// </remarks>
internal sealed class PdfLiteWriter
{
    // Number of decimal digit columns for each /ByteRange element.
    // 10 digits → max value 9,999,999,999 (~9.3 GB).
    private const int ByteRangeFieldWidth = 10;

    // Total placeholder text for /ByteRange value:
    //   [0000000000 0000000000 0000000000 0000000000]
    //    10          10         10         10         = 40 + 3 spaces + 2 brackets = 45 chars
    private static readonly int ByteRangePlaceholderLength =
        2 + 4 * ByteRangeFieldWidth + 3; // '[' + 4×10 digits + 3 spaces + ']'

    private readonly Stream _stream;

    internal PdfLiteWriter(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    internal long Position => _stream.Position;

    // -----------------------------------------------------------------------
    // Public write helpers
    // -----------------------------------------------------------------------

    // ISO 8859-1 (Latin-1) encoder — available on all target frameworks.
    private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

    internal void WriteRaw(string s)
    {
        var bytes = Latin1.GetBytes(s);
        _stream.Write(bytes, 0, bytes.Length);
    }

    internal void WriteRaw(byte[] bytes)
    {
        _stream.Write(bytes, 0, bytes.Length);
    }

    internal void WriteNewline() => WriteRaw("\n");

    internal void WriteLine(string s) { WriteRaw(s); WriteNewline(); }

    // -----------------------------------------------------------------------
    // High-level PDF structure writers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Writes the PDF header (%PDF-x.y and a binary comment line).
    /// </summary>
    internal void WriteHeader(string version = "1.6")
    {
        WriteLine($"%PDF-{version}");
        // Four bytes > 127 signal that the file contains binary data.
        WriteRaw(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' });
    }

    /// <summary>
    /// Writes a direct dictionary string (between &lt;&lt; and &gt;&gt;).
    /// The caller must write "N G obj\n" and "endobj\n" around this.
    /// </summary>
    internal void WriteBeginObj(int num, int gen = 0)
    {
        WriteRaw($"{num} {gen} obj\n");
    }

    internal void WriteEndObj() => WriteRaw("endobj\n");

    /// <summary>
    /// Writes a minimal Catalog object.
    /// </summary>
    internal long WriteCatalog(int objNum, int pagesObjNum, int acroFormObjNum)
    {
        long offset = Position;
        WriteBeginObj(objNum);
        WriteRaw($"<< /Type /Catalog /Pages {pagesObjNum} 0 R /AcroForm {acroFormObjNum} 0 R >>\n");
        WriteEndObj();
        return offset;
    }

    /// <summary>Writes a /Pages dict containing a single page reference.</summary>
    internal long WritePages(int objNum, int pageObjNum)
    {
        long offset = Position;
        WriteBeginObj(objNum);
        WriteRaw($"<< /Type /Pages /Kids [{pageObjNum} 0 R] /Count 1 >>\n");
        WriteEndObj();
        return offset;
    }

    /// <summary>Writes a blank A4 page that includes the signature annotation.</summary>
    internal long WritePage(int objNum, int pagesObjNum, int sigWidgetObjNum)
    {
        long offset = Position;
        WriteBeginObj(objNum);
        WriteRaw($"<< /Type /Page /Parent {pagesObjNum} 0 R /MediaBox [0 0 595 842]\n");
        WriteRaw($"   /Annots [{sigWidgetObjNum} 0 R] >>\n");
        WriteEndObj();
        return offset;
    }

    /// <summary>Writes an AcroForm dictionary with SigFlags=3 (signatures exist + append only).</summary>
    internal long WriteAcroForm(int objNum, int sigWidgetObjNum)
    {
        long offset = Position;
        WriteBeginObj(objNum);
        WriteRaw($"<< /Fields [{sigWidgetObjNum} 0 R] /SigFlags 3 /DA (/Helv 12 Tf 0 g) >>\n");
        WriteEndObj();
        return offset;
    }

    /// <summary>Writes a signature widget annotation (invisible, 0×0 rectangle).</summary>
    internal long WriteSignatureWidget(int objNum, int pageObjNum, int sigValueObjNum, string fieldName)
    {
        long offset = Position;
        WriteBeginObj(objNum);
        WriteRaw($"<< /Type /Annot /Subtype /Widget /FT /Sig\n");
        WriteRaw($"   /T ({EscapePdfString(fieldName)})\n");
        WriteRaw($"   /Rect [0 0 0 0]\n");
        WriteRaw($"   /F 4\n");
        WriteRaw($"   /P {pageObjNum} 0 R\n");
        WriteRaw($"   /V {sigValueObjNum} 0 R >>\n");
        WriteEndObj();
        return offset;
    }

    /// <summary>
    /// Writes a visible signature widget annotation with an appearance stream reference.
    /// </summary>
    /// <param name="objNum">Object number for this widget.</param>
    /// <param name="pageObjNum">Page object reference.</param>
    /// <param name="sigValueObjNum">Signature value object reference.</param>
    /// <param name="fieldName">AcroForm field name.</param>
    /// <param name="rect">Visible rectangle [x y x+w y+h].</param>
    /// <param name="normalApObjNum">Object number of the /N Form XObject for the AP.</param>
    internal long WriteVisibleSignatureWidget(
        int objNum, int pageObjNum, int sigValueObjNum, string fieldName,
        float[] rect, int normalApObjNum)
    {
        long offset = Position;
        WriteBeginObj(objNum);
        WriteRaw($"<< /Type /Annot /Subtype /Widget /FT /Sig\n");
        WriteRaw($"   /T ({EscapePdfString(fieldName)})\n");
        WriteRaw($"   /Rect [{rect[0].ToString("F2", CultureInfo.InvariantCulture)} " +
                 $"{rect[1].ToString("F2", CultureInfo.InvariantCulture)} " +
                 $"{rect[2].ToString("F2", CultureInfo.InvariantCulture)} " +
                 $"{rect[3].ToString("F2", CultureInfo.InvariantCulture)}]\n");
        WriteRaw($"   /F 4\n");
        WriteRaw($"   /P {pageObjNum} 0 R\n");
        WriteRaw($"   /AP << /N {normalApObjNum} 0 R >>\n");
        WriteRaw($"   /V {sigValueObjNum} 0 R >>\n");
        WriteEndObj();
        return offset;
    }

    /// <summary>
    /// Writes a Form XObject stream object (for the /N appearance entry).
    /// </summary>
    /// <param name="objNum">Object number.</param>
    /// <param name="width">BBox width.</param>
    /// <param name="height">BBox height.</param>
    /// <param name="matrix">Optional /Matrix value string, e.g. "1 0 0 1 0 0".</param>
    /// <param name="hasImageResource">If <c>true</c>, include /XObject /Img0 in resources.</param>
    /// <param name="imageObjNum">Image XObject object number (used when <paramref name="hasImageResource"/> is <c>true</c>).</param>
    /// <param name="contentStream">Uncompressed content stream bytes.</param>
    internal long WriteFormXObject(
        int objNum, float width, float height,
        string matrix, bool hasImageResource, int imageObjNum,
        byte[] contentStream)
    {
        long offset = Position;
        WriteBeginObj(objNum);
        WriteRaw($"<< /Type /XObject /Subtype /Form\n");
        WriteRaw($"   /BBox [0 0 {width.ToString("F2", CultureInfo.InvariantCulture)} " +
                 $"{height.ToString("F2", CultureInfo.InvariantCulture)}]\n");
        WriteRaw($"   /Matrix [{matrix}]\n");
        WriteRaw($"   /Resources <<\n");
        // Helvetica — standard Type1 font, no embedding required
        WriteRaw($"     /Font << /Helv << /Type /Font /Subtype /Type1 " +
                 $"/BaseFont /Helvetica /Encoding /WinAnsiEncoding >> >>\n");
        if (hasImageResource)
            WriteRaw($"     /XObject << /Img0 {imageObjNum} 0 R >>\n");
        WriteRaw($"   >>\n");
        WriteRaw($"   /Length {contentStream.Length}\n");
        WriteRaw(">>\n");
        WriteRaw("stream\n");
        WriteRaw(contentStream);
        WriteRaw("\nendstream\n");
        WriteEndObj();
        return offset;
    }

    /// <summary>
    /// Writes a JPEG Image XObject stream object.
    /// </summary>
    internal long WriteJpegImageXObject(
        int objNum, int pixelWidth, int pixelHeight, byte[] jpegBytes)
    {
        long offset = Position;
        WriteBeginObj(objNum);
        WriteRaw($"<< /Type /XObject /Subtype /Image\n");
        WriteRaw($"   /Width {pixelWidth} /Height {pixelHeight}\n");
        WriteRaw($"   /ColorSpace /DeviceRGB /BitsPerComponent 8\n");
        WriteRaw($"   /Filter /DCTDecode\n");
        WriteRaw($"   /Length {jpegBytes.Length}\n");
        WriteRaw(">>\n");
        WriteRaw("stream\n");
        WriteRaw(jpegBytes);
        WriteRaw("\nendstream\n");
        WriteEndObj();
        return offset;
    }

    /// <summary>
    /// Writes the signature value dictionary (/Type /Sig) with fixed-width placeholders
    /// for /ByteRange and /Contents.  Returns state describing their byte positions.
    /// </summary>
    internal (long offset, SignaturePlaceholderState state) WriteSignatureValue(
        int objNum,
        string subFilter,
        string? reason,
        string? location,
        DateTimeOffset signingTime,
        int contentSizeBytes)
    {
        long offset = Position;
        WriteBeginObj(objNum);
        WriteRaw("<< /Type /Sig\n");
        WriteRaw($"   /Filter /Adobe.PPKLite\n");
        WriteRaw($"   /SubFilter /{subFilter}\n");

        // /ByteRange — fixed-width placeholder
        WriteRaw("   /ByteRange ");
        long byteRangeValueOffset = Position;
        WriteRaw(BuildByteRangePlaceholder()); // 45 chars
        WriteNewline();

        // /Contents — fixed-width hex placeholder
        WriteRaw("   /Contents ");
        long contentsHexStart = Position; // offset of '<'
        int hexLen = contentSizeBytes * 2;
        WriteRaw("<");
        WriteRaw(new string('0', hexLen));
        WriteRaw(">");
        WriteNewline();

        // /M (PDF date string)
        WriteRaw($"   /M ({FormatPdfDate(signingTime)})\n");

        if (!string.IsNullOrEmpty(reason))
            WriteRaw($"   /Reason ({EscapePdfString(reason!)})\n");
        if (!string.IsNullOrEmpty(location))
            WriteRaw($"   /Location ({EscapePdfString(location!)})\n");

        WriteRaw(">>\n");
        WriteEndObj();

        var state = new SignaturePlaceholderState
        {
            ByteRangeValueOffset = byteRangeValueOffset,
            ByteRangeValueLength = ByteRangePlaceholderLength,
            ContentsHexStart = contentsHexStart,
            ContentsHexLength = hexLen
        };
        return (offset, state);
    }

    /// <summary>
    /// Writes a cross-reference table and trailer, then returns startxref offset.
    /// </summary>
    internal void WriteXrefAndTrailer(
        long[] objectOffsets,   // offsets[i] = byte offset of object (i+1) (1-based)
        int catalogObjNum,
        long startxrefOffset)
    {
        // Cross-reference section
        int objCount = objectOffsets.Length + 1; // +1 for obj 0 (free head)
        WriteRaw($"xref\n0 {objCount}\n");
        // Entry 0: free head
        WriteRaw("0000000000 65535 f \n");
        foreach (long off in objectOffsets)
            WriteRaw(off.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");

        // Trailer
        WriteRaw($"trailer\n<< /Size {objCount} /Root {catalogObjNum} 0 R >>\n");
        WriteRaw($"startxref\n{startxrefOffset}\n%%EOF\n");
    }

    // -----------------------------------------------------------------------
    // Seek-and-overwrite helpers (used after CMS is built)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Seeks back and overwrites the /ByteRange placeholder with the real values.
    /// Values are right-justified within their column width so total length stays constant.
    /// </summary>
    internal void PatchByteRange(SignaturePlaceholderState state, long[] values)
    {
        if (values.Length != 4)
            throw new ArgumentException("ByteRange must have exactly 4 elements.", nameof(values));

        var sb = new StringBuilder();
        sb.Append('[');
        for (int i = 0; i < 4; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(values[i].ToString(CultureInfo.InvariantCulture).PadLeft(ByteRangeFieldWidth));
        }
        sb.Append(']');

        string text = sb.ToString();
        if (text.Length != state.ByteRangeValueLength)
            throw new InvalidOperationException(
                $"ByteRange placeholder mismatch: expected {state.ByteRangeValueLength}, got {text.Length}.");

        long savedPos = _stream.Position;
        _stream.Seek(state.ByteRangeValueOffset, SeekOrigin.Begin);
        WriteRaw(text);
        _stream.Seek(savedPos, SeekOrigin.Begin);
    }

    /// <summary>
    /// Seeks back and overwrites the /Contents hex placeholder with the CMS bytes
    /// (zero-padded on the right to fill the reserved space).
    /// </summary>
    internal void PatchContents(SignaturePlaceholderState state, byte[] cmsBytes)
    {
        int maxBytes = state.ContentsHexLength / 2;
        if (cmsBytes.Length > maxBytes)
            throw new ArgumentException(
                $"CMS bytes ({cmsBytes.Length}) exceed reserved /Contents size ({maxBytes}).");

        // Hex-encode CMS, then zero-pad to fill the placeholder.
        // Use '0' fill (not '\0') so the PDF hex string is valid ASCII.
        var hex = new char[state.ContentsHexLength];
        for (int i = 0; i < hex.Length; i++) hex[i] = '0'; // pre-fill padding
        for (int i = 0; i < cmsBytes.Length; i++)
        {
            hex[i * 2]     = ToHexChar(cmsBytes[i] >> 4);
            hex[i * 2 + 1] = ToHexChar(cmsBytes[i] & 0x0F);
        }
        // Remaining chars already '0' (padding)

        long savedPos = _stream.Position;
        // +1 to skip the opening '<'
        _stream.Seek(state.ContentsHexStart + 1, SeekOrigin.Begin);
        WriteRaw(new string(hex));
        _stream.Seek(savedPos, SeekOrigin.Begin);
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Writes an arbitrary PDF object with the provided dictionary (or other) body.
    /// Useful for writing updated/new objects during incremental signing.
    /// </summary>
    internal long WriteRawObject(int objNum, string body)
    {
        long offset = Position;
        WriteBeginObj(objNum);
        WriteRaw(body);
        WriteNewline();
        WriteEndObj();
        return offset;
    }

    private static string BuildByteRangePlaceholder()
    {
        // [0000000000 0000000000 0000000000 0000000000]
        var sb = new StringBuilder("[");
        for (int i = 0; i < 4; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(new string('0', ByteRangeFieldWidth));
        }
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>
    /// Formats a <see cref="DateTimeOffset"/> as a PDF date string:
    /// D:YYYYMMDDHHmmSSOHH'mm'
    /// </summary>
    private static string FormatPdfDate(DateTimeOffset dt)
    {
        int ohh = Math.Abs((int)dt.Offset.TotalHours);
        int omm = Math.Abs(dt.Offset.Minutes);
        char sign = dt.Offset >= TimeSpan.Zero ? '+' : '-';
        return $"D:{dt:yyyyMMddHHmmss}{sign}{ohh:D2}'{omm:D2}'";
    }

    /// <summary>
    /// Escapes special characters in a PDF literal string (parentheses and backslash).
    /// </summary>
    private static string EscapePdfString(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private static char ToHexChar(int nibble) =>
        (char)(nibble < 10 ? '0' + nibble : 'a' + nibble - 10);
}
