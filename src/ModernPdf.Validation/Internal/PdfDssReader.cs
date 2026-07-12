// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Asn1.Ocsp;
using Org.BouncyCastle.Ocsp;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.X509.Extension;
using LegacyPdf = iTextSharp.text.pdf;

namespace ModernPdf.Validation.Internal
{
    /// <summary>
    /// The parsed contents of a PDF Document Security Store (/DSS + /VRI).
    /// All collections may be empty; null is never returned for a collection.
    /// </summary>
    internal sealed class DssData
    {
        /// <summary>Certificates from the global /Certs pool.</summary>
        public IReadOnlyList<X509Certificate> Certs { get; set; } = Array.Empty<X509Certificate>();

        /// <summary>
        /// OCSP BasicOCSPResp objects from the global /OCSPs pool.
        /// Already verified for internal signature integrity; revocation decisions
        /// are made by <see cref="DssRevocationChecker"/>.
        /// </summary>
        public IReadOnlyList<BasicOcspResp> OcspResponses { get; set; } = Array.Empty<BasicOcspResp>();

        /// <summary>CRL objects from the global /CRLs pool.</summary>
        public IReadOnlyList<X509Crl> Crls { get; set; } = Array.Empty<X509Crl>();

        /// <summary>Per-signature VRI entries keyed by upper-case hex SHA-1 of the signature bytes.</summary>
        public IReadOnlyDictionary<string, VriEntry> Vri { get; set; } =
            new Dictionary<string, VriEntry>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class VriEntry
    {
        public IReadOnlyList<X509Certificate> Certs { get; set; } = Array.Empty<X509Certificate>();
        public IReadOnlyList<BasicOcspResp>   Ocsps { get; set; } = Array.Empty<BasicOcspResp>();
        public IReadOnlyList<X509Crl>         Crls  { get; set; } = Array.Empty<X509Crl>();
    }

    // -------------------------------------------------------------------------
    // Structured DSS reader (with recovery scanner for damaged xref data)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves the catalog /DSS dictionary through PdfReader's object model.
    /// Returns an empty <see cref="DssData"/> when no /DSS is present.
    /// </summary>
    internal static class PdfDssReader
    {
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        private static readonly LegacyPdf.PdfName DssName   = new LegacyPdf.PdfName("DSS");
        private static readonly LegacyPdf.PdfName VriName   = new LegacyPdf.PdfName("VRI");
        private static readonly LegacyPdf.PdfName CertsName = new LegacyPdf.PdfName("Certs");
        private static readonly LegacyPdf.PdfName OcspsName = new LegacyPdf.PdfName("OCSPs");
        private static readonly LegacyPdf.PdfName CrlsName  = new LegacyPdf.PdfName("CRLs");
        private static readonly LegacyPdf.PdfName CertName  = new LegacyPdf.PdfName("Cert");
        private static readonly LegacyPdf.PdfName OcspName  = new LegacyPdf.PdfName("OCSP");
        private static readonly LegacyPdf.PdfName CrlName   = new LegacyPdf.PdfName("CRL");

        public static DssData Read(byte[] pdfBytes)
        {
            if (pdfBytes == null || pdfBytes.Length == 0) return new DssData();

            LegacyPdf.PdfReader? reader = null;
            try
            {
                reader = new LegacyPdf.PdfReader(pdfBytes);
                LegacyPdf.PdfDictionary dss = reader.Catalog.GetAsDict(DssName);
                if (dss == null) return new DssData();

                var certs = ReadArray(dss.GetAsArray(CertsName), ParseCert);
                var ocsps = ReadArray(dss.GetAsArray(OcspsName), ParseOcsp);
                var crls  = ReadArray(dss.GetAsArray(CrlsName), ParseCrl);
                var vri   = new Dictionary<string, VriEntry>(StringComparer.OrdinalIgnoreCase);

                LegacyPdf.PdfDictionary vriDictionary = dss.GetAsDict(VriName);
                if (vriDictionary != null)
                {
                    foreach (object keyObject in vriDictionary.Keys)
                    {
                        if (!(keyObject is LegacyPdf.PdfName key)) continue;
                        string keyText = LegacyPdf.PdfName.DecodeName(key.ToString()).TrimStart('/');
                        if (keyText.Length != 40 || !IsHex(keyText)) continue;
                        LegacyPdf.PdfDictionary entry = vriDictionary.GetAsDict(key);
                        if (entry == null) continue;
                        vri[keyText.ToUpperInvariant()] = new VriEntry
                        {
                            Certs = ReadArray(entry.GetAsArray(CertName), ParseCert),
                            Ocsps = ReadArray(entry.GetAsArray(OcspName), ParseOcsp),
                            Crls  = ReadArray(entry.GetAsArray(CrlName), ParseCrl),
                        };
                    }
                }

                var result = new DssData
                {
                    Certs = certs,
                    OcspResponses = ocsps,
                    Crls = crls,
                    Vri = vri,
                };
                // Recovery for damaged/non-conforming xref data: a few producers leave
                // otherwise readable DSS objects outside the effective xref.  Normal PDFs
                // always use the structured result above; scanning is only a last resort.
                if (result.Certs.Count == 0 && result.OcspResponses.Count == 0 &&
                    result.Crls.Count == 0 && result.Vri.Count == 0)
                    return ReadTextScan(pdfBytes);
                return result;
            }
            catch
            {
                return new DssData();
            }
            finally
            {
                reader?.Close();
            }
        }

        private static IReadOnlyList<T> ReadArray<T>(
            LegacyPdf.PdfArray? array, Func<byte[], T?> parser) where T : class
        {
            var result = new List<T>();
            if (array == null) return result;
            for (int i = 0; i < array.Size; i++)
            {
                LegacyPdf.PdfObject value = array.GetDirectObject(i);
                byte[]? bytes = ReadObjectBytes(value);
                if (bytes == null) continue;
                T? parsed = parser(bytes);
                if (parsed != null) result.Add(parsed);
            }
            return result;
        }

