// Sample 01 — Basic PDF signing with a software RSA key.
// Creates a self-signed certificate in memory, signs a PDF, writes the output to disk.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ModernPdf.Abstractions.Crypto;
using ModernPdf.Abstractions.Signing;
using ModernPdf.Crypto;
using ModernPdf.Signing;

Console.WriteLine("=== PadesSharp — Sample 01: Basic Signing ===");

// ── 1. Create a self-signed RSA certificate (demo only; use a CA-issued cert in production) ──
using var rsa = RSA.Create(2048);
var certRequest = new CertificateRequest(
    "CN=Demo Signer,O=PadesSharp,C=VN",
    rsa,
    HashAlgorithmName.SHA256,
    RSASignaturePadding.Pkcs1);
certRequest.CertificateExtensions.Add(
    new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
using var cert = certRequest.CreateSelfSigned(
    DateTimeOffset.UtcNow.AddDays(-1),
    DateTimeOffset.UtcNow.AddYears(2));

Console.WriteLine($"Cert Subject  : {cert.Subject}");
Console.WriteLine($"Cert NotBefore: {cert.NotBefore:u}");
Console.WriteLine($"Cert NotAfter : {cert.NotAfter:u}");

// ── 2. Initialise the signing engine ────────────────────────────────────────────
using var provider    = new RsaSoftwareSignatureProvider(cert);
var digestService     = new DefaultDigestService();
var cmsSigner         = new BouncyCastleCmsSigner(digestService);
var engine            = new PdfSigningEngine(cmsSigner, digestService);

// ── 3. Build the sign request ────────────────────────────────────────────────
string outputPath = Path.Combine(AppContext.BaseDirectory, "sample01_signed.pdf");

using var output = File.Create(outputPath);
var signRequest = new PdfSignRequest
{
    OutputPdf            = output,
    Certificate          = cert,
    SignatureProvider    = provider,
    DigestAlgorithm      = PdfDigestAlgorithm.Sha256,
    Reason               = "Contract approval",
    Location             = "Hanoi, Vietnam",
    SignatureName        = "Signature1",
    SignatureContentSize = 8192,     // enough for a CMS signature without TSA
};

// ── 4. Sign ──────────────────────────────────────────────────────────────────
var result = await engine.SignAsync(signRequest);

Console.WriteLine();
Console.WriteLine($"Signed OK      : {result.Success}");
Console.WriteLine($"Signature name : {result.SignatureName}");
Console.WriteLine($"ByteRange      : [{string.Join(", ", result.ByteRange)}]");
Console.WriteLine($"Output size    : {new FileInfo(outputPath).Length:N0} bytes");
Console.WriteLine($"Output file    : {outputPath}");
Console.WriteLine();
Console.WriteLine("Open the file in Adobe Acrobat / Foxit to verify the signature.");
