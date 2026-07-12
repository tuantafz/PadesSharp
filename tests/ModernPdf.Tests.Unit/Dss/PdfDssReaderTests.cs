// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FluentAssertions;
using ModernPdf.Validation.Internal;
using Org.BouncyCastle.X509;

namespace ModernPdf.Tests.Unit.Dss;

// ===========================================================================
// PdfDssReader — FlateDecode and ObjStm fixture tests
// ===========================================================================

/// <summary>
/// Verifies that <see cref="PdfDssReader"/> correctly extracts DSS revocation data
/// from PDFs that use:
///   (a) FlateDecode-compressed stream objects for certificates/OCSP responses.
///   (b) PDF 1.5+ compressed object streams (ObjStm) for certificate objects.
///   (c) /Length-based stream extraction (endstream keyword inside compressed data must
///       not confuse the reader).
///
/// All PDFs are constructed in-memory from first principles so the tests have no
/// dependency on external fixture files.
/// </summary>
public class PdfDssReaderTests
{
    private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

    // -----------------------------------------------------------------------
    // Helpers — minimal PDF builder
    // -----------------------------------------------------------------------

    /// <summary>
    /// Compresses <paramref name="data"/> with zlib (CMF byte 0x78 + deflate stream).
    /// The Adler-32 trailer is omitted; DeflateStream decompresses correctly without it.
    /// </summary>
    private static byte[] ZlibCompress(byte[] data)
    {
        using var output = new MemoryStream();
        output.WriteByte(0x78); // CMF: deflate, 32 KB window
        output.WriteByte(0x9C); // FLG: default compression
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(data, 0, data.Length);
        return output.ToArray();
    }

    private static byte[] Latin1Bytes(string s) => Latin1.GetBytes(s);

    // -------
    // Builds a minimal 4-object PDF:
    //   1: Catalog  (/DSS 4 0 R)
    //   2: Pages
    //   3: FlateDecode stream containing certDer
    //   4: DSS dict  (/Certs [3 0 R]  /VRI << >>)
    // -------
    private static byte[] BuildFlateCertPdf(byte[] certDer)
    {
        byte[] compressed = ZlibCompress(certDer);

        using var ms = new MemoryStream();
        void WriteStr(string s) { var b = Latin1Bytes(s); ms.Write(b, 0, b.Length); }
        void WriteBytes(byte[] b) => ms.Write(b, 0, b.Length);

        WriteStr("%PDF-1.5\n");

        var off = new int[5];

        // obj 1: Catalog
        off[1] = (int)ms.Position;
        WriteStr("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /DSS 4 0 R >>\nendobj\n");

        // obj 2: Pages
        off[2] = (int)ms.Position;
        WriteStr("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");

        // obj 3: FlateDecode stream with cert bytes
        off[3] = (int)ms.Position;
        WriteStr($"3 0 obj\n<< /Length {compressed.Length} /Filter /FlateDecode >>\nstream\n");
        WriteBytes(compressed);
        WriteStr("\nendstream\nendobj\n");

        // obj 4: DSS dict (/VRI needed so the fallback finder recognises the object)
        off[4] = (int)ms.Position;
        WriteStr("4 0 obj\n<< /Type /DSS /Certs [ 3 0 R ] /VRI << >> >>\nendobj\n");

        int xrefStart = (int)ms.Position;
        WriteStr("xref\n0 5\n");
        WriteStr("0000000000 65535 f \n");
        for (int i = 1; i <= 4; i++)
            WriteStr($"{off[i]:D10} 00000 n \n");
        WriteStr($"trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n{xrefStart}\n%%EOF\n");

        return ms.ToArray();
    }

    // -------
    // Builds a minimal PDF where cert object 5 lives inside an ObjStm (object 3).
    //   1: Catalog  (/DSS 4 0 R)
    //   2: Pages
    //   3: ObjStm  (contains object 5 = cert hex string)
    //   4: DSS dict  (/Certs [ 5 0 R ]  /VRI << >>)
    //   5: (defined inside ObjStm, not as a top-level "5 0 obj")
    // -------
    private static byte[] BuildObjStmCertPdf(byte[] certDer)
    {
        // The ObjStm header declares "N First" pairs: "5 0\n" means obj 5 starts at offset 0
        // within the data section (the section that begins at byte /First from stream start).
        string objStmHeader = "5 0\n"; // objNum=5, byteOffset=0
        int first = Latin1.GetByteCount(objStmHeader);

        // The data section: the object value for obj 5 is a hex string of certDer.
        string hexStr = BitConverter.ToString(certDer).Replace("-", string.Empty).ToLowerInvariant();
        string certObjValue = $"<{hexStr}>";
        string stmContent = objStmHeader + certObjValue;

        byte[] stmBytes    = Latin1Bytes(stmContent);
        byte[] compressed  = ZlibCompress(stmBytes);

        using var ms = new MemoryStream();
        void WriteStr(string s) { var b = Latin1Bytes(s); ms.Write(b, 0, b.Length); }
        void WriteBytes(byte[] b) => ms.Write(b, 0, b.Length);

        WriteStr("%PDF-1.5\n");
        var off = new int[5];

        // obj 1: Catalog
        off[1] = (int)ms.Position;
        WriteStr("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /DSS 4 0 R >>\nendobj\n");

        // obj 2: Pages
        off[2] = (int)ms.Position;
        WriteStr("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");

        // obj 3: ObjStm containing obj 5
        off[3] = (int)ms.Position;
        WriteStr($"3 0 obj\n<< /Type /ObjStm /N 1 /First {first} /Length {compressed.Length} /Filter /FlateDecode >>\nstream\n");
        WriteBytes(compressed);
        WriteStr("\nendstream\nendobj\n");

        // obj 4: DSS dict
        off[4] = (int)ms.Position;
        WriteStr("4 0 obj\n<< /Type /DSS /Certs [ 5 0 R ] /VRI << >> >>\nendobj\n");

        int xrefStart = (int)ms.Position;
        WriteStr("xref\n0 5\n");
        WriteStr("0000000000 65535 f \n");
        for (int i = 1; i <= 4; i++)
            WriteStr($"{off[i]:D10} 00000 n \n");
        WriteStr($"trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n{xrefStart}\n%%EOF\n");

        return ms.ToArray();
    }