        private static byte[]? ReadObjectBytes(LegacyPdf.PdfObject value)
        {
            if (value is LegacyPdf.PRStream stream)
                return LegacyPdf.PdfReader.GetStreamBytes(stream);
            if (value is LegacyPdf.PdfString str)
                return str.GetBytes();
            return null;
        }

        // Retained only as implementation history; normal validation uses PdfReader above.
        private static DssData ReadTextScan(byte[] pdfBytes)
        {
            if (pdfBytes == null || pdfBytes.Length == 0)
                return new DssData();

            // Do NOT normalise line endings here — binary stream content (DER-encoded
            // certificates, OCSP responses, CRLs) would be corrupted if 0x0D 0x0A byte
            // pairs inside DER values were collapsed to 0x0A.  Structural searches below
            // handle both CRLF and LF explicitly.
            string text = Latin1.GetString(pdfBytes);

            // Locate the /DSS dictionary.  In a conforming PDF it appears once in a
            // catalog dictionary as "/DSS N 0 R" (indirect reference) or inline.
            // We search for the most-recently written /DSS indirect object since PDF
            // readers use the last-defined revision when traversing xref tables.
            int dssObjStart = FindLastDssObject(text);
            if (dssObjStart < 0)
                return new DssData();

            // Extract the stream object's dictionary portion (between << and >>).
            int dictOpen = text.IndexOf("<<", dssObjStart, StringComparison.Ordinal);
            if (dictOpen < 0) return new DssData();

            string? dssDictText = ExtractDict(text, dictOpen);
            if (dssDictText == null) return new DssData();

            // Build an index of objects stored in compressed object streams (ObjStm).
            // This is required for PDFs that use PDF 1.5+ object compression — without it
            // the DSS certificate/OCSP/CRL objects cannot be found for any such PDF.
            var objStmIndex = BuildObjStmIndex(text);

            // Parse the three optional array keys.
            var globalCerts = ParseIndirectObjectArray(text, dssDictText, "/Certs",  ParseCert, objStmIndex);
            var globalOcsps = ParseIndirectObjectArray(text, dssDictText, "/OCSPs",  ParseOcsp, objStmIndex);
            var globalCrls  = ParseIndirectObjectArray(text, dssDictText, "/CRLs",   ParseCrl,  objStmIndex);

            // Parse /VRI dictionary.
            var vri = ParseVri(text, dssDictText, globalCerts, globalOcsps, globalCrls, objStmIndex);

            return new DssData
            {
                Certs         = globalCerts,
                OcspResponses = globalOcsps,
                Crls          = globalCrls,
                Vri           = vri,
            };
        }

        // -----------------------------------------------------------------------
        // Locate the /DSS object
        // -----------------------------------------------------------------------

        private static int FindLastDssObject(string text)
        {
            // Look for indirect object declarations that contain /Type /DSS or
            // simply have a /Certs or /OCSPs key (the /Type may be omitted).
            // Strategy: find "N 0 obj" headers whose body contains "/DSS " or
            // key "/Certs", "/OCSPs", "/CRLs", and "/VRI".
            // We use the catalog reference "/DSS N 0 R" when present, otherwise
            // fall back to searching for "/VRI" inside an obj block.
            int lastPos = -1;

            // First try: find "/DSS" as a catalog entry value reference.
            // The catalog looks like: /DSS 7 0 R
            int catPos = 0;
            while (true)
            {
                int pos = text.IndexOf("/DSS ", catPos, StringComparison.Ordinal);
                if (pos < 0) break;
                catPos = pos + 1;

                // After "/DSS " we expect digits then " 0 R".
                int i = pos + 5;
                while (i < text.Length && char.IsDigit(text[i])) i++;
                if (i < text.Length && text.Substring(i).StartsWith(" 0 R", StringComparison.Ordinal))
                {
                    // We have "N 0 R" — find that object.
                    string objNumStr = text.Substring(pos + 5, i - (pos + 5));
                    if (int.TryParse(objNumStr, out int objNum))
                    {
                        int objPos = FindObjectDefinition(text, objNum);
                        if (objPos > lastPos) lastPos = objPos;
                    }
                }
            }
            if (lastPos >= 0) return lastPos;

            // Fallback: find any object that declares /VRI and /Certs or /OCSPs.
            int searchFrom = 0;
            while (true)
            {
                int vriPos = text.IndexOf("/VRI", searchFrom, StringComparison.Ordinal);
                if (vriPos < 0) break;
                searchFrom = vriPos + 1;

                // Check the enclosing object also has /Certs or /OCSPs.
                // Accept both "N 0 obj\n" (LF) and "N 0 obj\r\n" (CRLF).
                int objStart = text.LastIndexOf(" obj\r\n", vriPos, StringComparison.Ordinal);
                if (objStart < 0)
                    objStart = text.LastIndexOf(" obj\n", vriPos, StringComparison.Ordinal);
                if (objStart < 0) continue;
                int blockEnd = text.IndexOf("endobj", objStart, StringComparison.Ordinal);
                if (blockEnd < 0) continue;
                string block = text.Substring(objStart, blockEnd - objStart);
                if ((block.Contains("/Certs") || block.Contains("/OCSPs")) && vriPos > lastPos)
                    lastPos = objStart;
            }
            return lastPos;
        }

        private static int FindObjectDefinition(string text, int objNum)
        {
            string header = $"{objNum} 0 obj";
            int pos = 0, last = -1;
            while (true)
            {
                int found = text.IndexOf(header, pos, StringComparison.Ordinal);
                if (found < 0) break;
                // Validate that the char before the obj number is whitespace or start-of-file.
                if (found == 0 || IsPdfWhitespace(text[found - 1]))
                    last = found;
                pos = found + 1;
            }
            return last;
        }

        // -----------------------------------------------------------------------
        // /VRI parsing
        // -----------------------------------------------------------------------

