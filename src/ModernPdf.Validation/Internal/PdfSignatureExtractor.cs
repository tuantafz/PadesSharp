// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using iTextSharp.text.pdf;

namespace ModernPdf.Validation.Internal
{
    /// <summary>Internal model for one extracted PDF signature.</summary>
    internal sealed class ExtractedSignature
    {
        public string   FieldName     { get; set; } = "Signature";
        public long[]   ByteRange     { get; set; } = Array.Empty<long>();
        public byte[]   ContentsBytes { get; set; } = Array.Empty<byte>();
        public string   SubFilter     { get; set; } = "adbe.pkcs7.detached";
    }

    /// <summary>
    /// Extracts signed AcroForm fields through PdfReader's PDF object model.
    /// A raw scanner is used only when damaged signed bytes prevent parsing.
    ///
    /// The following limitations apply only to the damaged-document recovery scanner:
    ///   - Does not traverse xref streams or cross-reference tables; relies on byte-order search.
    ///   - Signature dicts in ObjStm (object streams) are not supported — this is technically
    ///     invalid per PDF spec (ISO 32000 §7.5.4 restricts stream objects in ObjStm) and does
    ///     not occur in practice with conforming PDF signers.
    ///   - /Contents can be a direct hex string &lt;...&gt; or an indirect reference "N 0 R"
    ///     (both are resolved).
    ///
    /// Line endings are normalised to LF for text parsing; /ByteRange values are read as
    /// literal integers so they always reference the original (un-normalised) file bytes.
    /// </summary>
    internal static class PdfSignatureExtractor
    {
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        // PDF whitespace chars (ISO 32000-1 Table 1)
        private static readonly char[] PdfWs = { '\0', '\t', '\n', '\f', '\r', ' ' };

        public static IReadOnlyList<ExtractedSignature> Extract(byte[] pdfBytes)
        {
            if (pdfBytes == null || pdfBytes.Length == 0)
                return Array.Empty<ExtractedSignature>();

            var results = new List<ExtractedSignature>();
            PdfReader? reader = null;
            try
            {
                reader = new PdfReader(pdfBytes);
                AcroFields fields = reader.AcroFields;
                foreach (object item in fields.GetSignatureNames())
                {
                    string fieldName = item as string ?? item.ToString() ?? "Signature";
                    PdfDictionary dictionary = fields.GetSignatureDictionary(fieldName);
                    if (dictionary == null) continue;

                    PdfArray rangeArray = dictionary.GetAsArray(PdfName.BYTERANGE);
                    PdfString contentsString = dictionary.GetAsString(PdfName.CONTENTS);
                    if (rangeArray == null || rangeArray.Size != 4 || contentsString == null)
                        continue;

                    var byteRange = new long[4];
                    bool rangeValid = true;
                    for (int i = 0; i < byteRange.Length; i++)
                    {
                        PdfNumber number = rangeArray.GetAsNumber(i);
                        if (number == null || !long.TryParse(number.ToString(), NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out byteRange[i]))
                        {
                            rangeValid = false;
                            break;
                        }
                    }
                    if (!rangeValid) continue;

                    byte[]? contents = TrimDerPadding(contentsString.GetOriginalBytes());
                    if (contents == null || contents.Length == 0) continue;

                    PdfName subFilterName = dictionary.GetAsName(PdfName.SUBFILTER);
                    string subFilter = subFilterName == null
                        ? "adbe.pkcs7.detached"
                        : PdfName.DecodeName(subFilterName.ToString()).TrimStart('/');

                    results.Add(new ExtractedSignature
                    {
                        FieldName = fieldName,
                        ByteRange = byteRange,
                        ContentsBytes = contents,
                        SubFilter = subFilter,
                    });
                }
            }
            catch
            {
                // A signed byte may have been corrupted so severely that PdfReader cannot
                // open the document (for example a modified PDF header).  Recover the raw
                // signature dictionary in that case so CMS/ByteRange validation can report
                // the tampering instead of silently returning no signature results.
                return ExtractByTextScan(pdfBytes);
            }
            finally
            {
                reader?.Close();
            }

            return results;
        }

