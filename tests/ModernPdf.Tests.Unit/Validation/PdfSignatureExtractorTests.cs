using FluentAssertions;
using ModernPdf.Validation.Internal;
using iTextSharp.text;
using iTextSharp.text.pdf;

namespace ModernPdf.Tests.Unit.Validation;

public class PdfSignatureExtractorTests
{
    [Fact]
    public void Extract_SignatureLikeTokensInsideContentStream_DoesNotCreateSignature()
    {
        using var output = new MemoryStream();
        var document = new Document();
        try
        {
            PdfWriter writer = PdfWriter.GetInstance(document, output);
            document.Open();
            document.Add(new Paragraph("Unsigned PDF"));
            writer.DirectContent.SetLiteral(
                "\n<< /Type /Sig /ByteRange [0 1 2 3] /Contents <3000> >>\n");
        }
        finally
        {
            document.Close();
        }

        var signatures = PdfSignatureExtractor.Extract(output.ToArray());

        signatures.Should().BeEmpty(
            "signature-like bytes in a page content stream are not AcroForm signatures");
    }
}