        private static IReadOnlyDictionary<string, VriEntry> ParseVri(
            string text,
            string dssDictText,
            IReadOnlyList<X509Certificate> globalCerts,
            IReadOnlyList<BasicOcspResp>   globalOcsps,
            IReadOnlyList<X509Crl>         globalCrls,
            IReadOnlyDictionary<int, byte[]> objStmIndex)
        {
            var result = new Dictionary<string, VriEntry>(StringComparer.OrdinalIgnoreCase);

            int vriKeyPos = dssDictText.IndexOf("/VRI", StringComparison.Ordinal);
            if (vriKeyPos < 0) return result;

            // /VRI value is an indirect reference or inline dict.
            int i = vriKeyPos + "/VRI".Length;
            while (i < dssDictText.Length && IsPdfWhitespace(dssDictText[i])) i++;

            string? vriDictText = null;

            if (i < dssDictText.Length && dssDictText[i] == '<' &&
                i + 1 < dssDictText.Length && dssDictText[i + 1] == '<')
            {
                // Inline dict
                vriDictText = ExtractDict(dssDictText, i);
            }
            else
            {
                // Indirect reference: "N 0 R"
                int refObjNum = ReadObjRef(dssDictText, i);
                if (refObjNum > 0)
                {
                    int objPos = FindObjectDefinition(text, refObjNum);
                    if (objPos >= 0)
                    {
                        int dictOpen = text.IndexOf("<<", objPos, StringComparison.Ordinal);
                        if (dictOpen >= 0)
                            vriDictText = ExtractDict(text, dictOpen);
                    }
                }
            }

            if (vriDictText == null) return result;

            // Each entry in /VRI is "/<HEX-SHA1> N 0 R" or "/<HEX-SHA1> << ... >>".
            int pos = 0;
            while (pos < vriDictText.Length)
            {
                // Find a name key that is a 40-hex-char SHA-1.
                int namePos = vriDictText.IndexOf('/', pos);
                if (namePos < 0) break;
                pos = namePos + 1;

                int nameEnd = pos;
                while (nameEnd < vriDictText.Length &&
                       !IsPdfWhitespace(vriDictText[nameEnd]) &&
                       !IsPdfDelimiter(vriDictText[nameEnd]))
                    nameEnd++;

                string nameVal = vriDictText.Substring(pos, nameEnd - pos);
                if (nameVal.Length != 40 || !IsHex(nameVal))
                    continue;

                // Read the VRI sub-dict.
                int j = nameEnd;
                while (j < vriDictText.Length && IsPdfWhitespace(vriDictText[j])) j++;

                string? entryDictText = null;
                if (j < vriDictText.Length && vriDictText[j] == '<' &&
                    j + 1 < vriDictText.Length && vriDictText[j + 1] == '<')
                {
                    entryDictText = ExtractDict(vriDictText, j);
                }
                else
                {
                    int refObjNum = ReadObjRef(vriDictText, j);
                    if (refObjNum > 0)
                    {
                        int objPos = FindObjectDefinition(text, refObjNum);
                        if (objPos >= 0)
                        {
                            int dictOpen = text.IndexOf("<<", objPos, StringComparison.Ordinal);
                            if (dictOpen >= 0)
                                entryDictText = ExtractDict(text, dictOpen);
                        }
                    }
                }

                if (entryDictText == null) continue;

                var entry = new VriEntry
                {
                    Certs = ParseIndirectObjectArray(text, entryDictText, "/Cert", ParseCert, objStmIndex),
                    Ocsps = ParseIndirectObjectArray(text, entryDictText, "/OCSP", ParseOcsp, objStmIndex),
                    Crls  = ParseIndirectObjectArray(text, entryDictText, "/CRL",  ParseCrl,  objStmIndex),
                };
                result[nameVal.ToUpperInvariant()] = entry;
                pos = nameEnd;
            }

            return result;
        }

        // -----------------------------------------------------------------------
        // Generic array-of-indirect-refs parser
        // -----------------------------------------------------------------------

        private static IReadOnlyList<T> ParseIndirectObjectArray<T>(
            string text,
            string dictText,
            string key,
            Func<byte[], T?> parse,
            IReadOnlyDictionary<int, byte[]> objStmIndex)
            where T : class
        {
            var results = new List<T>();

            int keyPos = dictText.IndexOf(key, StringComparison.Ordinal);
            if (keyPos < 0) return results;

            int i = keyPos + key.Length;
            while (i < dictText.Length && IsPdfWhitespace(dictText[i])) i++;
            if (i >= dictText.Length || dictText[i] != '[') return results;

            int closePos = dictText.IndexOf(']', i + 1);
            if (closePos < 0) return results;

            string inner = dictText.Substring(i + 1, closePos - i - 1).Trim();
            var tokens = inner.Split(new[] { ' ', '\t', '\n', '\r', '\f' },
                                     StringSplitOptions.RemoveEmptyEntries);

            // Tokens come in triples: "N", "0", "R"
            for (int t = 0; t + 2 < tokens.Length; t += 3)
            {
                if (!int.TryParse(tokens[t], out int objNum)) continue;
                if (tokens[t + 1] != "0" || tokens[t + 2] != "R") continue;

                byte[]? objBytes = ExtractStreamOrStringObject(text, objNum, objStmIndex);
                if (objBytes == null) continue;

                T? parsed = parse(objBytes);
                if (parsed != null) results.Add(parsed);
            }

            return results;
        }

        // -----------------------------------------------------------------------
        // Stream/string object extraction
        // -----------------------------------------------------------------------

        private static byte[]? ExtractStreamOrStringObject(
            string text, int objNum, IReadOnlyDictionary<int, byte[]> objStmIndex)
        {
            // Try direct object definition first.
            int objPos = FindObjectDefinition(text, objNum);
            if (objPos >= 0)
            {
                byte[]? direct = ExtractDirectObject(text, objPos);
                if (direct != null) return direct;
            }

            // Fallback: object may be in a compressed object stream (ObjStm, PDF 1.5+).
            // ObjStm bytes are the raw object body — decode hex strings (<HEXHEX...>) to binary.
            if (objStmIndex.TryGetValue(objNum, out var stmBytes))
                return DecodeObjStmValue(stmBytes);

            return null;
        }

