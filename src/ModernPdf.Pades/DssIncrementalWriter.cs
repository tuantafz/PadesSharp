// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ModernPdf.Abstractions.Dss;

namespace ModernPdf.Pades
{
    /// <summary>
    /// Appends a /DSS (Document Security Store) dictionary to a signed PDF as an
    /// incremental update to enable PAdES-LTV long-term validation.
    /// The byte range of the existing signature is never modified.
    /// </summary>
    public sealed class DssIncrementalWriter : IDssPdfWriter
    {
        // Latin-1 encoding for PDF text I/O (matches PdfLiteWriter convention).
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        /// <inheritdoc/>
        public byte[] AppendDss(byte[] signedPdfBytes, DssData dssData)
        {
            if (signedPdfBytes == null || signedPdfBytes.Length == 0)
                throw new ArgumentNullException(nameof(signedPdfBytes));
            if (dssData == null)
                throw new ArgumentNullException(nameof(dssData));

            // Convert to text for structural parsing (offsets remain valid in byte form).
            string pdfText = Latin1.GetString(signedPdfBytes);

            var (prevStartxref, prevSize, catalogObjNum) = ParseTrailer(pdfText);
            string existingCatalogDict = ExtractObjectDict(pdfText, catalogObjNum);

            using (var ms = new MemoryStream(signedPdfBytes.Length + 65536))
            {
                // Start with the original signed bytes (untouched).
                ms.Write(signedPdfBytes, 0, signedPdfBytes.Length);

                int nextObjNum = prevSize;
                var objOffsets = new List<(int ObjNum, long Offset)>();

                // --- Write stream objects for each cert ---
                var certObjNums = new List<int>();
                foreach (byte[] certBytes in dssData.Certificates)
                {
                    int num = nextObjNum++;
                    objOffsets.Add((num, ms.Position));
                    WriteStreamObject(ms, num, certBytes);
                    certObjNums.Add(num);
                }

                // --- Write stream objects for each OCSP response ---
                var ocspObjNums = new List<int>();
                foreach (byte[] ocspBytes in dssData.OcspResponses)
                {
                    int num = nextObjNum++;
                    objOffsets.Add((num, ms.Position));
                    WriteStreamObject(ms, num, ocspBytes);
                    ocspObjNums.Add(num);
                }

                // --- Write stream objects for each CRL ---
                var crlObjNums = new List<int>();
                foreach (byte[] crlBytes in dssData.Crls)
                {
                    int num = nextObjNum++;
                    objOffsets.Add((num, ms.Position));
                    WriteStreamObject(ms, num, crlBytes);
                    crlObjNums.Add(num);
                }

                // --- Write the /DSS dictionary ---
                int dssObjNum = nextObjNum++;
                objOffsets.Add((dssObjNum, ms.Position));
                WriteDssDict(ms, dssObjNum, certObjNums, ocspObjNums, crlObjNums, dssData.Vri);

                // --- Write updated Catalog (incremental override of the catalog object) ---
                string newCatalogDict = InjectDssEntry(existingCatalogDict, dssObjNum);
                objOffsets.Add((catalogObjNum, ms.Position));
                WriteTextObject(ms, catalogObjNum, newCatalogDict);

                // --- Write incremental cross-reference table and trailer ---
                long xrefOffset = ms.Position;
                WriteIncrementalXrefAndTrailer(ms, objOffsets, prevSize, nextObjNum,
                                               catalogObjNum, prevStartxref, xrefOffset);

                return ms.ToArray();
            }
        }

        // -----------------------------------------------------------------------
        // PDF object writers
        // -----------------------------------------------------------------------

        private static void WriteStreamObject(MemoryStream ms, int objNum, byte[] data)
        {
            // Object header + stream keyword
            WriteText(ms, $"{objNum} 0 obj\n<< /Length {data.Length} >>\nstream\n");
            // Raw binary content
            ms.Write(data, 0, data.Length);
            // Stream footer
            WriteText(ms, "\nendstream\nendobj\n");
        }

        private static void WriteTextObject(MemoryStream ms, int objNum, string dictBody)
        {
            WriteText(ms, $"{objNum} 0 obj\n{dictBody}\nendobj\n");
        }

