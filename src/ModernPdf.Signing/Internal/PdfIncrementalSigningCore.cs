// Original implementation based on public standards, no code copied from iText 5/7.
// Reference: ISO 32000-1 §12.8.3 (incremental updates), §12.8 (digital signatures)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ModernPdf.Abstractions.Appearance;
using ModernPdf.Abstractions.Signing;

namespace ModernPdf.Signing.Internal;

/// <summary>
/// Appends a digital signature to an existing PDF document as an incremental update
/// (ISO 32000-1 §12.8.3).  The original document bytes are never modified; new objects
/// are appended and a new cross-reference section + trailer are written at the end.
/// </summary>
internal static class PdfIncrementalSigningCore
{
    private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

    /// <summary>
    /// Reads the existing PDF from <paramref name="inputBytes"/>, appends a new
    /// signature field and placeholder as an incremental update, and returns the
    /// combined bytes together with the <see cref="SignaturePlaceholderState"/> for
    /// ByteRange / Contents patching by the calling engine.
    /// </summary>
    internal static (byte[] pdfBytes, SignaturePlaceholderState state) AppendSignature(
        byte[] inputBytes,
        PdfSignRequest request,
        DateTimeOffset signingTime,
        PdfSignatureAppearanceResult? appearance,
        int contentSize)
    {
        // Normalize CRLF → LF so all string searches work regardless of PDF line endings.
        // Latin-1 decode is byte-safe: no multi-byte sequences, so offset arithmetic on
        // the normalized string is invalid for byte-range calculation — we keep inputBytes
        // for all actual byte writes and only use pdfText for structural parsing.
        string pdfText = Latin1.GetString(inputBytes).Replace("\r\n", "\n").Replace("\r", "\n");

        // Parse the existing PDF structure: trailer → Catalog → Pages → first Page
        var (prevStartxref, prevSize, catalogObjNum) = ParseTrailer(pdfText);
        string catalogDict = ExtractLastObjectDict(pdfText, catalogObjNum);

        int pagesRef = ParseRef(catalogDict, "/Pages")
            ?? throw new InvalidOperationException(
                "PDF Catalog has no /Pages reference — document may be corrupt.");

        int? acroFormRef = ParseRef(catalogDict, "/AcroForm");

        string pagesDict   = ExtractLastObjectDict(pdfText, pagesRef);
        int firstPageRef   = ParseFirstKid(pagesDict)
            ?? throw new InvalidOperationException(
                "PDF has no pages — document may be corrupt.");

        string firstPageDict      = ExtractLastObjectDict(pdfText, firstPageRef);
        string? existingAcroForm  = acroFormRef.HasValue
            ? ExtractLastObjectDict(pdfText, acroFormRef.Value)
            : null;

        // Assign object numbers for the new objects.  All start at prevSize (next free slot).
        int nextNum         = prevSize;
        int sigValueObjNum  = nextNum++;
        int sigWidgetObjNum = nextNum++;
        bool isNewAcroForm  = !acroFormRef.HasValue;
        int acroFormObjNum  = isNewAcroForm ? nextNum++ : acroFormRef!.Value;
        int? formXObjNum    = appearance != null ? nextNum++ : (int?)null;
        int? imageXObjNum   = (appearance?.HasImage == true) ? nextNum++ : (int?)null;

        // Start the incremental update after a copy of the original bytes.
        using var ms = new MemoryStream(inputBytes.Length + 65536);
        ms.Write(inputBytes, 0, inputBytes.Length);

        var w       = new PdfLiteWriter(ms);
        var offsets = new List<(int ObjNum, long Offset)>();

        // ── 1. Signature value object (ByteRange + Contents placeholders) ─────
        offsets.Add((sigValueObjNum, ms.Position));
        var (_, state) = w.WriteSignatureValue(
            sigValueObjNum, request.SubFilter, request.Reason, request.Location,
            signingTime, contentSize);

        // ── 2. Signature widget annotation ────────────────────────────────────
        offsets.Add((sigWidgetObjNum, ms.Position));
        if (appearance != null && request.Appearance?.Rectangle != null && formXObjNum.HasValue)
        {
            var r = request.Appearance.Rectangle;
            w.WriteVisibleSignatureWidget(
                sigWidgetObjNum, firstPageRef, sigValueObjNum,
                request.SignatureName,
                new[] { r.X, r.Y, r.X + r.Width, r.Y + r.Height },
                formXObjNum.Value);
        }
        else
        {
            w.WriteSignatureWidget(sigWidgetObjNum, firstPageRef, sigValueObjNum,
                request.SignatureName);
        }

        // ── 3. AcroForm — updated (existing fields + new widget) or brand new ─
        offsets.Add((acroFormObjNum, ms.Position));
        WriteAcroForm(w, acroFormObjNum, existingAcroForm, sigWidgetObjNum);

        // ── 4. First page — add signature widget to /Annots ───────────────────
        offsets.Add((firstPageRef, ms.Position));
        WriteUpdatedPage(w, firstPageRef, firstPageDict, sigWidgetObjNum);

        // ── 5. Catalog — only if a new AcroForm was created ──────────────────
        if (isNewAcroForm)
        {
            offsets.Add((catalogObjNum, ms.Position));
            WriteUpdatedCatalog(w, catalogObjNum, catalogDict, acroFormObjNum);
        }

        // ── 6. Appearance XObjects (Form XObject + optional Image XObject) ────
        if (appearance != null && formXObjNum.HasValue)
        {
            int rotation  = request.Appearance?.PageRotation ?? 0;
            string matrix = Appearance.DefaultPdfSignatureAppearanceBuilder
                .GetRotationMatrix(rotation, appearance.Width, appearance.Height);

            offsets.Add((formXObjNum.Value, ms.Position));
            w.WriteFormXObject(
                formXObjNum.Value, appearance.Width, appearance.Height,
                matrix, appearance.HasImage, imageXObjNum ?? 0, appearance.ContentStream);

            if (appearance.HasImage && imageXObjNum.HasValue)
            {
                offsets.Add((imageXObjNum.Value, ms.Position));
                w.WriteJpegImageXObject(
                    imageXObjNum.Value, appearance.ImagePixelWidth,
                    appearance.ImagePixelHeight, appearance.ImageXObjectData!);
            }
        }

        // ── 7. Incremental cross-reference table and trailer ─────────────────
        long xrefOffset = ms.Position;
        WriteIncrementalXrefAndTrailer(ms, offsets, prevSize, nextNum,
            catalogObjNum, prevStartxref, xrefOffset);

        return (ms.ToArray(), state);
    }