        /// <summary>
        /// Interprets the raw body bytes of an ObjStm-embedded object.
        /// When the value is a PDF hex string (<c>&lt;HEXHEX...&gt;</c>) it is decoded to
        /// binary, which is the standard encoding for DSS certificates and OCSP responses.
        /// Stream objects cannot live inside an ObjStm (PDF spec §7.5.7), so this path
        /// covers only direct values.
        /// </summary>
        private static byte[]? DecodeObjStmValue(byte[] raw)
        {
            if (raw == null || raw.Length == 0) return null;

            // Skip leading PDF whitespace.
            int i = 0;
            while (i < raw.Length && IsPdfWhitespace((char)raw[i])) i++;
            if (i >= raw.Length) return null;

            // Hex string: <HEXHEX...>  (not '<<' which would be a dict start)
            if (raw[i] == (byte)'<' &&
                (i + 1 >= raw.Length || raw[i + 1] != (byte)'<'))
            {
                int closeAngle = Array.IndexOf(raw, (byte)'>', i + 1);
                if (closeAngle < 0) return null;
                string hex = Latin1.GetString(raw, i + 1, closeAngle - i - 1).Trim();
                if (hex.Length % 2 != 0) return null;
                var bytes = new byte[hex.Length / 2];
                for (int k = 0; k < bytes.Length; k++)
                    bytes[k] = Convert.ToByte(hex.Substring(k * 2, 2), 16);
                return bytes;
            }

            // Literal string or other value — return as-is (unlikely for DSS data).
            return raw;
        }

        /// <summary>
        /// Extracts the content of a direct (non-ObjStm) PDF object located at
        /// <paramref name="objPos"/> in the Latin-1 text.  Applies /FlateDecode
        /// decompression when the stream dictionary advertises it.
        /// </summary>
        private static byte[]? ExtractDirectObject(string text, int objPos)
        {
            // Accept both "stream\r\n" and "stream\n".
            int streamPosCrlf = text.IndexOf("stream\r\n", objPos, StringComparison.Ordinal);
            int streamPosLf   = text.IndexOf("stream\n",  objPos, StringComparison.Ordinal);
            int streamPos = (streamPosCrlf >= 0 && (streamPosLf < 0 || streamPosCrlf <= streamPosLf))
                                ? streamPosCrlf : streamPosLf;
            int streamHeaderLen = (streamPos == streamPosCrlf && streamPosCrlf >= 0) ? 8 : 7;
            int endobjPos = text.IndexOf("endobj", objPos, StringComparison.Ordinal);

            if (streamPos > 0 && (endobjPos < 0 || streamPos < endobjPos))
            {
                // Latin-1 GetBytes is 1-to-1 so binary content is preserved exactly.
                string dictText = text.Substring(objPos, streamPos - objPos);
                int start       = streamPos + streamHeaderLen;

                // Prefer /Length for exact byte count.  Scanning for the "endstream"
                // keyword inside a FlateDecode stream is unreliable because compressed
                // binary data can contain the byte sequence 0x65 6E 64 73 74 72 65 61 6D
                // ("endstream") by accident, causing truncation.
                int length = ResolveLength(text, dictText);
                byte[] raw;
                if (length >= 0 && start + length <= text.Length)
                {
                    raw = Latin1.GetBytes(text.Substring(start, length));
                }
                else
                {
                    // Fallback when /Length is absent or cannot be resolved.
                    int endstream = text.IndexOf("endstream", start, StringComparison.Ordinal);
                    if (endstream < 0) return null;
                    // Strip the trailing EOL that precedes "endstream" (PDF spec §7.3.8.1).
                    int end = endstream;
                    if (end > start && text[end - 1] == '\n') end--;
                    if (end > start && text[end - 1] == '\r') end--;
                    raw = Latin1.GetBytes(text.Substring(start, end - start));
                }

                if (StreamNeedsFlateDecode(dictText))
                    return DecompressFlateDecode(raw);

                return raw;
            }

            // Hex-string value: "N 0 obj\n<HEX>\nendobj"
            if (endobjPos > 0)
            {
                int dictOpen = text.IndexOf("<<", objPos, StringComparison.Ordinal);
                int hexOpen  = text.IndexOf('<', objPos);

                if (hexOpen > 0 && (dictOpen < 0 || hexOpen < dictOpen) && hexOpen < endobjPos)
                {
                    if (hexOpen + 1 < text.Length && text[hexOpen + 1] != '<')
                    {
                        int hexClose = text.IndexOf('>', hexOpen + 1);
                        if (hexClose > 0 && hexClose < endobjPos)
                        {
                            string hex = text.Substring(hexOpen + 1, hexClose - hexOpen - 1).Trim();
                            if (hex.Length % 2 != 0) return null;
                            var bytes = new byte[hex.Length / 2];
                            for (int k = 0; k < bytes.Length; k++)
                                bytes[k] = Convert.ToByte(hex.Substring(k * 2, 2), 16);
                            return bytes;
                        }
                    }
                }
            }
            return null;
        }

        // -----------------------------------------------------------------------
        // Compressed object stream (ObjStm) index builder  — PDF 1.5+
        // -----------------------------------------------------------------------