        private static void WriteDssDict(
            MemoryStream ms,
            int dssObjNum,
            List<int> certObjNums,
            List<int> ocspObjNums,
            List<int> crlObjNums,
            Dictionary<string, VriData> vri)
        {
            var sb = new StringBuilder();
            sb.Append($"{dssObjNum} 0 obj\n");
            sb.Append("<< /Type /DSS\n");

            if (certObjNums.Count > 0)
            {
                sb.Append("/Certs [");
                foreach (int n in certObjNums)
                    sb.Append($"{n} 0 R ");
                sb.Append("]\n");
            }

            if (ocspObjNums.Count > 0)
            {
                sb.Append("/OCSPs [");
                foreach (int n in ocspObjNums)
                    sb.Append($"{n} 0 R ");
                sb.Append("]\n");
            }

            if (crlObjNums.Count > 0)
            {
                sb.Append("/CRLs [");
                foreach (int n in crlObjNums)
                    sb.Append($"{n} 0 R ");
                sb.Append("]\n");
            }

            if (vri.Count > 0)
            {
                sb.Append("/VRI <<\n");
                foreach (var kvp in vri)
                {
                    sb.Append($"/{kvp.Key} <<\n");

                    var vriEntry = kvp.Value;

                    if (vriEntry.CertificateIndices.Count > 0)
                    {
                        sb.Append("/Cert [");
                        foreach (int idx in vriEntry.CertificateIndices)
                        {
                            if (idx < certObjNums.Count)
                                sb.Append($"{certObjNums[idx]} 0 R ");
                        }
                        sb.Append("]\n");
                    }

                    if (vriEntry.OcspIndices.Count > 0)
                    {
                        sb.Append("/OCSP [");
                        foreach (int idx in vriEntry.OcspIndices)
                        {
                            if (idx < ocspObjNums.Count)
                                sb.Append($"{ocspObjNums[idx]} 0 R ");
                        }
                        sb.Append("]\n");
                    }

                    if (vriEntry.CrlIndices.Count > 0)
                    {
                        sb.Append("/CRL [");
                        foreach (int idx in vriEntry.CrlIndices)
                        {
                            if (idx < crlObjNums.Count)
                                sb.Append($"{crlObjNums[idx]} 0 R ");
                        }
                        sb.Append("]\n");
                    }

                    sb.Append(">>\n");
                }
                sb.Append(">>\n");
            }

            sb.Append(">>\nendobj\n");
            WriteText(ms, sb.ToString());
        }

