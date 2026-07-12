// Sample 02 — File-path helpers: SignToFileAsync + ValidateFile.
// Signs a PDF directly to a file (no manual Stream management),
// then validates the signature from the file path.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ModernPdf.Abstractions.Crypto;
using ModernPdf.Abstractions.Signing;
using ModernPdf.Abstractions.Validation;
using ModernPdf.Crypto;
using ModernPdf.Signing;
using ModernPdf.Validation;

Console.WriteLine("=== PadesSharp — Sample 02: File Helper ===");

// ── 1. Demo certificate ─────────────────────────────────────────────────────────
using var rsa = RSA.Create(2048);
var req = new CertificateRequest(
    "CN=File Helper Demo,O=PadesSharp,C=VN",
    rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
req.CertificateExtensions.Add(
    new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
using var cert     = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
using var provider = new RsaSoftwareSignatureProvider(cert);

// ── 2. Initialise the engine ───────────────────────────────────────────────────
var digestService = new DefaultDigestService();
var cmsSigner     = new BouncyCastleCmsSigner(digestService);
var engine        = new PdfSigningEngine(cmsSigner, digestService);

// ── 3. Sign to file — output directory is created automatically if absent ──────
string outDir    = Path.Combine(AppContext.BaseDirectory, "output");
string pdfPath   = Path.Combine(outDir, "sample02_signed.pdf");

Console.WriteLine($"Signing to: {pdfPath}");

var result = await PdfSigningFileHelper.SignToFileAsync(
    outputPath: pdfPath,
    request: new PdfSignRequest
    {
        Certificate          = cert,
        SignatureProvider    = provider,
        DigestAlgorithm      = PdfDigestAlgorithm.Sha256,
        Reason               = "File helper demo",
        Location             = "Ho Chi Minh City",
        SignatureName        = "Sig1",
        SignatureContentSize = 8192,
    },
    signer: engine);

Console.WriteLine($"Signed OK      : {result.Success}");
Console.WriteLine($"Output size    : {new FileInfo(pdfPath).Length:N0} bytes");

// ── 4. Validate from file path ──────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("Validating from file...");

var validator = new DefaultPdfSignatureValidator();
var report    = PdfValidationFileHelper.ValidateFile(pdfPath, validator);

PrintReport(report);

// ── 5. Validate from byte array ──────────────────────────────────────────────
Console.WriteLine("Validating from byte[]...");
byte[] bytes   = File.ReadAllBytes(pdfPath);
var report2    = PdfValidationFileHelper.ValidateBytes(bytes, validator);
PrintReport(report2);

// ─────────────────────────────────────────────────────────────────────────────

static void PrintReport(PdfValidationReport report)
{
    Console.WriteLine($"  IsValid           : {report.IsValid}");
    foreach (var sig in report.Signatures)
    {
        Console.WriteLine($"  [{sig.SignatureName}]");
        Console.WriteLine($"    DocumentIntegrity : {sig.DocumentIntegrityValid}");
        Console.WriteLine($"    CmsSignature      : {sig.CmsSignatureValid}");
        Console.WriteLine($"    CertChain         : {sig.CertificateChainValid}");
        Console.WriteLine($"    Timestamp         : {sig.TimestampValid}");
        foreach (var e in sig.Errors)   Console.WriteLine($"    ✖ {e}");
        foreach (var w in sig.Warnings) Console.WriteLine($"    ⚠ {w}");
    }
    Console.WriteLine();
}