        /// <summary>
        /// Scans the PDF for all /Type /ObjStm objects, decompresses each, and
        /// returns a map of object-number → raw object bytes.  This resolves
        /// objects that are not written as first-class "N 0 obj" entries in the
        /// file — which is common in PDFs produced by Adobe Acrobat or iText 7
        /// when cross-reference compression is enabled.
        /// </summary>
        private static IReadOnlyDictionary<int, byte[]> BuildObjStmIndex(string text)
        {
            var index = new Dictionary<int, byte[]>();

            // Scan for all " 0 obj" headers in the file.
            int pos = 0;
            while (pos < text.Length)
            {
                int objSuffix = text.IndexOf(" 0 obj", pos, StringComparison.Ordinal);
                if (objSuffix < 0) break;
                pos = objSuffix + 1;

                // "obj" must be followed by whitespace or a delimiter.
                int afterObj = objSuffix + 6; // length of " 0 obj" = 6
                if (afterObj < text.Length &&
                    !IsPdfWhitespace(text[afterObj]) && !IsPdfDelimiter(text[afterObj]))
                    continue;

                // Walk backwards to extract the integer object number before " 0 obj".
                int numEnd   = objSuffix;
                int numStart = numEnd;
                while (numStart > 0 && char.IsDigit(text[numStart - 1])) numStart--;
                if (numStart >= numEnd) continue;
                // The char before the digits must be whitespace or start-of-file.
                if (numStart > 0 && !IsPdfWhitespace(text[numStart - 1])) continue;

                int objDefStart = numStart;

                // Limit the body scan to avoid reading the entire next object.
                int endobjPos = text.IndexOf("endobj", afterObj, StringComparison.Ordinal);
                if (endobjPos < 0) break;

                // Quick check: does this object look like an ObjStm?
                int bodyLen = Math.Min(300, endobjPos - afterObj);
                if (bodyLen <= 0) continue;
                string bodySnippet = text.Substring(afterObj, bodyLen);
                if (!bodySnippet.Contains("/ObjStm")) continue;

                // Verify the /ObjStm token is a name value after /Type.
                if (!ContainsTypeName(bodySnippet, "ObjStm")) continue;

                // Extract and (if necessary) decompress the ObjStm stream.
                byte[]? stmData = ExtractDirectObject(text, objDefStart);
                if (stmData == null) continue;

                // Find /N and /First in the stream dictionary.
                int streamKwd = text.IndexOf("stream", objDefStart, StringComparison.Ordinal);
                if (streamKwd < 0 || streamKwd > endobjPos) continue;
                string dictRegion = text.Substring(objDefStart, streamKwd - objDefStart);

                int n     = ParseDictInt(dictRegion, "/N");
                int first = ParseDictInt(dictRegion, "/First");
                if (n <= 0 || first <= 0 || first >= stmData.Length) continue;

                // Parse the header section: alternating objNum and byte-offset pairs.
                // We use ASCII because the header is guaranteed to be ASCII digits/spaces.
                string header = Encoding.ASCII.GetString(stmData, 0, first);
                var tokens = header.Split(new[] { ' ', '\t', '\n', '\r', '\f' },
                                          StringSplitOptions.RemoveEmptyEntries);

                for (int t = 0; t + 1 < tokens.Length && t / 2 < n; t += 2)
                {
                    if (!int.TryParse(tokens[t],     NumberStyles.None,
                            CultureInfo.InvariantCulture, out int entryObjNum)) continue;
                    if (!int.TryParse(tokens[t + 1], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int offset)) continue;

                    int dataStart = first + offset;
                    // Bound this object by the next object's start (or end of stream).
                    int dataEnd = stmData.Length;
                    if (t + 3 < tokens.Length &&
                        int.TryParse(tokens[t + 3], NumberStyles.None,
                            CultureInfo.InvariantCulture, out int nextOffset))
                        dataEnd = Math.Min(stmData.Length, first + nextOffset);

                    if (dataStart >= dataEnd || dataStart >= stmData.Length) continue;

                    var objBytes = new byte[dataEnd - dataStart];
                    Array.Copy(stmData, dataStart, objBytes, 0, objBytes.Length);
                    // Later revision wins: always overwrite earlier definitions.
                    index[entryObjNum] = objBytes;
                }
            }

            return index;
        }

        /// <summary>
        /// Returns true if <paramref name="dictText"/> (the region between "N 0 obj" and
        /// "stream") contains <c>/Type /name</c> where name starts with
        /// <paramref name="typeName"/>.  Checks that the token boundary after the name
        /// is a whitespace or delimiter to avoid substring false-positives.
        /// </summary>
        private static bool ContainsTypeName(string dictText, string typeName)
        {
            int pos = 0;
            while (pos < dictText.Length)
            {
                int typeIdx = dictText.IndexOf("/Type", pos, StringComparison.Ordinal);
                if (typeIdx < 0) return false;
                pos = typeIdx + 1;

                int i = typeIdx + "/Type".Length;
                while (i < dictText.Length && IsPdfWhitespace(dictText[i])) i++;
                if (i >= dictText.Length || dictText[i] != '/') continue;
                i++;
                if (!dictText.Substring(i).StartsWith(typeName, StringComparison.Ordinal)) continue;
                int endName = i + typeName.Length;
                if (endName < dictText.Length &&
                    !IsPdfWhitespace(dictText[endName]) && !IsPdfDelimiter(dictText[endName]))
                    continue;
                return true;
            }
            return false;
        }

        // -----------------------------------------------------------------------
        // /FlateDecode support
        // -----------------------------------------------------------------------