        private static void WriteIncrementalXrefAndTrailer(
            MemoryStream ms,
            List<(int ObjNum, long Offset)> objOffsets,
            int prevSize,
            int nextObjNum,
            int catalogObjNum,
            long prevStartxref,
            long xrefOffset)
        {
            // Sort entries by object number so we can emit contiguous subsections.
            var sorted = new List<(int ObjNum, long Offset)>(objOffsets);
            sorted.Sort((a, b) => a.ObjNum.CompareTo(b.ObjNum));

            var sb = new StringBuilder();
            sb.Append("xref\n");

            // Group into contiguous runs and emit one subsection per run.
            int i = 0;
            while (i < sorted.Count)
            {
                int runStart = sorted[i].ObjNum;
                int j = i;
                while (j + 1 < sorted.Count && sorted[j + 1].ObjNum == sorted[j].ObjNum + 1)
                    j++;
                int runCount = j - i + 1;

                sb.Append($"{runStart} {runCount}\n");
                for (int k = i; k <= j; k++)
                    sb.Append(sorted[k].Offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");

                i = j + 1;
            }

            // /Size = total object count (max of old size and new highest object number + 1).
            int newSize = Math.Max(prevSize, nextObjNum);

            sb.Append($"trailer\n<< /Size {newSize} /Root {catalogObjNum} 0 R /Prev {prevStartxref} >>\n");
            sb.Append($"startxref\n{xrefOffset}\n%%EOF\n");

            WriteText(ms, sb.ToString());
        }

        // -----------------------------------------------------------------------
        // PDF structural parsing helpers
        // -----------------------------------------------------------------------

        private static (long PrevStartxref, int PrevSize, int CatalogObjNum) ParseTrailer(string pdfText)
        {
            const string sxMarker      = "startxref\n";
            const string trailerMarker = "trailer\n";
            const string sizeKey       = "/Size ";
            const string rootKey       = "/Root ";

            int sxPos = pdfText.LastIndexOf(sxMarker, StringComparison.Ordinal);
            if (sxPos < 0)
                throw new InvalidOperationException("'startxref' not found in PDF.");

            int numStart = sxPos + sxMarker.Length;
            int numEnd   = pdfText.IndexOf('\n', numStart);
            long prevStartxref = long.Parse(
                pdfText.Substring(numStart, numEnd - numStart).Trim(),
                CultureInfo.InvariantCulture);

            int trailerPos = pdfText.LastIndexOf(trailerMarker, StringComparison.Ordinal);
            if (trailerPos < 0)
                throw new InvalidOperationException("'trailer' not found in PDF.");

            int sizeKeyPos = pdfText.IndexOf(sizeKey, trailerPos, StringComparison.Ordinal);
            if (sizeKeyPos < 0)
                throw new InvalidOperationException("'/Size' not found in PDF trailer.");

            int sizeNumStart = sizeKeyPos + sizeKey.Length;
            int sizeNumEnd   = sizeNumStart;
            while (sizeNumEnd < pdfText.Length && char.IsDigit(pdfText[sizeNumEnd]))
                sizeNumEnd++;
            int prevSize = int.Parse(
                pdfText.Substring(sizeNumStart, sizeNumEnd - sizeNumStart),
                CultureInfo.InvariantCulture);

            // Parse /Root N 0 R from the last trailer to locate the Catalog object.
            // Per PDF spec, the Catalog is always referenced by /Root — NOT necessarily object 1.
            int rootKeyPos = pdfText.IndexOf(rootKey, trailerPos, StringComparison.Ordinal);
            if (rootKeyPos < 0)
                throw new InvalidOperationException("'/Root' not found in PDF trailer.");

            int rootNumStart = rootKeyPos + rootKey.Length;
            int rootNumEnd   = rootNumStart;
            while (rootNumEnd < pdfText.Length && char.IsDigit(pdfText[rootNumEnd]))
                rootNumEnd++;
            int catalogObjNum = int.Parse(
                pdfText.Substring(rootNumStart, rootNumEnd - rootNumStart),
                CultureInfo.InvariantCulture);

            return (prevStartxref, prevSize, catalogObjNum);
        }

        private static string ExtractObjectDict(string pdfText, int objNum)
        {
            string marker = $"{objNum} 0 obj\n";
            // Search for the most recent definition of this object number
            // (the last occurrence wins in an incremental-update PDF).
            int objPos = pdfText.LastIndexOf(marker, StringComparison.Ordinal);
            if (objPos < 0)
                throw new InvalidOperationException($"Object {objNum} not found in PDF.");

            int dictStart = pdfText.IndexOf("<<", objPos + marker.Length, StringComparison.Ordinal);
            if (dictStart < 0)
                throw new InvalidOperationException($"No dictionary in object {objNum}.");

            // Walk forward counting nested << >> pairs.
            // Skip hex strings <...> to avoid treating their content as dict delimiters.
            int depth = 0;
            int pos   = dictStart;
            while (pos < pdfText.Length - 1)
            {
                char c = pdfText[pos];

                // Skip PDF literal strings (parentheses may be nested/escaped)
                if (c == '(')
                {
                    pos++;
                    int parenDepth = 1;
                    while (pos < pdfText.Length && parenDepth > 0)
                    {
                        if (pdfText[pos] == '\\') { pos += 2; continue; }
                        if (pdfText[pos] == '(') parenDepth++;
                        else if (pdfText[pos] == ')') parenDepth--;
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
                    // Hex string <AABB...> — skip to matching >
                    pos = pdfText.IndexOf('>', pos + 1) + 1;
                    if (pos == 0) throw new InvalidOperationException($"Unterminated hex string in object {objNum}.");
                }
                else if (c == '>' && pdfText[pos + 1] == '>')
                {
                    depth--;
                    pos += 2;
                    if (depth == 0)
                        return pdfText.Substring(dictStart, pos - dictStart);
                }
                else
                {
                    pos++;
                }
            }
            throw new InvalidOperationException($"Unbalanced dictionary in object {objNum}.");
        }

        private static string InjectDssEntry(string catalogDict, int dssObjNum)
        {
            // Insert /DSS before the final ">>" of the catalog dictionary.
            int lastClose = catalogDict.LastIndexOf(">>", StringComparison.Ordinal);
            return catalogDict.Substring(0, lastClose)
                 + $"/DSS {dssObjNum} 0 R "
                 + ">>";
        }

        // -----------------------------------------------------------------------
        // Low-level write helper
        // -----------------------------------------------------------------------

        private static void WriteText(MemoryStream ms, string text)
        {
            byte[] bytes = Latin1.GetBytes(text);
            ms.Write(bytes, 0, bytes.Length);
        }
    }
}
