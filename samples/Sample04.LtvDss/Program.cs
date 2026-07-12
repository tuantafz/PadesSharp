// Sample 04 — DSS/VRI incremental append (PAdES-LTV).
//
// Workflow:
//   1. Sign the PDF (basic PAdES-B-B)
//   2. Collect OCSP / CRL revocation data for the certificate chain
//   3. Write /DSS + /VRI as an incremental update → output supports LTV
//
// No real OCSP/CRL server is available in this demo, so DssData will be empty.
// In production: supply a configured DefaultOcspClient or DefaultCrlClient.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ModernPdf.Abstractions.Crypto;
using ModernPdf.Abstractions.Dss;
using ModernPdf.Abstractions.Signing;
using ModernPdf.Crypto;
using ModernPdf.Pades;
using ModernPdf.Signing;

Console.WriteLine("=== PadesSharp — Sample 04: DSS/VRI / PAdES-LTV ===");

// ── 1. Demo certificate ─────────────────────────────────────────────────────────
using var rsa = RSA.Create(2048);
var req = new CertificateRequest(
    "CN=LTV Demo Signer,O=PadesSharp,C=VN",
    rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
req.CertificateExtensions.Add(
    new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
using var cert     = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
using var provider = new RsaSoftwareSignatureProvider(cert);

// ── 2. Basic PDF sign → byte[] ───────────────────────────────────────────────
var digestService = new DefaultDigestService();
var cmsSigner     = new BouncyCastleCmsSigner(digestService);
var engine        = new PdfSigningEngine(cmsSigner, digestService);

using var signedStream = new MemoryStream();
var signResult = await engine.SignAsync(new PdfSignRequest
{
    OutputPdf            = signedStream,
    Certificate          = cert,
    SignatureProvider    = provider,
    DigestAlgorithm      = PdfDigestAlgorithm.Sha256,
    Reason               = "LTV demo",
    Location             = "Hanoi",
    SignatureName        = "Sig1",
    SignatureContentSize = 8192,
});

byte[] signedPdfBytes = signedStream.ToArray();
Console.WriteLine($"Step 1 — Signed: {signedPdfBytes.Length:N0} bytes");

// ── 3. Collect revocation data ──────────────────────────────────────────────────
// Production: pass a configured DefaultOcspClient and/or DefaultCrlClient.
// Demo: no OCSP/CRL server available → collector returns empty DssData but
//       still embeds the certificate(s) in /DSS.
var collector = new LtvDataCollector(
    ocspClient: null,   // replace with: new DefaultOcspClient(httpClient)
    crlClient:  null);  // replace with: new DefaultCrlClient(httpClient)

// Certificate chain — demo uses a single self-signed cert
var chain = new List<System.Security.Cryptography.X509Certificates.X509Certificate2> { cert };
var dssData = await collector.CollectAsync(chain, signResult.SignatureCmsBytes);

Console.WriteLine($"Step 2 — LTV collected: {dssData.OcspResponses.Count} OCSP, " +
                  $"{dssData.Crls.Count} CRL, {dssData.Certificates.Count} cert");

// ── 4. Append /DSS incremental update ────────────────────────────────────────
var dssWriter    = new DssIncrementalWriter();
byte[] ltvBytes  = dssWriter.AppendDss(signedPdfBytes, dssData);

Console.WriteLine($"Step 3 — /DSS written: {ltvBytes.Length:N0} bytes");

// ── 5. Save output file ──────────────────────────────────────────────────────
string outPath = Path.Combine(AppContext.BaseDirectory, "sample04_ltv.pdf");
File.WriteAllBytes(outPath, ltvBytes);

Console.WriteLine();
Console.WriteLine($"Output: {outPath}");
Console.WriteLine($"Size delta (DSS overhead): {ltvBytes.Length - signedPdfBytes.Length:N0} bytes");
Console.WriteLine();
Console.WriteLine("LTV PDF is ready for PAdES-B-LT / PAdES-B-LTA.");
Console.WriteLine("For full LTV: supply real OCSP/CRL servers to LtvDataCollector.");