        private static bool StreamNeedsFlateDecode(string dictText)
        {
            int filterPos = dictText.IndexOf("/Filter", StringComparison.Ordinal);
            if (filterPos < 0) return false;

            int i = filterPos + "/Filter".Length;
            while (i < dictText.Length && IsPdfWhitespace(dictText[i])) i++;
            if (i >= dictText.Length) return false;

            if (dictText[i] == '/')
            {
                // Direct name: /Filter /FlateDecode  or  /Filter /Fl
                int nameStart = i + 1;
                int nameEnd   = nameStart;
                while (nameEnd < dictText.Length &&
                       !IsPdfWhitespace(dictText[nameEnd]) && !IsPdfDelimiter(dictText[nameEnd]))
                    nameEnd++;
                string name = dictText.Substring(nameStart, nameEnd - nameStart);
                return name == "FlateDecode" || name == "Fl";
            }
            if (dictText[i] == '[')
            {
                // Array form — only handle the single-filter case.  A chain of multiple
                // filters (e.g. [/FlateDecode /ASCIIHexDecode]) would require applying
                // each filter in sequence, which is not implemented; return false so the
                // raw bytes are passed through unchanged rather than partially decoded.
                int close = dictText.IndexOf(']', i + 1);
                if (close < 0) return false;
                string inner = dictText.Substring(i + 1, close - i - 1);
                // Count name tokens (tokens starting with '/').
                int nameCount = 0;
                bool isFlate  = false;
                foreach (string tok in inner.Split(new[] { ' ', '\t', '\n', '\r', '\f' },
                                                   StringSplitOptions.RemoveEmptyEntries))
                {
                    if (tok.StartsWith("/", StringComparison.Ordinal))
                    {
                        nameCount++;
                        if (tok == "/FlateDecode" || tok == "/Fl") isFlate = true;
                    }
                }
                return nameCount == 1 && isFlate;
            }
            return false;
        }

        /// <summary>
        /// Decompresses a zlib/deflate byte stream (PDF /FlateDecode filter).
        /// Tries stripping the 2-byte zlib header (0x78 …) first; falls back to
        /// raw deflate if that fails.
        /// </summary>
        private static byte[]? DecompressFlateDecode(byte[] compressed)
        {
            if (compressed == null || compressed.Length < 2) return null;

            // Zlib streams start with a CMF byte of 0x78 (deflate, 32 KB window).
            int skip = compressed[0] == 0x78 ? 2 : 0;

            try
            {
                using var ms      = new MemoryStream(compressed, skip, compressed.Length - skip);
                using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
                using var output  = new MemoryStream();
                deflate.CopyTo(output);
                byte[] result = output.ToArray();
                if (result.Length > 0) return result;
            }
            catch { /* fall through */ }

            if (skip > 0)
            {
                // Retry without skipping (raw deflate without zlib header).
                try
                {
                    using var ms      = new MemoryStream(compressed);
                    using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
                    using var output  = new MemoryStream();
                    deflate.CopyTo(output);
                    return output.ToArray();
                }
                catch { /* skip */ }
            }
            return null;
        }

        // -----------------------------------------------------------------------
        // Integer value parser for PDF dictionary keys
        // -----------------------------------------------------------------------

        private static int ParseDictInt(string dictText, string key)
        {
            int keyPos = dictText.IndexOf(key, StringComparison.Ordinal);
            if (keyPos < 0) return -1;
            int i = keyPos + key.Length;
            while (i < dictText.Length && IsPdfWhitespace(dictText[i])) i++;
            int numStart = i;
            while (i < dictText.Length && char.IsDigit(dictText[i])) i++;
            if (i == numStart) return -1;
            return int.TryParse(dictText.Substring(numStart, i - numStart),
                                NumberStyles.None, CultureInfo.InvariantCulture, out int val) ? val : -1;
        }

        /// <summary>
        /// Returns the integer /Length value from a stream dictionary text, resolving a
        /// single indirect reference ("N 0 R") when the value is not a literal integer.
        /// Returns -1 when /Length is absent or cannot be determined.
        /// </summary>
        private static int ResolveLength(string fullText, string dictText)
        {
            int keyPos = dictText.IndexOf("/Length", StringComparison.Ordinal);
            if (keyPos < 0) return -1;
            int i = keyPos + "/Length".Length;
            while (i < dictText.Length && IsPdfWhitespace(dictText[i])) i++;

            int numStart = i;
            while (i < dictText.Length && char.IsDigit(dictText[i])) i++;
            if (i == numStart) return -1;

            if (!int.TryParse(dictText.Substring(numStart, i - numStart),
                    NumberStyles.None, CultureInfo.InvariantCulture, out int firstNum))
                return -1;

            // Check for indirect reference pattern: "N 0 R" — where firstNum is the
            // object number and the next tokens are the generation number (0) and 'R'.
            int j = i;
            while (j < dictText.Length && IsPdfWhitespace(dictText[j])) j++;
            if (j < dictText.Length && dictText[j] == '0')
            {
                int k = j + 1;
                while (k < dictText.Length && IsPdfWhitespace(dictText[k])) k++;
                if (k < dictText.Length && dictText[k] == 'R')
                {
                    // Resolve: find "firstNum 0 obj <integer> endobj" in the full text.
                    int objPos = FindObjectDefinition(fullText, firstNum);
                    if (objPos < 0) return -1;
                    int bodyStart = objPos + $"{firstNum} 0 obj".Length;
                    while (bodyStart < fullText.Length && IsPdfWhitespace(fullText[bodyStart]))
                        bodyStart++;
                    int bodyEnd = bodyStart;
                    while (bodyEnd < fullText.Length && char.IsDigit(fullText[bodyEnd])) bodyEnd++;
                    if (bodyEnd == bodyStart) return -1;
                    return int.TryParse(fullText.Substring(bodyStart, bodyEnd - bodyStart),
                                        NumberStyles.None, CultureInfo.InvariantCulture, out int resolved)
                           ? resolved : -1;
                }
            }

            return firstNum; // direct integer value
        }

        // -----------------------------------------------------------------------
        // Object parsers
        // -----------------------------------------------------------------------

        private static X509Certificate? ParseCert(byte[] der)
        {
            try { return new X509CertificateParser().ReadCertificate(der); }
            catch { return null; }
        }

        private static BasicOcspResp? ParseOcsp(byte[] der)
        {
            try
            {
                var ocspResp = new OcspResp(der);
                return (BasicOcspResp?)ocspResp.GetResponseObject();
            }
            catch
            {
                // Also try parsing directly as BasicOCSPResponse DER.
                try { return new BasicOcspResp(BasicOcspResponse.GetInstance(Org.BouncyCastle.Asn1.Asn1Object.FromByteArray(der))); }
                catch { return null; }
            }
        }