        private static IReadOnlyList<ExtractedSignature> ExtractByTextScan(byte[] pdfBytes)
        {
            string pdfText = NormaliseLineEndings(Latin1.GetString(pdfBytes));
            var results = new List<ExtractedSignature>();
            int searchFrom = 0;
            while (true)
            {
                int sigPos = FindTypeSig(pdfText, searchFrom);
                if (sigPos < 0) break;
                searchFrom = sigPos + 1;
                int dictStart = pdfText.LastIndexOf("<<", sigPos, StringComparison.Ordinal);
                if (dictStart < 0) continue;
                string? dictText = ExtractDict(pdfText, dictStart);
                if (dictText == null) continue;
                long[]? byteRange = ParseByteRange(dictText);
                byte[]? contents = ParseContents(dictText, pdfText);
                if (byteRange == null || contents == null || contents.Length == 0) continue;
                int objNum = FindObjectNumber(pdfText, dictStart);
                results.Add(new ExtractedSignature
                {
                    FieldName = objNum > 0 ? FindFieldName(pdfText, objNum) : "Signature",
                    ByteRange = byteRange,
                    ContentsBytes = contents,
                    SubFilter = ParseName(dictText, "/SubFilter") ?? "adbe.pkcs7.detached",
                });
            }
            return results;
        }

        private static byte[]? TrimDerPadding(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 2 || bytes[0] != 0x30) return null;
            int headerLength;
            int contentLength;
            byte firstLength = bytes[1];
            if (firstLength < 0x80)
            {
                headerLength = 2;
                contentLength = firstLength;
            }
            else
            {
                int lengthBytes = firstLength & 0x7F;
                if (lengthBytes == 0 || lengthBytes > 4 || bytes.Length < 2 + lengthBytes)
                    return null;
                headerLength = 2 + lengthBytes;
                contentLength = 0;
                for (int i = 0; i < lengthBytes; i++)
                    contentLength = checked((contentLength << 8) | bytes[2 + i]);
            }

            int totalLength = checked(headerLength + contentLength);
            if (totalLength > bytes.Length) return null;
            var result = new byte[totalLength];
            Array.Copy(bytes, result, totalLength);
            return result;
        }

        // -----------------------------------------------------------------------
        // Normalisation
        // -----------------------------------------------------------------------

