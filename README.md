# PadesSharp

Preview-stage PDF digital signature library for .NET.  

> **Preview notice:** The API and validation behavior may change before v1.0. Review
> [SECURITY.md](SECURITY.md) and the documented validation limitations before using
> this library for production or compliance-sensitive workflows.
Modern signing modules built from scratch (ISO 32000, RFC 5652, ETSI); LegacyPdfCore is a multi-target fork of iTextSharp 4.1.6 LGPL — no code copied from iText 5/7 or any AGPL/commercial source.

---

## Tính năng

| Tính năng | Chuẩn | Module |
|---|---|---|
| Ký PDF với SHA-256 / SHA-384 / SHA-512 | ISO 32000-1 §12.8 | `ModernPdf.Signing` |
| CMS detached signature (PKCS#7 / CAdES-BES) | RFC 5652, ETSI EN 319 122 | `ModernPdf.Crypto` |
| Visible signature appearance (text, logo, Unicode) | ISO 32000-1 §12.5.6 | `ModernPdf.Appearance` |
| RFC 3161 TSA client (PAdES-T) | RFC 3161 | `ModernPdf.Crypto` |
| OCSP client (RFC 6960) | RFC 6960 | `ModernPdf.Crypto` |
| CRL client (RFC 5280) | RFC 5280 | `ModernPdf.Crypto` |
| DSS / VRI — PDF LTV (PAdES-LTV) | ETSI EN 319 102-1 | `ModernPdf.Pades` |
| PKCS#11 / HSM signing adapter (session pool, reconnect) | PKCS#11 v2.40 | `ModernPdf.Pkcs11` |
| PDF signature validation (integrity, cert, timestamp) | ISO 32000-1, ETSI | `ModernPdf.Validation` |
| File-based signing / validation helpers | — | `ModernPdf.Signing`, `ModernPdf.Validation` |
| Multi-threaded / concurrent signing | — | toàn bộ |

---

## Yêu cầu

- **.NET SDK** 9.0.201+ (xem `global.json`)
- **Target frameworks**: `net48` · `netstandard2.0` · `net8.0`
- Không yêu cầu cài đặt Adobe Acrobat hay bất kỳ native dependency nào ngoài PKCS#11 driver khi dùng HSM

---

## Cài đặt

> Chưa publish NuGet — dùng project reference hoặc clone trực tiếp.

```bash
git clone https://github.com/tuantafz/PadesSharp.git
cd PadesSharp
dotnet build
```

---

## Bắt đầu nhanh

### 1. Ký PDF bằng RSA software key

```csharp
using ModernPdf.Abstractions.Crypto;
using ModernPdf.Abstractions.Signing;
using ModernPdf.Crypto;
using ModernPdf.Signing;

// Chuẩn bị chứng thư và signing provider
using var cert = new X509Certificate2("signer.p12", "password",
    X509KeyStorageFlags.Exportable);
using var provider = new RsaSoftwareSignatureProvider(cert);

// Khởi tạo engine
var digestService = new DefaultDigestService();
var cmsSigner     = new BouncyCastleCmsSigner(digestService);
var engine        = new PdfSigningEngine(cmsSigner, digestService);

// Ký
using var output = File.Create("signed.pdf");
var result = await engine.SignAsync(new PdfSignRequest
{
    OutputPdf         = output,
    Certificate       = cert,
    SignatureProvider = provider,
    DigestAlgorithm   = PdfDigestAlgorithm.Sha256,
    Reason            = "Approved",
    Location          = "Hà Nội",
    SignatureName     = "Sig1",
});
```

### 2. Ký vào file (helper)

```csharp
await PdfSigningFileHelper.SignToFileAsync(
    outputPath: "signed.pdf",
    request: new PdfSignRequest
    {
        Certificate       = cert,
        SignatureProvider = provider,
        DigestAlgorithm   = PdfDigestAlgorithm.Sha256,
    },
    signer: engine);
```

### 3. Thêm timestamp (PAdES-T)

```csharp
using ModernPdf.Crypto.Tsa;

var tsaClient = new Rfc3161TsaClient(
    new TsaClientOptions { Endpoint = new Uri("http://tsa.example.com") },
    httpClientFactory);

var result = await engine.SignAsync(new PdfSignRequest
{
    // ...
    TsaClient = tsaClient,
});
```

### 4. LTV — Thêm DSS / VRI sau khi ký

```csharp
using ModernPdf.Pades;
using ModernPdf.Abstractions.Dss;

var ltvCollector = new LtvDataCollector(ocspClient, crlClient);
var dssData      = await ltvCollector.CollectAsync(certChain, result.SignatureCmsBytes);

var dssWriter    = new DssIncrementalWriter();
byte[] ltvPdf    = dssWriter.AppendDss(File.ReadAllBytes("signed.pdf"), dssData);
File.WriteAllBytes("signed_ltv.pdf", ltvPdf);
```

### 5. Ký bằng PKCS#11 / HSM

```csharp
using ModernPdf.Pkcs11;
using ModernPdf.Abstractions.Pkcs11;

var sessionRequest = new Pkcs11SessionRequest
{
    LibraryPath  = "/usr/lib/softhsm/libsofthsm2.so",
    SlotId       = "0",
    Pin          = "1234",
    KeyAlias     = "signing-key",
};

var factory  = new DefaultPkcs11SessionFactory(sessionRequest.LibraryPath);
var pool     = new Pkcs11SessionPool(factory, sessionRequest);
var provider = new Pkcs11SignatureProvider(pool, cert, PdfDigestAlgorithm.Sha256, logger);

var result = await engine.SignAsync(new PdfSignRequest
{
    Certificate       = cert,
    SignatureProvider = provider,
    DigestAlgorithm   = PdfDigestAlgorithm.Sha256,
    // ...
});
```

### 6. Validate chữ ký PDF

```csharp
using ModernPdf.Validation;
using ModernPdf.Abstractions.Validation;

var validator = new DefaultPdfSignatureValidator();
var report    = validator.Validate(File.OpenRead("signed.pdf"));

Console.WriteLine($"Valid: {report.IsValid}");
foreach (var sig in report.Signatures)
{
    Console.WriteLine($"  [{sig.SignatureName}]");
    Console.WriteLine($"    DocumentIntegrity : {sig.DocumentIntegrityValid}");
    Console.WriteLine($"    CmsSignature      : {sig.CmsSignatureValid}");
    Console.WriteLine($"    CertChain         : {sig.CertificateChainValid}");
    Console.WriteLine($"    Timestamp         : {sig.TimestampValid}");
    foreach (var err in sig.Errors)
        Console.WriteLine($"    ⚠ {err}");
}
```

---

## Samples

Thư mục `samples/` chứa 6 console app độc lập, mỗi cái minh họa một use-case:

| Project | Mô tả | Chạy |
|---|---|---|
| [Sample01.BasicSigning](samples/Sample01.BasicSigning/) | Ký PDF bằng RSA software key (tự tạo cert trong bộ nhớ) | `dotnet run --project samples/Sample01.BasicSigning` |
| [Sample02.FileHelper](samples/Sample02.FileHelper/) | Ký ra file & validate từ file path với helper API | `dotnet run --project samples/Sample02.FileHelper` |
| [Sample03.Tsa](samples/Sample03.Tsa/) | PAdES-T — thêm timestamp RFC 3161 (TSA) | `dotnet run --project samples/Sample03.Tsa` |
| [Sample04.LtvDss](samples/Sample04.LtvDss/) | PAdES-LTV — ghi /DSS + /VRI incremental | `dotnet run --project samples/Sample04.LtvDss` |
| [Sample05.Pkcs11Hsm](samples/Sample05.Pkcs11Hsm/) | Ký bằng PKCS#11 / HSM (SoftHSM2) | `dotnet run --project samples/Sample05.Pkcs11Hsm` |
| [Sample06.Validation](samples/Sample06.Validation/) | Validate chữ ký + phát hiện giả mạo | `dotnet run --project samples/Sample06.Validation` |

> Sample01–04 và 06 chạy ngay không cần phần cứng. Sample05 cần SoftHSM2 hoặc HSM thật.

---

## Cấu trúc dự án

```
src/
  LegacyPdfCore/              iTextSharp 4.1.6 LGPL fork (PDF render/write base)
  ModernPdf.Abstractions/     Interfaces & DTOs (không dependency ngoài BCL)
  ModernPdf.Crypto/           CMS, CAdES, TSA, OCSP, CRL (BouncyCastle 2.4.0)
  ModernPdf.Signing/          PdfSigningEngine, PdfSigningFileHelper
  ModernPdf.Appearance/       Visible signature appearance (text, image, Unicode)
  ModernPdf.Pades/            LtvDataCollector, DssIncrementalWriter (DSS/VRI)
  ModernPdf.Pkcs11/           DefaultPkcs11SessionFactory, Pkcs11SessionPool,
                              Pkcs11SignatureProvider (Pkcs11Interop 5.2.0)
  ModernPdf.Validation/       DefaultPdfSignatureValidator, PdfValidationFileHelper

samples/
  Sample01.BasicSigning/      Ký cơ bản (RSA software key)
  Sample02.FileHelper/        Ký ra file & validate từ file
  Sample03.Tsa/               PAdES-T — timestamp RFC 3161
  Sample04.LtvDss/            PAdES-LTV — DSS/VRI incremental
  Sample05.Pkcs11Hsm/         PKCS#11 / HSM (SoftHSM2)
  Sample06.Validation/        Validate & phát hiện giả mạo

tests/
  ModernPdf.Tests.Unit/       Unit tests — xUnit 2.9.3, FluentAssertions 7.2.0
```

---

## Dependencies

| Package | Phiên bản | Mục đích |
|---|---|---|
| `LegacyPdfCore` source fork | iTextSharp 4.1.6 lineage | PDF render/write base (LGPL 2.1+) |
| `BouncyCastle.Cryptography` | 2.4.0 | ASN.1, CMS, TSP, OCSP, CRL, X.509 |
| `Pkcs11Interop` | 5.2.0 | PKCS#11 native interop |
| `Microsoft.Extensions.Logging.Abstractions` | 8.0.2 | Logging interface |
| `Microsoft.Extensions.Http` | 8.0.1 | TSA/OCSP HTTP client |

---

## Chạy tests

```bash
dotnet test tests/ModernPdf.Tests.Unit --logger "console;verbosity=minimal"
```

The command must finish with zero failed tests before publishing a release.

---

## Tiến độ

| Sprint | Tính năng | Tests |
|---|---|---|
| 1 | Foundation, project structure | 3 |
| 2 | SHA-256/384/512, CMS detached | 27 |
| 3 | PDF signing engine | 39 |
| 4 | Visible signature appearance | 56 |
| 5 | TSA / RFC 3161 | 70 |
| 6 | OCSP / CRL revocation | 87 |
| 7 | DSS / VRI / LTV | 109 |
| 8 | PKCS#11 / HSM session pool | 136 |
| 9 | PDF signature validation | 160 |
| 10 | Performance + file helpers + .NET 8 | **177** |

---

## Nguyên tắc phát triển

- **Không copy code** từ iText 5, iText 7 hoặc bất kỳ nguồn AGPL/commercial nào.
- Mọi implementation đều dựa trực tiếp trên chuẩn công khai: ISO 32000, ETSI EN 319 102, RFC 5652, RFC 3161, RFC 6960.
- Mỗi file source có header: `Original implementation based on public standards, no code copied from iText 5/7`.
- PIN và thông tin nhạy cảm không bao giờ xuất hiện trong log.

---

## License

**PadesSharp** — [GNU Lesser General Public License v2.1 or later](LICENSE)

- Toàn bộ mã nguồn được phát hành dưới LGPL 2.1+
- `LegacyPdfCore` dựa trên iTextSharp 4.1.6 (LGPL 2.1)
- `ModernPdf.*` modules là implementation mới, viết từ chuẩn công khai
- Xem [docs/license-provenance.md](docs/license-provenance.md) để biết chi tiết provenance