        private static X509Crl? ParseCrl(byte[] der)
        {
            try { return new X509CrlParser().ReadCrl(der); }
            catch { return null; }
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static bool IsPdfWhitespace(char c) =>
            c == '\0' || c == '\t' || c == '\n' || c == '\f' || c == '\r' || c == ' ';

        private static bool IsPdfDelimiter(char c) =>
            c == '(' || c == ')' || c == '<' || c == '>' ||
            c == '[' || c == ']' || c == '{' || c == '}' || c == '/' || c == '%';

        private static bool IsHex(string s)
        {
            foreach (char c in s)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            return true;
        }

        private static int ReadObjRef(string text, int pos)
        {
            int i = pos;
            int numStart = i;
            while (i < text.Length && char.IsDigit(text[i])) i++;
            if (i == numStart) return -1;
            if (!int.TryParse(text.Substring(numStart, i - numStart), out int num)) return -1;
            // Skip " 0 R"
            int j = i;
            while (j < text.Length && IsPdfWhitespace(text[j])) j++;
            if (j + 3 >= text.Length) return -1;
            if (text[j] == '0' && IsPdfWhitespace(text[j + 1]) && text[j + 2] == 'R')
                return num;
            return -1;
        }

        private static string? ExtractDict(string pdfText, int start)
        {
            int depth = 0;
            int pos   = start;
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
                        if      (pdfText[pos] == '(') pd++;
                        else if (pdfText[pos] == ')') pd--;
                        pos++;
                    }
                    continue;
                }
                if (c == '<' && pdfText[pos + 1] == '<') { depth++; pos += 2; }
                else if (c == '<')
                {
                    int close = pdfText.IndexOf('>', pos + 1);
                    if (close < 0) return null;
                    pos = close + 1;
                }
                else if (c == '>' && pdfText[pos + 1] == '>')
                {
                    depth--;
                    pos += 2;
                    if (depth == 0) return pdfText.Substring(start, pos - start);
                }
                else pos++;
            }
            return null;
        }
    }

    // =========================================================================
    // DssRevocationChecker
    // =========================================================================

    /// <summary>
    /// Checks whether DSS-embedded OCSP responses or CRLs indicate that a
    /// certificate is Good, Revoked, or Unknown at a given reference time.
    /// </summary>
    internal static class DssRevocationChecker
    {
        public static DssRevocationResult Check(
            X509Certificate subject,
            X509Certificate issuer,
            DssData dss,
            byte[]? signatureBytes,
            DateTime referenceTime)
        {
            // Prefer VRI keyed by SHA-1(signatureBytes) if available.
            BasicOcspResp[] ocsps;
            X509Crl[]       crls;

            if (signatureBytes != null)
            {
#pragma warning disable CA5350 // SHA-1 is mandated by PDF 32000-1 §12.8.4.3 for the VRI dictionary key
                string sha1Hex = BytesToHex(ComputeSha1(signatureBytes)).ToUpperInvariant();
#pragma warning restore CA5350
                if (dss.Vri.TryGetValue(sha1Hex, out var vri))
                {
                    ocsps = vri.Ocsps.Concat(dss.OcspResponses).Distinct().ToArray();
                    crls  = vri.Crls.Concat(dss.Crls).Distinct().ToArray();
                }
                else
                {
                    ocsps = dss.OcspResponses.ToArray();
                    crls  = dss.Crls.ToArray();
                }
            }
            else
            {
                ocsps = dss.OcspResponses.ToArray();
                crls  = dss.Crls.ToArray();
            }

            // Try OCSP first.
            foreach (var ocsp in ocsps)
            {
                var result = CheckOcsp(ocsp, subject, issuer, referenceTime);
                if (result.Status != DssRevocationStatus.Unknown)
                    return result;
            }

            // Fall back to CRL.
            foreach (var crl in crls)
            {
                var result = CheckCrl(crl, subject, issuer, referenceTime);
                if (result.Status != DssRevocationStatus.Unknown)
                    return result;
            }

            return new DssRevocationResult(DssRevocationStatus.Unknown, "No applicable DSS revocation data found.");
        }

        // OID for id-kp-OCSPSigning (RFC 6960)
        private const string IdKpOcspSigning = "1.3.6.1.5.5.7.3.9";

        private static DssRevocationResult CheckOcsp(
            BasicOcspResp ocsp, X509Certificate subject, X509Certificate issuer, DateTime referenceTime)
        {
            try
            {
                // ── Step 1: Verify the OCSP response signature ─────────────
                // Verify() returns bool — false means bad signature; must not be ignored.
                // Try issuer key first.  If that fails, look for a delegated OCSP responder
                // cert embedded in the response.  An unverifiable signature → Unknown so a
                // forged "Good" cannot be trusted.
                bool sigVerified;
                try
                {
                    sigVerified = ocsp.Verify(issuer.GetPublicKey())
                               && ResponderIdMatchesCert(ocsp.ResponderId, issuer);
                }
                catch
                {
                    sigVerified = false;
                }

                if (!sigVerified)
                {
                    // Issuer key didn't work — try delegated OCSP responder certs.
                    DateTime producedAt = ocsp.ProducedAt;
                    foreach (var c in ocsp.GetCerts() ?? Array.Empty<X509Certificate>())
                    {
                        try
                        {
                            // Delegated responder must be signed by the issuer.
                            c.Verify(issuer.GetPublicKey());

                            // Must carry id-kp-OCSPSigning EKU.
                            var eku = c.GetExtendedKeyUsage();
                            bool hasOcspEku = eku != null &&
                                ((System.Collections.IEnumerable)eku).Cast<object>()
                                    .Any(o => string.Equals(o?.ToString(), IdKpOcspSigning, StringComparison.Ordinal));
                            if (!hasOcspEku) continue;

                            // Must be valid at producedAt.
                            if (c.NotBefore > producedAt || c.NotAfter < producedAt) continue;

                            // Responder ID must match this cert (by name or key hash).
                            if (!ResponderIdMatchesCert(ocsp.ResponderId, c)) continue;

                            bool ok;
                            try { ok = ocsp.Verify(c.GetPublicKey()); }
                            catch { ok = false; }
                            if (!ok) continue;

                            sigVerified = true;
                            break;
                        }
                        catch { /* try next */ }
                    }
                }

                if (!sigVerified)
                    return new DssRevocationResult(DssRevocationStatus.Unknown,
                        "OCSP response signature could not be verified.");

                // ── Step 2: Find the SingleResp for our certificate ────────
#pragma warning disable CS0618 // CertificateID(string,…) is deprecated but still functional in this BouncyCastle version
                var certId = new CertificateID(
                    Org.BouncyCastle.Asn1.Oiw.OiwObjectIdentifiers.IdSha1.Id,
                    issuer, subject.SerialNumber);
#pragma warning restore CS0618

                foreach (SingleResp resp in ocsp.Responses)
                {
                    if (!resp.GetCertID().Equals(certId)) continue;

                    // thisUpdate must not be more than 5 minutes after referenceTime
                    // (allows for minor clock skew).
                    if (resp.ThisUpdate > referenceTime.AddMinutes(5))
                        continue;

                    // nextUpdate, if present, must not have expired more than 1 hour before
                    // referenceTime (tolerance for DSS data embedded before validation).
                    if (resp.NextUpdate != null && resp.NextUpdate.Value < referenceTime.AddHours(-1))
                        return new DssRevocationResult(DssRevocationStatus.Unknown, "OCSP response is stale.");

                    var status = resp.GetCertStatus();
                    if (status == CertificateStatus.Good)
                        return new DssRevocationResult(DssRevocationStatus.Good, null);

                    if (status is RevokedStatus rev)
                    {
                        // Historical validation: only revoked if revocation happened at or
                        // before referenceTime.
                        if (rev.RevocationTime <= referenceTime)
                            return new DssRevocationResult(DssRevocationStatus.Revoked,
                                $"Certificate revoked at {rev.RevocationTime:u}.");
                        // Revoked after referenceTime → still Good at referenceTime.
                        return new DssRevocationResult(DssRevocationStatus.Good, null);
                    }

                    return new DssRevocationResult(DssRevocationStatus.Unknown, "OCSP status unknown.");
                }
            }
            catch { /* malformed — skip */ }
            return new DssRevocationResult(DssRevocationStatus.Unknown, null);
        }

        /// <summary>
        /// Returns true if the OCSP <see cref="RespID"/> matches <paramref name="cert"/>
        /// by either subject DN (byName) or key hash (byKey).
        /// </summary>
        private static bool ResponderIdMatchesCert(RespID responderId, X509Certificate cert)
        {
            try
            {
                // byName: create a RespID from the cert's subject DN and compare.
                if (responderId.Equals(new RespID(cert.SubjectDN)))
                    return true;

                // byKey: create a RespID from the cert's public key (computes SHA-1 key hash internally).
                if (responderId.Equals(new RespID(cert.GetPublicKey())))
                    return true;
            }
            catch { /* ignore malformed responder ID */ }
            return false;
        }

        private static DssRevocationResult CheckCrl(
            X509Crl crl, X509Certificate subject, X509Certificate issuer, DateTime referenceTime)
        {
            try
            {
                // Validate that this CRL was issued by the cert's issuer.
                if (!crl.IssuerDN.Equivalent(issuer.SubjectDN))
                    return new DssRevocationResult(DssRevocationStatus.Unknown, null);

                // Verify the CRL signature using the issuer's public key.
                try { crl.Verify(issuer.GetPublicKey()); }
                catch
                {
                    return new DssRevocationResult(DssRevocationStatus.Unknown,
                        "CRL signature could not be verified.");
                }

                // thisUpdate must not be more than 5 minutes after referenceTime.
                if (crl.ThisUpdate > referenceTime.AddMinutes(5))
                    return new DssRevocationResult(DssRevocationStatus.Unknown,
                        "CRL thisUpdate is in the future relative to referenceTime.");

                // nextUpdate, if present, must not have expired more than 1 hour before referenceTime.
                if (crl.NextUpdate != null && crl.NextUpdate.Value < referenceTime.AddHours(-1))
                    return new DssRevocationResult(DssRevocationStatus.Unknown, "CRL is stale.");

                var entry = crl.GetRevokedCertificate(subject.SerialNumber);
                if (entry != null)
                {
                    // Historical validation: only revoked if revocation occurred at or before referenceTime.
                    if (entry.RevocationDate <= referenceTime)
                        return new DssRevocationResult(DssRevocationStatus.Revoked,
                            $"Certificate revoked (CRL) at {entry.RevocationDate:u}.");
                    // Revoked after referenceTime → Good at referenceTime.
                    return new DssRevocationResult(DssRevocationStatus.Good, null);
                }

                return new DssRevocationResult(DssRevocationStatus.Good, null);
            }
            catch { return new DssRevocationResult(DssRevocationStatus.Unknown, null); }
        }

        private static string BytesToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.AppendFormat("{0:x2}", b);
            return sb.ToString();
        }

#pragma warning disable CA5350
        private static byte[] ComputeSha1(byte[] data)
        {
#if NET5_0_OR_GREATER
            return SHA1.HashData(data);
#else
            using var sha1 = SHA1.Create();
            return sha1.ComputeHash(data);
#endif
        }
#pragma warning restore CA5350
    }

    internal enum DssRevocationStatus { Good, Revoked, Unknown }

    internal sealed class DssRevocationResult
    {
        public DssRevocationStatus Status  { get; }
        public string?             Message { get; }
        public DssRevocationResult(DssRevocationStatus s, string? m) { Status = s; Message = m; }
    }
}