        private static string NormaliseLineEndings(string s)
        {
            // Replace CRLF first (otherwise the CR gets doubled-replaced),
            // then bare CR → LF.
            return s.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        // -----------------------------------------------------------------------
        // /Type /Sig token search (whitespace-tolerant)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Finds the next occurrence of "/Type" followed by optional PDF whitespace
        /// and then "/Sig" with a PDF delimiter or whitespace immediately after.
        /// Returns the position of the '/' in "/Type", or -1 if not found.
        /// </summary>
        private static int FindTypeSig(string text, int from)
        {
            const string typeKey = "/Type";
            int pos = from;
            while (pos < text.Length)
            {
                int typePos = text.IndexOf(typeKey, pos, StringComparison.Ordinal);
                if (typePos < 0) return -1;

                int i = typePos + typeKey.Length;
                // Skip PDF whitespace
                while (i < text.Length && IsPdfWhitespace(text[i])) i++;

                if (i + 4 <= text.Length &&
                    text[i] == '/' &&
                    text[i + 1] == 'S' && text[i + 2] == 'i' && text[i + 3] == 'g')
                {
                    // Ensure "Sig" is followed by a delimiter or whitespace (not "SigQ" etc.)
                    int end = i + 4;
                    if (end >= text.Length || IsPdfDelimiter(text[end]) || IsPdfWhitespace(text[end]))
                        return typePos;
                }

                pos = typePos + 1;
            }
            return -1;
        }

        private static bool IsPdfWhitespace(char c) =>
            c == '\0' || c == '\t' || c == '\n' || c == '\f' || c == '\r' || c == ' ';

        private static bool IsPdfDelimiter(char c) =>
            c == '(' || c == ')' || c == '<' || c == '>' ||
            c == '[' || c == ']' || c == '{' || c == '}' || c == '/' || c == '%';

        // -----------------------------------------------------------------------
        // Dictionary extraction
        // -----------------------------------------------------------------------

        /// <summary>
        /// Extracts a balanced &lt;&lt;...&gt;&gt; dictionary starting at <paramref name="start"/>.
        /// Skips PDF literal strings and hex strings so their content cannot be
        /// mistaken for dictionary delimiters.
        /// </summary>
        private static string? ExtractDict(string pdfText, int start)
        {
            int depth = 0;
            int pos   = start;
            while (pos < pdfText.Length - 1)
            {
                char c = pdfText[pos];

                if (c == '(')
                {
                    // PDF literal string — skip balanced parentheses.
                    pos++;
                    int pd = 1;
                    while (pos < pdfText.Length && pd > 0)
                    {
                        if (pdfText[pos] == '\\') { pos += 2; continue; }
                        if      (pdfText[pos] == '(') pd++;
                        else if (pdfText[pos] == ')') pd--;
                        pos++;
                    }
                    continue;
                }

                if (c == '<' && pdfText[pos + 1] == '<')
                {
                    depth++;
                    pos += 2;
                }
                else if (c == '<')
                {
                    // Hex string — skip to closing '>'.
                    int closePos = pdfText.IndexOf('>', pos + 1);
                    if (closePos < 0) return null;
                    pos = closePos + 1;
                }
                else if (c == '>' && pdfText[pos + 1] == '>')
                {
                    depth--;
                    pos += 2;
                    if (depth == 0) return pdfText.Substring(start, pos - start);
                }
                else
                {
                    pos++;
                }
            }
            return null;
        }

        // -----------------------------------------------------------------------
        // /ByteRange parsing
        // -----------------------------------------------------------------------

        private static long[]? ParseByteRange(string dictText)
        {
            // Search for /ByteRange and then skip to '['.
            int keyPos = dictText.IndexOf("/ByteRange", StringComparison.Ordinal);
            if (keyPos < 0) return null;

            int open = keyPos + "/ByteRange".Length;
            // Skip whitespace before '['
            while (open < dictText.Length && IsPdfWhitespace(dictText[open])) open++;
            if (open >= dictText.Length || dictText[open] != '[') return null;

            int close = dictText.IndexOf(']', open + 1);
            if (close < 0) return null;

            string inner = dictText.Substring(open + 1, close - open - 1);
            var parts = inner.Split(new[] { ' ', '\t', '\r', '\n', '\f' },
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 4) return null;

            var result = new long[4];
            for (int i = 0; i < 4; i++)
            {
                if (!long.TryParse(parts[i], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out result[i]))
                    return null;
            }
            return result;
        }

        // -----------------------------------------------------------------------
        // /Contents parsing
        // -----------------------------------------------------------------------

        /// <summary>
        /// Locates /Contents in <paramref name="dictText"/> and returns the actual
        /// DER-encoded CMS bytes.  Handles two forms:
        /// <list type="bullet">
        ///   <item>Direct hex string: <c>/Contents &lt;HEXHEX...&gt;</c></item>
        ///   <item>Indirect reference: <c>/Contents N 0 R</c> — resolves object N in
        ///         <paramref name="fullPdfText"/>.</item>
        /// </list>
        /// The zero-padding that PDF generators reserve around /Contents is stripped
        /// using the embedded DER length.
        /// </summary>
        private static byte[]? ParseContents(string dictText, string fullPdfText)
        {
            int keyPos = dictText.IndexOf("/Contents", StringComparison.Ordinal);
            if (keyPos < 0) return null;

            int i = keyPos + "/Contents".Length;
            while (i < dictText.Length && IsPdfWhitespace(dictText[i])) i++;
            if (i >= dictText.Length) return null;

            // ── Direct hex string: /Contents <HEX...> ──────────────────────
            if (dictText[i] == '<')
            {
                // Guard against '<<' (nested dict is not a hex string).
                if (i + 1 < dictText.Length && dictText[i + 1] == '<') return null;
                int hexEnd = dictText.IndexOf('>', i + 1);
                if (hexEnd < 0) return null;
                return DecodeContentsHex(dictText.Substring(i + 1, hexEnd - i - 1));
            }

            // ── Indirect reference: /Contents N 0 R ────────────────────────
            if (char.IsDigit(dictText[i]))
            {
                int numStart = i;
                while (i < dictText.Length && char.IsDigit(dictText[i])) i++;
                if (!int.TryParse(dictText.Substring(numStart, i - numStart),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out int objNum))
                    return null;
                // Must be followed by "0 R" (generation 0).
                while (i < dictText.Length && IsPdfWhitespace(dictText[i])) i++;
                if (i >= dictText.Length || dictText[i] != '0') return null;
                i++;
                while (i < dictText.Length && IsPdfWhitespace(dictText[i])) i++;
                if (i >= dictText.Length || dictText[i] != 'R') return null;
                return ResolveContentsObject(fullPdfText, objNum);
            }

            return null;
        }

        /// <summary>
        /// Looks up indirect object <paramref name="objNum"/> in the full (normalised)
        /// PDF text and extracts a hex-string value from its body.
        /// </summary>
        private static byte[]? ResolveContentsObject(string pdfText, int objNum)
        {
            string header = $"{objNum} 0 obj";
            // Use the last definition (most-recent revision wins).
            int objPos = pdfText.LastIndexOf(header, StringComparison.Ordinal);
            if (objPos < 0) return null;

            int endobjPos = pdfText.IndexOf("endobj", objPos + header.Length, StringComparison.Ordinal);
            if (endobjPos < 0) return null;

            int hexOpen = pdfText.IndexOf('<', objPos + header.Length);
            if (hexOpen < 0 || hexOpen >= endobjPos) return null;
            if (hexOpen + 1 < pdfText.Length && pdfText[hexOpen + 1] == '<') return null;

            int hexClose = pdfText.IndexOf('>', hexOpen + 1);
            if (hexClose < 0 || hexClose >= endobjPos) return null;

            return DecodeContentsHex(pdfText.Substring(hexOpen + 1, hexClose - hexOpen - 1));
        }

        /// <summary>
        /// Decodes a /Contents hex string, stripping the zero-padding PDF generators
        /// add around the reserved /Contents slot.  The actual length is read from the
        /// embedded DER SEQUENCE header.
        /// </summary>
        private static byte[]? DecodeContentsHex(string rawHex)
        {
            string hex = rawHex.Trim();
            if (hex.Length < 4) return null;
            int derLen = ParseDerTotalLength(hex);
            if (derLen <= 0 || derLen * 2 > hex.Length) return null;
            var result = new byte[derLen];
            for (int j = 0; j < derLen; j++)
                result[j] = Convert.ToByte(hex.Substring(j * 2, 2), 16);
            return result;
        }

        private static int ParseDerTotalLength(string hex)
        {
            if (hex.Length < 4) return -1;
            // hex[0:2] = tag (expect 0x30 = SEQUENCE), hex[2:4] = first length byte
            byte firstLen = Convert.ToByte(hex.Substring(2, 2), 16);
            if (firstLen < 0x80) return 1 + 1 + firstLen;
            if (firstLen == 0x81 && hex.Length >= 6)
                return 1 + 2 + Convert.ToByte(hex.Substring(4, 2), 16);
            if (firstLen == 0x82 && hex.Length >= 8)
                return 1 + 3 + ((Convert.ToByte(hex.Substring(4, 2), 16) << 8)
                               |  Convert.ToByte(hex.Substring(6, 2), 16));
            if (firstLen == 0x83 && hex.Length >= 10)
                return 1 + 4 + ((Convert.ToByte(hex.Substring(4, 2), 16) << 16)
                               | (Convert.ToByte(hex.Substring(6, 2), 16) << 8)
                               |  Convert.ToByte(hex.Substring(8, 2), 16));
            return -1;
        }

        // -----------------------------------------------------------------------
        // PDF name parsing (/Key /Value)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns the name token value after <paramref name="key"/>, e.g.
        /// ParseName(dict, "/SubFilter") → "adbe.pkcs7.detached".
        /// Handles any amount of PDF whitespace between key and value.
        /// </summary>
        private static string? ParseName(string dictText, string key)
        {
            int pos = dictText.IndexOf(key, StringComparison.Ordinal);
            if (pos < 0) return null;

            int i = pos + key.Length;
            // Skip PDF whitespace
            while (i < dictText.Length && IsPdfWhitespace(dictText[i])) i++;

            if (i >= dictText.Length || dictText[i] != '/') return null;
            int nameStart = i + 1;
            int nameEnd   = nameStart;
            while (nameEnd < dictText.Length &&
                   !IsPdfWhitespace(dictText[nameEnd]) &&
                   !IsPdfDelimiter(dictText[nameEnd]))
                nameEnd++;
            return dictText.Substring(nameStart, nameEnd - nameStart);
        }

        // -----------------------------------------------------------------------
        // Object number / field name
        // -----------------------------------------------------------------------

        /// <summary>
        /// Walks backwards from <paramref name="dictStart"/> looking for
        /// "N 0 obj" (with any whitespace) and returns N.
        /// Scan window is 200 characters to accommodate large comments/whitespace.
        /// </summary>
        private static int FindObjectNumber(string pdfText, int dictStart)
        {
            int scanLen  = Math.Min(200, dictStart);
            int scanFrom = dictStart - scanLen;
            string before = pdfText.Substring(scanFrom, scanLen);

            // Accept "N 0 obj" followed by whitespace or end of string.
            // We look for " 0 obj" suffix.
            int objSuffix = before.LastIndexOf(" 0 obj", StringComparison.Ordinal);
            if (objSuffix < 0) return -1;

            // The character right after " 0 obj" (if any) must be whitespace.
            int afterObj = scanFrom + objSuffix + " 0 obj".Length;
            if (afterObj < pdfText.Length && !IsPdfWhitespace(pdfText[afterObj]) &&
                !IsPdfDelimiter(pdfText[afterObj]))
                return -1;

            int numEnd   = scanFrom + objSuffix;
            int numStart = numEnd - 1;
            while (numStart > 0 && char.IsDigit(pdfText[numStart - 1]))
                numStart--;
            if (numStart >= numEnd) return -1;

            return int.TryParse(pdfText.Substring(numStart, numEnd - numStart),
                                NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                ? n : -1;
        }

        /// <summary>
        /// Finds the AcroForm widget that references this signature object via
        /// /V N 0 R and returns its /T name. Falls back to "Signature".
        /// </summary>
        private static string FindFieldName(string pdfText, int sigValueObjNum)
        {
            string marker = $"/V {sigValueObjNum} 0 R";
            int pos = pdfText.IndexOf(marker, StringComparison.Ordinal);
            if (pos < 0) return "Signature";

            int dictStart = pdfText.LastIndexOf("<<", pos, StringComparison.Ordinal);
            if (dictStart < 0) return "Signature";

            string widgetDict = ExtractDict(pdfText, dictStart) ?? string.Empty;

            // Search for /T followed (after optional PDF whitespace) by '(' — must not
            // match /Type, /TU, /TM etc.  Loop so we skip over non-/T-literal matches.
            int searchFrom = 0;
            while (true)
            {
                int tPos = widgetDict.IndexOf("/T", searchFrom, StringComparison.Ordinal);
                if (tPos < 0) return "Signature";

                int i = tPos + 2; // skip "/T"

                // The char right after "T" must be whitespace or '(' — not a letter/digit
                // (which would mean we matched /Type, /TU, etc.).
                if (i < widgetDict.Length && (char.IsLetterOrDigit(widgetDict[i]) || widgetDict[i] == '_'))
                {
                    searchFrom = tPos + 1;
                    continue;
                }

                // Skip whitespace
                while (i < widgetDict.Length && IsPdfWhitespace(widgetDict[i])) i++;

                if (i >= widgetDict.Length)
                    return "Signature";

                if (widgetDict[i] == '(')
                {
                    int nameStart = i + 1;
                    int nameEnd   = nameStart;
                    while (nameEnd < widgetDict.Length)
                    {
                        if (widgetDict[nameEnd] == '\\') { nameEnd += 2; continue; }
                        if (widgetDict[nameEnd] == ')') break;
                        nameEnd++;
                    }
                    return widgetDict.Substring(nameStart, nameEnd - nameStart);
                }

                searchFrom = tPos + 1;
            }
        }
    }
}
