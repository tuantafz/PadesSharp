// Sample 03 — PAdES-T: Add an RFC 3161 timestamp (TSA).
//
// To use a real TSA, set TSA_URL to the endpoint of your choice, e.g.:
//   Sectigo free TSA : http://timestamp.sectigo.com
//   Certum TSA       : http://time.certum.pl
//
// When TSA_URL is empty the sample falls back to a plain PAdES-B-B signature.

using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ModernPdf.Abstractions.Crypto;
using ModernPdf.Abstractions.Signing;
using ModernPdf.Abstractions.Tsa;
using ModernPdf.Crypto;
using ModernPdf.Crypto.Tsa;
using ModernPdf.Signing;

Console.WriteLine("=== PadesSharp — Sample 03: TSA / PAdES-T ===");

// ── 1. TSA URL (replace with your TSA endpoint) ────────────────────────────────
const string TSA_URL = ""; // leave empty → sign without timestamp

// ── 2. Demo certificate ─────────────────────────────────────────────────────────
using var rsa = RSA.Create(2048);
var req = new CertificateRequest(
    "CN=TSA Demo Signer,O=PadesSharp,C=VN",
    rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
req.CertificateExtensions.Add(
    new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
using var cert     = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
using var provider = new RsaSoftwareSignatureProvider(cert);

// ── 3. Initialise engine (digestService is shared by both TSA client and signing engine) ──
var digestService = new DefaultDigestService();
var cmsSigner     = new BouncyCastleCmsSigner(digestService);
var engine        = new PdfSigningEngine(cmsSigner, digestService);

// ── 4. Initialise Rfc3161TsaClient (only when URL is provided) ─────────────────
Rfc3161TsaClient? tsaClient = null;
if (!string.IsNullOrEmpty(TSA_URL))
{
    var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    tsaClient = new Rfc3161TsaClient(
        new TsaClientOptions { TsaUrl = TSA_URL },
        httpClient,
        digestService);
    Console.WriteLine($"TSA URL: {TSA_URL}");
}
else
{
    Console.WriteLine("TSA_URL not configured — signing PAdES-B-B (no timestamp).");
    Console.WriteLine("To enable PAdES-T set TSA_URL = \"http://timestamp.sectigo.com\"");
}

// ── 5. Sign — when TsaClient != null the engine automatically embeds
//            id-aa-signatureTimeStampToken as a CMS SignerInfo signed attribute ──
string outPath = Path.Combine(AppContext.BaseDirectory, "sample03_signed.pdf");
using var outStream = File.Create(outPath);

var result = await engine.SignAsync(new PdfSignRequest
{
    OutputPdf            = outStream,
    Certificate          = cert,
    SignatureProvider    = provider,
    DigestAlgorithm      = PdfDigestAlgorithm.Sha256,
    TsaClient            = tsaClient,          // null → no timestamp
    Reason               = "PAdES-T demo",
    Location             = "Hanoi",
    SignatureName        = "Sig1",
    // When using a TSA, increase SignatureContentSize because the TST token is ~2-4 KB
    SignatureContentSize = tsaClient != null ? 16384 : 8192,
});

Console.WriteLine();
Console.WriteLine($"Signed OK    : {result.Success}");
Console.WriteLine($"ByteRange    : [{string.Join(", ", result.ByteRange)}]");
Console.WriteLine($"Output size  : {new FileInfo(outPath).Length:N0} bytes");
Console.WriteLine($"Output file  : {outPath}");

if (tsaClient != null)
{
    Console.WriteLine();
    Console.WriteLine("Signature contains an RFC 3161 timestamp (PAdES-B-T).");
    Console.WriteLine("Open in Adobe Acrobat → Properties → Signatures to inspect.");
}