    // -----------------------------------------------------------------------
    // Updated-object writers
    // -----------------------------------------------------------------------

    private static void WriteAcroForm(
        PdfLiteWriter w, int objNum, string? existingDict, int newWidgetObjNum)
    {
        string fields;
        if (existingDict != null)
        {
            string existing = ExtractArrayContent(existingDict, "/Fields") ?? "";
            string sep      = existing.Length > 0 ? " " : "";
            fields = existing.TrimEnd() + sep + $"{newWidgetObjNum} 0 R";
        }
        else
        {
            fields = $"{newWidgetObjNum} 0 R";
        }

        w.WriteRawObject(objNum,
            $"<< /Fields [{fields}] /SigFlags 3 /DA (/Helv 12 Tf 0 g) >>");
    }

    private static void WriteUpdatedPage(
        PdfLiteWriter w, int objNum, string existingPageDict, int widgetObjNum)
    {
        // Add the new signature widget into /Annots (creating the array if absent).
        string existing = ExtractArrayContent(existingPageDict, "/Annots") ?? "";
        string sep      = existing.Length > 0 ? " " : "";
        string annots   = existing.TrimEnd() + sep + $"{widgetObjNum} 0 R";

        string updated = InjectOrReplaceArrayKey(existingPageDict, "/Annots", annots);
        w.WriteRawObject(objNum, updated);
    }

    private static void WriteUpdatedCatalog(
        PdfLiteWriter w, int objNum, string existingCatalogDict, int acroFormObjNum)
    {
        int lastClose = existingCatalogDict.LastIndexOf(">>", StringComparison.Ordinal);
        string updated = existingCatalogDict.Substring(0, lastClose)
                       + $"/AcroForm {acroFormObjNum} 0 R >>";
        w.WriteRawObject(objNum, updated);
    }

    // -----------------------------------------------------------------------
    // Incremental cross-reference + trailer
    // -----------------------------------------------------------------------

