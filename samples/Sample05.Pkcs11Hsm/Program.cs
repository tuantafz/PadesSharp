// Sample 05 — PKCS#11 / HSM signing.
//
// Prerequisites: install SoftHSM2 (or a real HSM) and initialise a token with an RSA key.
//
// SoftHSM2 quick-start on Windows:
//   winget install OpenSC.SoftHSM2
//   softhsm2-util --init-token --slot 0 --label "PadesSharp" --pin 1234 --so-pin 0000
//   pkcs11-tool --module softhsm2-x64.dll --slot 0 --login --pin 1234 \
//               --keypairgen --key-type rsa:2048 --label "SignKey" --id 01
//
// Then set the following environment variables (or edit the constants below):
//   PKCS11_LIB   — full path to libsofthsm2.dll / libsofthsm2.so
//   PKCS11_PIN   — user token PIN
//   PKCS11_SLOT  — slot ID string (default "0")
//   PKCS11_LABEL — CKA_LABEL of the key/cert object (default "SignKey")

using System.Security.Cryptography.X509Certificates;
using ModernPdf.Abstractions.Crypto;
using ModernPdf.Abstractions.Pkcs11;
using ModernPdf.Abstractions.Signing;
using ModernPdf.Crypto;
using ModernPdf.Pkcs11;
using ModernPdf.Signing;

Console.WriteLine("=== PadesSharp — Sample 05: PKCS#11 / HSM ===");

// ── 1. Read configuration from environment variables ────────────────────────
string? pkcs11Lib = Environment.GetEnvironmentVariable("PKCS11_LIB");
string? pin       = Environment.GetEnvironmentVariable("PKCS11_PIN") ?? "1234";
string  slotId    = Environment.GetEnvironmentVariable("PKCS11_SLOT") ?? "0";
string  keyLabel  = Environment.GetEnvironmentVariable("PKCS11_LABEL") ?? "SignKey";

// Auto-detect a default SoftHSM2 library when PKCS11_LIB is not set
if (string.IsNullOrEmpty(pkcs11Lib))
{
    string[] candidates =
    [
        @"C:\Program Files\SoftHSM2\lib\softhsm2-x64.dll",
        @"C:\Program Files (x86)\SoftHSM2\lib\softhsm2.dll",
        "/usr/lib/softhsm/libsofthsm2.so",
        "/usr/local/lib/softhsm/libsofthsm2.so",
        "/opt/homebrew/lib/softhsm/libsofthsm2.so",
    ];
    pkcs11Lib = Array.Find(candidates, File.Exists);
}

if (string.IsNullOrEmpty(pkcs11Lib) || !File.Exists(pkcs11Lib))
{
    Console.WriteLine("PKCS#11 library not found.");
    Console.WriteLine("Install SoftHSM2 or set environment variable PKCS11_LIB=<path>.");
    Console.WriteLine("Sample exiting early (no error).");
    return;
}

Console.WriteLine($"PKCS#11 library: {pkcs11Lib}");
Console.WriteLine($"Slot ID        : {slotId}");
Console.WriteLine($"Key label      : {keyLabel}");
Console.WriteLine();

// ── 2. Build Pkcs11SessionRequest ────────────────────────────────────────────
var sessionRequest = new Pkcs11SessionRequest
{
    LibraryPath      = pkcs11Lib,
    SlotId           = slotId,
    Pin              = pin,
    KeyAlias         = keyLabel,
    CertificateAlias = keyLabel,
    DigestAlgorithm  = PdfDigestAlgorithm.Sha256,
    SignMechanism    = Pkcs11SignMechanism.RsaPkcs,
    MaxSessions      = 2,
};

// ── 3. Retrieve the certificate from the token ───────────────────────────────
using var sessionFactory = new DefaultPkcs11SessionFactory(pkcs11Lib);

X509Certificate2 cert;
try
{
    using var session = sessionFactory.OpenSession(sessionRequest);
    cert = session.GetCertificate(keyLabel);
}
catch (Exception ex)
{
    Console.WriteLine($"HSM connection error: {ex.Message}");
    Console.WriteLine("Check the PIN, slot ID and key label on the token.");
    return;
}

Console.WriteLine($"Cert Subject: {cert.Subject}");
Console.WriteLine($"Cert Valid  : {cert.NotBefore:d} → {cert.NotAfter:d}");

// ── 4. Sign the PDF via the PKCS#11 provider ─────────────────────────────────
// Pkcs11SignatureProvider manages its own internal pool from sessionFactory + sessionRequest.
var digestService = new DefaultDigestService();
var cmsSigner     = new BouncyCastleCmsSigner(digestService);
var engine        = new PdfSigningEngine(cmsSigner, digestService);

using var pkcs11Provider = new Pkcs11SignatureProvider(sessionFactory, sessionRequest, cert);

string outPath = Path.Combine(AppContext.BaseDirectory, "sample05_hsm_signed.pdf");
using var outStream = File.Create(outPath);

var result = await engine.SignAsync(new PdfSignRequest
{
    OutputPdf            = outStream,
    Certificate          = cert,
    SignatureProvider    = pkcs11Provider,
    DigestAlgorithm      = PdfDigestAlgorithm.Sha256,
    Reason               = "HSM-signed document",
    Location             = "Hanoi",
    SignatureName        = "Sig1",
    SignatureContentSize = 12288,
});

Console.WriteLine();
Console.WriteLine($"Signed OK    : {result.Success}");
Console.WriteLine($"ByteRange    : [{string.Join(", ", result.ByteRange)}]");
Console.WriteLine($"Output file  : {outPath}");
Console.WriteLine();
Console.WriteLine("The private key never leaves the HSM — only the digest is sent in.");