    // -------
    // Builds a PDF where the cert FlateDecode stream uses an indirect /Length N 0 R.
    // Some PDF generators write /Length as an indirect object to allow updating the
    // value without rewriting the full stream dictionary.  This exercises ResolveLength.
    //   1: Catalog  (/DSS 5 0 R)
    //   2: Pages
    //   3: Length object  (integer value for /Length of obj 4)
    //   4: FlateDecode cert stream  (/Length 3 0 R)
    //   5: DSS dict  (/Certs [ 4 0 R ]  /VRI << >>)
    // -------
    private static byte[] BuildIndirectLengthPdf(byte[] certDer)
    {
        byte[] compressed = ZlibCompress(certDer);

        using var ms = new MemoryStream();
        void WriteStr(string s) { var b = Latin1Bytes(s); ms.Write(b, 0, b.Length); }
        void WriteBytes(byte[] b) => ms.Write(b, 0, b.Length);

        WriteStr("%PDF-1.5\n");
        var off = new int[6];

        // obj 1: Catalog
        off[1] = (int)ms.Position;
        WriteStr("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /DSS 5 0 R >>\nendobj\n");

        // obj 2: Pages
        off[2] = (int)ms.Position;
        WriteStr("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");

        // obj 3: integer length object
        off[3] = (int)ms.Position;
        WriteStr($"3 0 obj\n{compressed.Length}\nendobj\n");

        // obj 4: FlateDecode cert stream with /Length 3 0 R (indirect)
        off[4] = (int)ms.Position;
        WriteStr("4 0 obj\n<< /Length 3 0 R /Filter /FlateDecode >>\nstream\n");
        WriteBytes(compressed);
        WriteStr("\nendstream\nendobj\n");

        // obj 5: DSS dict
        off[5] = (int)ms.Position;
        WriteStr("5 0 obj\n<< /Type /DSS /Certs [ 4 0 R ] /VRI << >> >>\nendobj\n");

        int xrefStart = (int)ms.Position;
        WriteStr("xref\n0 6\n");
        WriteStr("0000000000 65535 f \n");
        for (int i = 1; i <= 5; i++)
            WriteStr($"{off[i]:D10} 00000 n \n");
        WriteStr($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xrefStart}\n%%EOF\n");

        return ms.ToArray();
    }

    // -------
    // Returns a minimal DER-encoded X.509 certificate as byte[] via the BCL.
    // -------
    private static byte[] CreateTestCertDer()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=DssReaderTest", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return cert.RawData;
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Read_FlateDecodeCert_CertExtracted()
    {
        byte[] certDer = CreateTestCertDer();
        byte[] pdf     = BuildFlateCertPdf(certDer);

        DssData dss = PdfDssReader.Read(pdf);

        dss.Certs.Should().HaveCount(1, "one cert object in the DSS /Certs array");
        dss.Certs[0].GetEncoded().Should().Equal(certDer,
            "the cert bytes must survive FlateDecode decompression unchanged");
    }

    [Fact]
    public void Read_ObjStmCert_CertExtracted()
    {
        byte[] certDer = CreateTestCertDer();
        byte[] pdf     = BuildObjStmCertPdf(certDer);

        DssData dss = PdfDssReader.Read(pdf);

        dss.Certs.Should().HaveCount(1, "cert object resides in an ObjStm");
        dss.Certs[0].GetEncoded().Should().Equal(certDer,
            "the cert bytes must be correctly extracted from the ObjStm");
    }

    [Fact]
    public void Read_IndirectLengthRef_CertExtracted()
    {
        // Some PDF generators write "/Length N 0 R" (indirect reference) in stream dicts
        // rather than a literal integer.  This exercises ResolveLength() which dereferences
        // the object to get the actual byte count.
        byte[] certDer = CreateTestCertDer();
        byte[] pdf     = BuildIndirectLengthPdf(certDer);

        DssData dss = PdfDssReader.Read(pdf);

        dss.Certs.Should().HaveCount(1, "cert must be found via indirect /Length reference");
        dss.Certs[0].GetEncoded().Should().Equal(certDer,
            "cert bytes must survive decompression through indirect /Length");
    }

    [Fact]
    public void Read_EmptyPdf_ReturnsEmptyDss()
    {
        DssData dss = PdfDssReader.Read(Array.Empty<byte>());
        dss.Certs.Should().BeEmpty();
        dss.OcspResponses.Should().BeEmpty();
        dss.Crls.Should().BeEmpty();
    }

    [Fact]
    public void Read_PdfWithNoDss_ReturnsEmptyDss()
    {
        // A PDF that contains no DSS dictionary at all.
        byte[] pdf = Latin1Bytes("%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\nxref\n0 2\n0000000000 65535 f \n0000000009 00000 n \ntrailer\n<< /Size 2 /Root 1 0 R >>\nstartxref\n0\n%%EOF\n");
        DssData dss = PdfDssReader.Read(pdf);
        dss.Certs.Should().BeEmpty();
    }
}
