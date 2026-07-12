// Sample 06 — Validate a PDF signature and detect tampering.
//
// Demo:
//   1. Sign a PDF normally → validation succeeds
//   2. Flip one byte in the PDF content → validation fails (document integrity)
//   3. Validate with various options: skip chain check, skip revocation, etc.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ModernPdf.Abstractions.Crypto;
using ModernPdf.Abstractions.Signing;
using ModernPdf.Abstractions.Validation;
using ModernPdf.Crypto;
using ModernPdf.Signing;
using ModernPdf.Validation;

Console.WriteLine("=== PadesSharp — Sample 06: Validation ===");
Console.WriteLine();

// ── 1. Certificate + engine ──────────────────────────────────────────────────
using var rsa = RSA.Create(2048);
var certReq = new CertificateRequest(
    "CN=Validation Demo,O=PadesSharp,C=VN",
    rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
certReq.CertificateExtensions.Add(
    new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
using var cert     = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
using var provider = new RsaSoftwareSignatureProvider(cert);

var digestService = new DefaultDigestService();
var cmsSigner     = new BouncyCastleCmsSigner(digestService);
var engine        = new PdfSigningEngine(cmsSigner, digestService);

// ── 2. Sign the PDF ──────────────────────────────────────────────────────────
using var signedStream = new MemoryStream();
await engine.SignAsync(new PdfSignRequest
{
    OutputPdf            = signedStream,
    Certificate          = cert,
    SignatureProvider    = provider,
    DigestAlgorithm      = PdfDigestAlgorithm.Sha256,
    Reason               = "Validation demo",
    Location             = "Hanoi",
    SignatureName        = "Sig1",
    SignatureContentSize = 8192,
});
byte[] signedPdf = signedStream.ToArray();
Console.WriteLine($"Signed: {signedPdf.Length:N0} bytes");

// ── 3. Validate the original (valid) signature ───────────────────────────────
var validator = new DefaultPdfSignatureValidator();

// Skip chain and revocation checks because the cert is self-signed (no CA / OCSP)
var options = new PdfValidationOptions
{
    ValidateCertificateChain = false,
    ValidateRevocation       = false,
    ValidateTimestamp        = false,
};

Console.WriteLine();
Console.WriteLine("--- Validate original signature (should be VALID) ---");
using var goodStream = new MemoryStream(signedPdf, writable: false);
var goodReport = validator.Validate(goodStream, options);
PrintReport(goodReport);

// ── 4. Tamper: flip one byte in the signed content ──────────────────────────
byte[] tamperedPdf = (byte[])signedPdf.Clone();
// The last byte is always in the second ByteRange segment (after /Contents),
// so it is covered by the signature. Flipping it guarantees a detectable tamper.
// NOTE: signedPdf.Length/4 would land inside the /Contents hex padding which is
// excluded from the ByteRange, making tampering there invisible to the validator.
tamperedPdf[tamperedPdf.Length - 1] ^= 0xFF;

Console.WriteLine("--- Validate tampered PDF (should be INVALID) ---");
using var tamperedStream = new MemoryStream(tamperedPdf, writable: false);
var tamperedReport = validator.Validate(tamperedStream, options);
PrintReport(tamperedReport);

// ── 5. Validate with full chain check (self-signed cert → expected to fail) ──
Console.WriteLine("--- Validate with full chain check (self-signed cert → INVALID expected) ---");
var strictOptions = new PdfValidationOptions
{
    ValidateCertificateChain = true,
    ValidateChainTrust       = true,   // build X.509 chain to system trust store
    ValidateRevocation       = false,  // no OCSP/CRL server in this demo
    AllowUnknownRevocationStatus = true,
};
using var strictStream = new MemoryStream(signedPdf, writable: false);
var strictReport = validator.Validate(strictStream, strictOptions);
PrintReport(strictReport);

Console.WriteLine("Done. See validation results above.");

// ─────────────────────────────────────────────────────────────────────────────
static void PrintReport(PdfValidationReport report)
{
    string status = report.IsValid ? "VALID ✔" : "INVALID ✘";
    Console.WriteLine($"  Result: {status}  (signatures: {report.Signatures.Count})");
    foreach (var sig in report.Signatures)
    {
        Console.WriteLine($"  [{sig.SignatureName}]");
        Console.WriteLine($"    DocumentIntegrity : {Result(sig.DocumentIntegrityValid)}");
        Console.WriteLine($"    CmsSignature      : {Result(sig.CmsSignatureValid)}");
        Console.WriteLine($"    CertChain         : {Result(sig.CertificateChainValid)}");
        Console.WriteLine($"    Timestamp         : {Result(sig.TimestampValid)}");
        Console.WriteLine($"    Overall Valid     : {Result(sig.IsValid)}");
        foreach (var e in sig.Errors)   Console.WriteLine($"    ✖ ERR: {e}");
        foreach (var w in sig.Warnings) Console.WriteLine($"    ⚠ WRN: {w}");
    }
    Console.WriteLine();

    static string Result(bool? v) => v switch
    {
        true  => "OK",
        false => "FAIL",
        null  => "skipped",
    };
}