    private static void WriteIncrementalXrefAndTrailer(
        Stream ms,
        List<(int ObjNum, long Offset)> objOffsets,
        int prevSize,
        int nextObjNum,
        int catalogObjNum,
        long prevStartxref,
        long xrefOffset)
    {
        var sorted = new List<(int ObjNum, long Offset)>(objOffsets);
        sorted.Sort((a, b) => a.ObjNum.CompareTo(b.ObjNum));

        var sb = new StringBuilder();
        sb.Append("xref\n");

        // Emit one subsection per contiguous run of object numbers.
        int i = 0;
        while (i < sorted.Count)
        {
            int runStart = sorted[i].ObjNum;
            int j        = i;
            while (j + 1 < sorted.Count && sorted[j + 1].ObjNum == sorted[j].ObjNum + 1)
                j++;
            int runCount = j - i + 1;

            sb.Append($"{runStart} {runCount}\n");
            for (int k = i; k <= j; k++)
                sb.Append(sorted[k].Offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");

            i = j + 1;
        }

        int newSize = Math.Max(prevSize, nextObjNum);
        sb.Append($"trailer\n<< /Size {newSize} /Root {catalogObjNum} 0 R /Prev {prevStartxref} >>\n");
        sb.Append($"startxref\n{xrefOffset}\n%%EOF\n");

        byte[] bytes = Latin1.GetBytes(sb.ToString());
        ms.Write(bytes, 0, bytes.Length);
    }

    // -----------------------------------------------------------------------
    // Dictionary manipulation helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Replaces /Key [existing content] with /Key [newContent] inside a dict string,
    /// or injects /Key [newContent] before the closing >> if the key is absent.
    /// </summary>
    private static string InjectOrReplaceArrayKey(string dictText, string key, string newContent)
    {
        // Look for "/Key [" with optional whitespace between key and bracket
        int keyPos = dictText.IndexOf(key, StringComparison.Ordinal);
        if (keyPos >= 0)
        {
            int brk = dictText.IndexOf('[', keyPos + key.Length);
            // Only treat as the key's array if the '[' is within a few chars (no other key in between)
            if (brk >= 0 && brk <= keyPos + key.Length + 5)
            {
                int close = dictText.IndexOf(']', brk + 1);
                if (close >= 0)
                {
                    return dictText.Substring(0, brk + 1)
                         + newContent
                         + dictText.Substring(close);
                }
            }
        }

        // Key not present: insert before the final >>
        int lastClose = dictText.LastIndexOf(">>", StringComparison.Ordinal);
        return dictText.Substring(0, lastClose)
             + $"\n   {key} [{newContent}] >>";
    }

    // -----------------------------------------------------------------------
    // PDF structural parsing helpers
    // -----------------------------------------------------------------------

    private static (long PrevStartxref, int PrevSize, int CatalogObjNum) ParseTrailer(string pdfText)
    {
        const string sxMarker = "startxref\n";
        const string sizeKey  = "/Size ";
        const string rootKey  = "/Root ";

        int sxPos = pdfText.LastIndexOf(sxMarker, StringComparison.Ordinal);
        if (sxPos < 0) throw new InvalidOperationException("'startxref' not found in PDF.");

        int numStart = sxPos + sxMarker.Length;
        int numEnd   = pdfText.IndexOf('\n', numStart);
        long prevStartxref = long.Parse(
            pdfText.Substring(numStart, numEnd - numStart).Trim(),
            CultureInfo.InvariantCulture);

        int trailerPos = pdfText.LastIndexOf("trailer", StringComparison.Ordinal);
        if (trailerPos < 0) throw new InvalidOperationException("'trailer' not found in PDF.");

        int sizeKeyPos = pdfText.IndexOf(sizeKey, trailerPos, StringComparison.Ordinal);
        if (sizeKeyPos < 0) throw new InvalidOperationException("'/Size' not found in PDF trailer.");
        int sizeStart = sizeKeyPos + sizeKey.Length;
        int sizeEnd   = sizeStart;
        while (sizeEnd < pdfText.Length && char.IsDigit(pdfText[sizeEnd])) sizeEnd++;
        int prevSize = int.Parse(
            pdfText.Substring(sizeStart, sizeEnd - sizeStart), CultureInfo.InvariantCulture);

        int rootKeyPos = pdfText.IndexOf(rootKey, trailerPos, StringComparison.Ordinal);
        if (rootKeyPos < 0) throw new InvalidOperationException("'/Root' not found in PDF trailer.");
        int rootStart = rootKeyPos + rootKey.Length;
        int rootEnd   = rootStart;
        while (rootEnd < pdfText.Length && char.IsDigit(pdfText[rootEnd])) rootEnd++;
        int catalogObjNum = int.Parse(
            pdfText.Substring(rootStart, rootEnd - rootStart), CultureInfo.InvariantCulture);

        return (prevStartxref, prevSize, catalogObjNum);
    }

    private static string ExtractLastObjectDict(string pdfText, int objNum)
    {
        // Search for "N 0 obj" preceded by a non-digit (to avoid matching "10 0 obj" when N=0).
        // Accept any whitespace after "obj" before the dictionary.
        string token  = $"{objNum} 0 obj";
        int objPos    = -1;
        int searchFrom = 0;
        while (true)
        {
            int idx = pdfText.IndexOf(token, searchFrom, StringComparison.Ordinal);
            if (idx < 0) break;
            // Ensure the character immediately before the object number is not a digit
            bool precedingOk = idx == 0 || !char.IsDigit(pdfText[idx - 1]);
            if (precedingOk) { objPos = idx; } // keep last match
            searchFrom = idx + token.Length;
        }
        if (objPos < 0)
            throw new InvalidOperationException($"Object {objNum} not found in PDF.");

        int dictStart = pdfText.IndexOf("<<", objPos + token.Length, StringComparison.Ordinal);
        if (dictStart < 0)
            throw new InvalidOperationException($"No dictionary found in object {objNum}.");

        int depth = 0;
        int pos   = dictStart;
        while (pos < pdfText.Length - 1)
        {
            char c = pdfText[pos];
            if (c == '(')
            {
                pos++;
                int pd = 1;
                while (pos < pdfText.Length && pd > 0)
                {
                    if (pdfText[pos] == '\\') { pos += 2; continue; }
                    if (pdfText[pos] == '(')  pd++;
                    else if (pdfText[pos] == ')') pd--;
                    pos++;
                }
                continue;
            }
            if (c == '<' && pdfText[pos + 1] == '<')
            {
                depth++; pos += 2;
            }
            else if (c == '<')
            {
                pos = pdfText.IndexOf('>', pos + 1) + 1;
                if (pos == 0)
                    throw new InvalidOperationException($"Unterminated hex string in object {objNum}.");
            }
            else if (c == '>' && pdfText[pos + 1] == '>')
            {
                depth--; pos += 2;
                if (depth == 0) return pdfText.Substring(dictStart, pos - dictStart);
            }
            else
            {
                pos++;
            }
        }
        throw new InvalidOperationException($"Unbalanced dictionary in object {objNum}.");
    }

    /// <summary>Finds /Key N 0 R in a dictionary string and returns N, or <c>null</c>.</summary>
    private static int? ParseRef(string dictText, string key)
    {
        int pos = dictText.IndexOf(key, StringComparison.Ordinal);
        if (pos < 0) return null;

        // Skip any whitespace after the key name
        int numStart = pos + key.Length;
        while (numStart < dictText.Length && IsWhitespace(dictText[numStart])) numStart++;

        int numEnd = numStart;
        while (numEnd < dictText.Length && char.IsDigit(dictText[numEnd])) numEnd++;
        if (numEnd == numStart) return null;

        return int.TryParse(
            dictText.Substring(numStart, numEnd - numStart),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : (int?)null;
    }

    /// <summary>Returns the first object number from /Kids [N ...] in a /Pages dictionary.</summary>
    private static int? ParseFirstKid(string dictText)
    {
        int pos = dictText.IndexOf("/Kids", StringComparison.Ordinal);
        if (pos < 0) return null;

        // Find opening bracket, allowing whitespace between "/Kids" and "["
        int bracketPos = pos + 5;
        while (bracketPos < dictText.Length && IsWhitespace(dictText[bracketPos])) bracketPos++;
        if (bracketPos >= dictText.Length || dictText[bracketPos] != '[') return null;

        int numStart = bracketPos + 1;
        while (numStart < dictText.Length && IsWhitespace(dictText[numStart])) numStart++;
        int numEnd = numStart;
        while (numEnd < dictText.Length && char.IsDigit(dictText[numEnd])) numEnd++;
        if (numEnd == numStart) return null;

        return int.TryParse(
            dictText.Substring(numStart, numEnd - numStart),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : (int?)null;
    }

    private static bool IsWhitespace(char c) => c == ' ' || c == '\n' || c == '\t' || c == '\r';

    /// <summary>Returns the content inside /Key [...] from a dictionary string, or <c>null</c>.</summary>
    private static string? ExtractArrayContent(string dictText, string key)
    {
        // Try "/Key [" (space before bracket)
        string search = key + " [";
        int pos = dictText.IndexOf(search, StringComparison.Ordinal);
        int openBracket;
        if (pos >= 0)
        {
            openBracket = pos + search.Length - 1;
        }
        else
        {
            // Also try "/Key\n[" or similar whitespace variations
            pos = dictText.IndexOf(key, StringComparison.Ordinal);
            if (pos < 0) return null;
            openBracket = dictText.IndexOf('[', pos + key.Length);
            if (openBracket < 0 || openBracket > pos + key.Length + 5) return null;
        }

        int closeBracket = dictText.IndexOf(']', openBracket + 1);
        if (closeBracket < 0) return null;
        return dictText.Substring(openBracket + 1, closeBracket - openBracket - 1);
    }
}
