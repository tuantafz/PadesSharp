# PadesSharp

## Project Vision

PadesSharp is a modern enterprise-grade PDF signing and validation library for .NET.

Main goals:
- Fork and modernize iTextSharp 4.1.6 LGPL base
- Build advanced PAdES signing capabilities
- Support PKCS#11 / HSM / SmartCA
- Support TSA / OCSP / CRL / LTV
- Production-ready for enterprise trust services
- Cross-platform (.NET 8, Linux, Windows, macOS)
- High-performance PDF signing engine

Target architecture:
- PadesSharp.Core
- PadesSharp.Signing
- PadesSharp.Pades
- PadesSharp.Validation
- PadesSharp.Rendering
- PadesSharp.Hsm
- PadesSharp.Tools

---

# Kế hoạch nâng cấp fork iTextSharp 4.1.6 thành PDF Signing Toolkit hiện đại

## 1. Mục tiêu

Xây dựng một fork từ iTextSharp 4.1.6 theo hướng hiện đại hóa, phục vụ các nghiệp vụ ký số PDF chuyên nghiệp, nhưng không copy/port code từ iText 5/iText 7 hoặc các thư viện AGPL/commercial khác.

Mục tiêu chính:

1. Hỗ trợ ký SHA256/SHA384/SHA512.
2. Hỗ trợ Detached CMS/CAdES signing.
3. Hỗ trợ TSA client.
4. Hỗ trợ OCSP/CRL client.
5. Hỗ trợ DSS/VRI để tạo PDF LTV.
6. Hỗ trợ visible signature appearance.
7. Hỗ trợ PKCS#11/HSM signing adapter.
8. Hỗ trợ PDF validation cơ bản.
9. Tối ưu incremental update.
10. Hỗ trợ .NET 8/Linux.

## 2. Nguyên tắc pháp lý và kỹ thuật

### 2.1. Không được làm

AI agent không được:

- Copy source code từ iText 5/iText 7.
- Dịch Java code từ iText 5/iText 7 sang C#.
- Giữ nguyên class structure, method flow hoặc internal algorithm từ iText bản mới.
- Dùng code từ repository AGPL/commercial nếu chưa được phê duyệt license.
- Dùng AI để “rewrite” code iText mới sang fork 4.1.6.

### 2.2. Được làm

AI agent được:

- Đọc ISO 32000, ETSI PAdES, RFC 5652, RFC 3161, RFC 6960, RFC 5280.
- Tự thiết kế lại module.
- Viết implementation mới dựa trên public standard.
- Dùng BouncyCastle C# nếu license được phê duyệt.
- Tạo abstraction layer riêng để không sửa sâu vào core iTextSharp 4.1.6.

### 2.3. Kiến trúc tổng thể

Không biến fork thành clone của iText mới. Thiết kế theo hướng:

```text
LegacyPdfCore/                 // fork iTextSharp 4.1.6
ModernPdfSigning/              // module ký số mới
ModernPdfPades/                // PAdES, DSS, VRI, LTV
ModernPdfCrypto/               // CMS, CAdES, TSA, OCSP, CRL
ModernPdfPkcs11/               // PKCS#11/HSM adapter
ModernPdfValidation/           // validate chữ ký, cert chain, revocation
ModernPdfAppearance/           // visible signature appearance
ModernPdfNet8/                 // compatibility layer .NET 8/Linux
ModernPdfTests/                // unit/integration tests
ModernPdfSamples/              // sample apps
```

## 3. Repository structure đề xuất

```text
src/
  LegacyPdfCore/
    iTextSharpFork.csproj

  ModernPdf.Abstractions/
    ModernPdf.Abstractions.csproj

  ModernPdf.Crypto/
    ModernPdf.Crypto.csproj

  ModernPdf.Signing/
    ModernPdf.Signing.csproj

  ModernPdf.Pades/
    ModernPdf.Pades.csproj

  ModernPdf.Pkcs11/
    ModernPdf.Pkcs11.csproj

  ModernPdf.Validation/
    ModernPdf.Validation.csproj

  ModernPdf.Appearance/
    ModernPdf.Appearance.csproj

  ModernPdf.Samples.Console/
    ModernPdf.Samples.Console.csproj

tests/
  ModernPdf.Tests.Unit/
  ModernPdf.Tests.Integration/
  ModernPdf.Tests.Compatibility/

docs/
  architecture.md
  signing-flow.md
  pades-ltv.md
  pkcs11-hsm.md
  validation.md
  license-provenance.md
```

## 4. Coding rules cho AI agent

1. Mỗi module phải có interface trước, implementation sau.
2. Không viết logic crypto trực tiếp trong PDF layer.
3. Không để PDF layer phụ thuộc trực tiếp vào PKCS#11.
4. Không để TSA/OCSP/CRL client phụ thuộc vào PDF classes.
5. Mọi file mới phải có header ghi rõ: `Original implementation based on public standards, no code copied from iText 5/7`.
6. Mỗi feature phải có unit test và integration test.
7. Mỗi public API phải có XML doc comment.
8. Không dùng static mutable state.
9. Không reuse `PdfReader` giữa nhiều request ký.
10. Stream phải được dispose đúng.

## 5. Module 1 — SHA256/SHA384/SHA512 signing

### 5.1. Mục tiêu

Bổ sung thuật toán băm hiện đại cho luồng ký PDF:

- SHA256
- SHA384
- SHA512

### 5.2. Interface cần tạo

```csharp
public enum PdfDigestAlgorithm
{
    Sha256,
    Sha384,
    Sha512
}

public interface IDigestService
{
    string GetDigestOid(PdfDigestAlgorithm algorithm);
    byte[] ComputeDigest(Stream input, PdfDigestAlgorithm algorithm);
    byte[] ComputeDigest(byte[] input, PdfDigestAlgorithm algorithm);
}
```

### 5.3. Implementation

Tạo class:

```csharp
public sealed class DefaultDigestService : IDigestService
```

Yêu cầu:

- Dùng `System.Security.Cryptography.SHA256/SHA384/SHA512`.
- Không dùng SHA1 mặc định.
- Mapping OID:
  - SHA256: `2.16.840.1.101.3.4.2.1`
  - SHA384: `2.16.840.1.101.3.4.2.2`
  - SHA512: `2.16.840.1.101.3.4.2.3`

### 5.4. Acceptance criteria

- Có unit test cho từng thuật toán.
- Digest output khớp OpenSSL.
- Có test ký PDF bằng SHA256.
- Có test ký PDF bằng SHA384.
- Có test ký PDF bằng SHA512.
- Adobe Reader nhận diện đúng digest algorithm.

## 6. Module 2 — Detached CMS/CAdES signing

### 6.1. Mục tiêu

Tạo CMS detached signature cho PDF theo chuẩn CMS SignedData.

### 6.2. Interface cần tạo

```csharp
public interface ICmsSigner
{
    byte[] CreateDetachedSignature(CmsSigningRequest request);
}

public sealed class CmsSigningRequest
{
    public byte[] ContentDigest { get; set; }
    public X509Certificate2 SigningCertificate { get; set; }
    public IReadOnlyList<X509Certificate2> CertificateChain { get; set; }
    public PdfDigestAlgorithm DigestAlgorithm { get; set; }
    public ISignatureProvider SignatureProvider { get; set; }
    public DateTimeOffset SigningTime { get; set; }
    public bool IncludeSigningCertificateV2 { get; set; }
}
```

```csharp
public interface ISignatureProvider
{
    string SignatureAlgorithm { get; }
    byte[] SignDigest(byte[] digest, PdfDigestAlgorithm digestAlgorithm);
}
```

### 6.3. Implementation

Tạo class:

```csharp
public sealed class BouncyCastleCmsSigner : ICmsSigner
```

Yêu cầu:

- CMS phải là detached.
- Có signed attributes:
  - contentType
  - messageDigest
  - signingTime
  - signingCertificateV2 nếu bật CAdES-BES
- Hỗ trợ RSA PKCS#1 v1.5 trước.
- Thiết kế mở để sau này thêm RSA-PSS, ECDSA.

### 6.4. Acceptance criteria

- CMS verify được bằng OpenSSL.
- PDF ký mở được bằng Adobe Reader.
- Chữ ký detached, không embed content vào CMS.
- Có test CMS với cert chain 1 cấp.
- Có test CMS với cert chain nhiều cấp.
- Có negative test: thay đổi PDF sau ký thì verify fail.

## 7. Module 3 — TSA client

### 7.1. Mục tiêu

Gửi TimeStampReq tới TSA và nhận TimeStampToken theo RFC 3161.

### 7.2. Interface cần tạo

```csharp
public interface ITsaClient
{
    Task<TsaTokenResult> GetTimestampTokenAsync(byte[] imprint, PdfDigestAlgorithm digestAlgorithm, CancellationToken cancellationToken);
}

public sealed class TsaClientOptions
{
    public Uri Endpoint { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public int RetryCount { get; set; } = 3;
    public bool RequireNonce { get; set; } = true;
}

public sealed class TsaTokenResult
{
    public byte[] EncodedToken { get; set; }
    public DateTimeOffset GenTime { get; set; }
    public string PolicyOid { get; set; }
    public X509Certificate2 TsaCertificate { get; set; }
}
```

### 7.3. Implementation

Tạo class:

```csharp
public sealed class Rfc3161TsaClient : ITsaClient
```

Yêu cầu:

- HTTP POST content type: `application/timestamp-query`.
- Response content type: `application/timestamp-reply`.
- Validate nonce nếu request có nonce.
- Validate status granted/grantedWithMods.
- Có retry exponential backoff.
- Log rõ lỗi TSA timeout, HTTP error, invalid response, nonce mismatch.

### 7.4. Acceptance criteria

- Call được TSA test server.
- Token parse được.
- Token verify được với TSA certificate.
- Timeout hoạt động đúng.
- Retry hoạt động đúng.
- Có test nonce mismatch.

## 8. Module 4 — OCSP/CRL client

### 8.1. Mục tiêu

Lấy thông tin revocation cho certificate chain.

### 8.2. Interface cần tạo

```csharp
public interface IOcspClient
{
    Task<OcspResponseResult> GetOcspResponseAsync(X509Certificate2 certificate, X509Certificate2 issuerCertificate, CancellationToken cancellationToken);
}

public interface ICrlClient
{
    Task<CrlResult> DownloadCrlAsync(Uri crlUri, CancellationToken cancellationToken);
}

public interface IRevocationDataProvider
{
    Task<RevocationData> CollectAsync(IReadOnlyList<X509Certificate2> chain, CancellationToken cancellationToken);
}
```

```csharp
public sealed class RevocationData
{
    public IReadOnlyList<byte[]> OcspResponses { get; set; }
    public IReadOnlyList<byte[]> Crls { get; set; }
}
```

### 8.3. Implementation

Tạo các class:

```csharp
public sealed class DefaultOcspClient : IOcspClient
public sealed class DefaultCrlClient : ICrlClient
public sealed class DefaultRevocationDataProvider : IRevocationDataProvider
```

Yêu cầu:

- Đọc OCSP URL từ AIA extension.
- Đọc CRL URL từ CDP extension.
- Validate OCSP response status.
- Validate certificate status: good/revoked/unknown.
- Cache OCSP/CRL theo key certificate serial + issuer.
- Không fail toàn bộ ký nếu không lấy được OCSP, trừ khi policy yêu cầu strict.

### 8.4. Acceptance criteria

- Lấy được OCSP cho cert test.
- Lấy được CRL cho cert test.
- Detect revoked certificate.
- Detect unknown OCSP status.
- Có cache test.
- Có timeout test.

## 9. Module 5 — DSS/VRI cho LTV

### 9.1. Mục tiêu

Bổ sung DSS dictionary và VRI dictionary vào PDF để hỗ trợ Long-Term Validation.

### 9.2. Interface cần tạo

```csharp
public interface IPdfLtvService
{
    void AddLtvInformation(PdfLtvRequest request);
}

public sealed class PdfLtvRequest
{
    public Stream SignedPdfInput { get; set; }
    public Stream LtvPdfOutput { get; set; }
    public IReadOnlyList<PdfSignatureRevocationInfo> Signatures { get; set; }
}

public sealed class PdfSignatureRevocationInfo
{
    public string SignatureName { get; set; }
    public byte[] SignatureHash { get; set; }
    public IReadOnlyList<byte[]> OcspResponses { get; set; }
    public IReadOnlyList<byte[]> Crls { get; set; }
    public IReadOnlyList<byte[]> Certificates { get; set; }
}
```

### 9.3. PDF structure cần tạo

Trong Catalog:

```text
/DSS <<
  /Certs [cert streams]
  /OCSPs [ocsp streams]
  /CRLs [crl streams]
  /VRI <<
    /<signature-hash> <<
      /Cert [refs]
      /OCSP [refs]
      /CRL [refs]
    >>
  >>
>>
```

### 9.4. Implementation

Tạo class:

```csharp
public sealed class PdfDssWriter : IPdfLtvService
```

Yêu cầu:

- Ghi DSS bằng incremental update.
- Không phá chữ ký đã có.
- Deduplicate cert/OCSP/CRL streams.
- Tính VRI key từ signature value hash.
- Cho phép thêm LTV sau khi ký.

### 9.5. Acceptance criteria

- PDF sau khi thêm DSS vẫn giữ chữ ký valid.
- Adobe Reader hiển thị LTV hoặc có revocation info embedded.
- Có test với OCSP only.
- Có test với CRL only.
- Có test nhiều chữ ký trong cùng PDF.

## 10. Module 6 — Visible signature appearance

### 10.1. Mục tiêu

Tạo giao diện chữ ký hiển thị ổn định trên PDF.

### 10.2. Interface cần tạo

```csharp
public interface IPdfSignatureAppearanceBuilder
{
    PdfSignatureAppearanceResult Build(PdfSignatureAppearanceRequest request);
}

public sealed class PdfSignatureAppearanceRequest
{
    public string SignerName { get; set; }
    public string Reason { get; set; }
    public string Location { get; set; }
    public DateTimeOffset SigningTime { get; set; }
    public string CertificateSubject { get; set; }
    public byte[] LogoImage { get; set; }
    public PdfSignatureRectangle Rectangle { get; set; }
    public int PageNumber { get; set; }
    public bool ShowDate { get; set; }
    public bool ShowReason { get; set; }
    public bool ShowLocation { get; set; }
}

public sealed class PdfSignatureRectangle
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
}
```

### 10.3. Implementation

Tạo class:

```csharp
public sealed class DefaultPdfSignatureAppearanceBuilder : IPdfSignatureAppearanceBuilder
```

Yêu cầu:

- Hỗ trợ text-only signature.
- Hỗ trợ image + text.
- Hỗ trợ font Unicode.
- Hỗ trợ tiếng Việt.
- Hỗ trợ nhiều page size.
- Hỗ trợ rotate page 0/90/180/270.
- Có option scale image keep aspect ratio.

### 10.4. Acceptance criteria

- Hiển thị đúng trên A4 portrait.
- Hiển thị đúng trên A4 landscape.
- Hiển thị đúng trên page rotate 90.
- Hiển thị đúng tiếng Việt có dấu.
- Không vỡ layout khi signer name dài.

## 11. Module 7 — PKCS#11/HSM signing adapter

### 11.1. Mục tiêu

Tách signing operation ra khỏi PDF/CMS layer để hỗ trợ HSM/token qua PKCS#11.

### 11.2. Interface cần tạo

```csharp
public interface IPkcs11SessionFactory
{
    IPkcs11Session OpenSession(Pkcs11SessionRequest request);
}

public interface IPkcs11Session : IDisposable
{
    byte[] Sign(byte[] dataToSign, Pkcs11SignMechanism mechanism);
    X509Certificate2 GetCertificate(string certificateAlias);
}

public sealed class Pkcs11SessionRequest
{
    public string LibraryPath { get; set; }
    public string SlotId { get; set; }
    public string Pin { get; set; }
    public string KeyAlias { get; set; }
}

public enum Pkcs11SignMechanism
{
    RsaPkcs,
    Sha256RsaPkcs,
    Sha384RsaPkcs,
    Sha512RsaPkcs
}
```

### 11.3. Implementation

Tạo class:

```csharp
public sealed class Pkcs11SignatureProvider : ISignatureProvider
```

Yêu cầu:

- Support mechanism `CKM_RSA_PKCS` trước.
- Cho phép ký digestInfo hoặc ký raw digest tùy HSM/token.
- Có session pool.
- Có reconnect nếu session bị đóng.
- Không log PIN.
- Không giữ PIN plaintext lâu hơn cần thiết.
- Có timeout cho signing operation.

### 11.4. Acceptance criteria

- Ký được bằng SoftHSM trên Linux.
- Ký được bằng token thật trên Windows nếu có môi trường test.
- Ký được bằng HSM qua PKCS#11 nếu có môi trường test.
- Parallel signing 10 request không crash.
- Session reconnect hoạt động.
- PIN không xuất hiện trong log.

## 12. Module 8 — PDF validation

### 12.1. Mục tiêu

Validate chữ ký PDF ở mức phục vụ nghiệp vụ nội bộ.

### 12.2. Interface cần tạo

```csharp
public interface IPdfSignatureValidator
{
    PdfValidationReport Validate(Stream pdfInput, PdfValidationOptions options);
}

public sealed class PdfValidationOptions
{
    public bool ValidateCertificateChain { get; set; } = true;
    public bool ValidateRevocation { get; set; } = true;
    public bool ValidateTimestamp { get; set; } = true;
    public bool AllowUnknownRevocationStatus { get; set; } = false;
    public DateTimeOffset? ValidationTime { get; set; }
}

public sealed class PdfValidationReport
{
    public bool IsValid { get; set; }
    public IReadOnlyList<PdfSignatureValidationResult> Signatures { get; set; }
}

public sealed class PdfSignatureValidationResult
{
    public string SignatureName { get; set; }
    public bool DocumentIntegrityValid { get; set; }
    public bool CmsSignatureValid { get; set; }
    public bool CertificateChainValid { get; set; }
    public bool RevocationValid { get; set; }
    public bool TimestampValid { get; set; }
    public IReadOnlyList<string> Errors { get; set; }
    public IReadOnlyList<string> Warnings { get; set; }
}
```

### 12.3. Implementation

Tạo class:

```csharp
public sealed class DefaultPdfSignatureValidator : IPdfSignatureValidator
```

Yêu cầu:

- Extract signature fields.
- Check byte range.
- Verify CMS signature.
- Verify certificate chain.
- Verify OCSP/CRL nếu có.
- Verify timestamp token nếu có.
- Report rõ lỗi.

### 12.4. Acceptance criteria

- Validate PDF ký hợp lệ trả valid.
- PDF bị sửa sau ký trả invalid.
- Cert hết hạn trả invalid hoặc warning tùy policy.
- Cert revoked trả invalid.
- OCSP unavailable trả warning hoặc invalid tùy policy.
- Timestamp invalid trả invalid.

## 13. Module 9 — Incremental update optimization

### 13.1. Mục tiêu

Tối ưu ghi PDF incremental update để không phá chữ ký cũ và giảm memory usage.

### 13.2. Interface cần tạo

```csharp
public interface IPdfIncrementalUpdater
{
    void AppendUpdate(PdfIncrementalUpdateRequest request);
}

public sealed class PdfIncrementalUpdateRequest
{
    public Stream InputPdf { get; set; }
    public Stream OutputPdf { get; set; }
    public Action<IPdfObjectUpdateContext> UpdateAction { get; set; }
}
```

### 13.3. Implementation

Tạo class:

```csharp
public sealed class PdfIncrementalUpdater : IPdfIncrementalUpdater
```

Yêu cầu:

- Không load toàn bộ PDF vào memory nếu không cần.
- Copy original bytes sang output.
- Append object mới ở cuối file.
- Append xref/trailer mới.
- Cập nhật `/Prev` đúng.
- Không rewrite toàn bộ file khi thêm DSS/LTV.

### 13.4. Acceptance criteria

- File lớn 500MB vẫn xử lý không OOM.
- Thêm signature không phá signature cũ.
- Thêm DSS không phá signature cũ.
- Output mở được bằng Adobe Reader.
- Xref chain hợp lệ.

## 14. Module 10 — .NET 8/Linux compatibility

### 14.1. Mục tiêu

Đưa fork chạy tốt trên:

- .NET Framework 4.8
- .NET Standard 2.0
- .NET 8
- Windows
- Ubuntu 22.04/24.04
- Docker Linux container

### 14.2. Project target đề xuất

```xml
<TargetFrameworks>net48;netstandard2.0;net8.0</TargetFrameworks>
```

### 14.3. Việc cần làm

- Loại bỏ dependency Windows-only nếu có.
- Thay file path hard-code bằng abstraction.
- Kiểm tra font handling trên Linux.
- Kiểm tra encoding tiếng Việt.
- Kiểm tra stream/file lock.
- Kiểm tra native PKCS#11 library path trên Linux.
- Bổ sung Dockerfile test.
- Bổ sung GitHub Actions/GitLab CI.

### 14.4. Acceptance criteria

- Build pass trên Windows.
- Build pass trên Ubuntu.
- Unit test pass trên .NET 8.
- Ký PDF trong Docker Linux thành công.
- SoftHSM integration test pass trên Linux.
- Visible signature tiếng Việt hiển thị đúng trên Linux.

## 15. Public API cuối cùng mong muốn

### 15.1. Ký PDF đơn giản

```csharp
var signer = serviceProvider.GetRequiredService<IPdfSigner>();

await signer.SignAsync(new PdfSignRequest
{
    InputPdf = File.OpenRead("input.pdf"),
    OutputPdf = File.Create("signed.pdf"),
    DigestAlgorithm = PdfDigestAlgorithm.Sha256,
    SignatureProvider = pkcs11SignatureProvider,
    Certificate = signingCert,
    CertificateChain = chain,
    Appearance = appearance,
    TsaClient = tsaClient,
    EnableLtv = true
});
```

### 15.2. Interface chính

```csharp
public interface IPdfSigner
{
    Task<PdfSignResult> SignAsync(PdfSignRequest request, CancellationToken cancellationToken = default);
}

public sealed class PdfSignRequest
{
    public Stream InputPdf { get; set; }
    public Stream OutputPdf { get; set; }
    public PdfDigestAlgorithm DigestAlgorithm { get; set; } = PdfDigestAlgorithm.Sha256;
    public ISignatureProvider SignatureProvider { get; set; }
    public X509Certificate2 Certificate { get; set; }
    public IReadOnlyList<X509Certificate2> CertificateChain { get; set; }
    public PdfSignatureAppearanceRequest Appearance { get; set; }
    public ITsaClient TsaClient { get; set; }
    public IRevocationDataProvider RevocationDataProvider { get; set; }
    public bool EnableLtv { get; set; }
    public string Reason { get; set; }
    public string Location { get; set; }
}

public sealed class PdfSignResult
{
    public bool Success { get; set; }
    public string SignatureName { get; set; }
    public byte[] SignatureValueHash { get; set; }
    public IReadOnlyList<string> Warnings { get; set; }
}
```

## 16. Signing flow chuẩn

```text
1. Open input PDF.
2. Create signature field if not exists.
3. Reserve /Contents placeholder.
4. Calculate ByteRange.
5. Hash ByteRange content with SHA256/SHA384/SHA512.
6. Build signed attributes.
7. Sign signed attributes through ISignatureProvider.
8. Build detached CMS/CAdES.
9. Add timestamp token if TSA enabled.
10. Inject CMS into /Contents.
11. Close incremental update.
12. If LTV enabled:
    12.1 Collect OCSP/CRL.
    12.2 Append DSS/VRI as second incremental update.
13. Return result.
```

## 17. Sprint plan

> **Cập nhật:** 18/05/2026 — Tổng: **177/177 tests pass**

| Sprint | Tên | Trạng thái | Tests |
|--------|-----|-----------|-------|
| 1 | Foundation | ✅ Hoàn thành | 3/3 |
| 2 | Digest + CMS detached | ✅ Hoàn thành | 27/27 |
| 3 | PDF signing integration | ✅ Hoàn thành | 39/39 |
| 4 | Visible signature | ✅ Hoàn thành | 56/56 |
| 5 | TSA / RFC 3161 | ✅ Hoàn thành | 70/70 |
| 6 | OCSP/CRL | ✅ Hoàn thành | 87/87 |
| 7 | DSS/VRI/LTV | ✅ Hoàn thành | 109/109 |
| 8 | PKCS#11/HSM | ✅ Hoàn thành | 136/136 |
| 9 | Validation | ✅ Hoàn thành | 160/160 |
| 10 | Performance + .NET 8/Linux | ✅ Hoàn thành | 177/177 |

---

### Sprint 1 — Foundation ✅

Tasks:

- Tạo repository structure.
- Import iTextSharp 4.1.6 fork vào `LegacyPdfCore`.
- Tạo abstraction projects.
- Setup build multi-target net48/netstandard2.0/net8.0.
- Setup unit test project.
- Setup coding rules.
- Setup license provenance document.

Deliverables:

- Build pass.
- Empty test pass.
- CI pipeline chạy được.

### Sprint 2 — Digest + CMS detached ✅

Tasks:

- Implement `IDigestService`.
- Implement `ISignatureProvider` abstraction.
- Implement `ICmsSigner`.
- Implement RSA software signing provider for test.
- Add CMS verify tests.

Deliverables:

- Tạo được detached CMS.
- Verify được bằng OpenSSL/BouncyCastle.

### Sprint 3 — PDF signing integration ✅

Tasks:

- Tích hợp CMS vào PDF signature placeholder.
- Tính ByteRange.
- Inject `/Contents`.
- Test Adobe Reader compatibility.

Deliverables:

- Ký được PDF bằng SHA256/SHA384/SHA512.

### Sprint 4 — Visible signature ✅

Tasks:

- Implement appearance builder.
- Hỗ trợ text/image.
- Hỗ trợ font Unicode.
- Hỗ trợ page rotation.

Deliverables:

- PDF có chữ ký hiển thị đẹp, không lỗi tiếng Việt.

### Sprint 5 — TSA ✅

Tasks:

- Implement RFC3161 TSA client.
- Add timestamp token vào CMS unsigned attributes.
- Verify timestamp token.

Deliverables:

- PDF có timestamp hợp lệ.

### Sprint 6 — OCSP/CRL ✅

Tasks:

- Implement OCSP client.
- Implement CRL downloader.
- Implement revocation data provider.
- Add cache.

Deliverables:

- Thu thập được revocation data cho certificate chain.

### Sprint 7 — DSS/VRI/LTV ✅

Tasks:

- Implement DSS writer.
- Implement VRI dictionary.
- Add LTV after signing.
- Test multiple signatures.

Deliverables:

- PDF có DSS/VRI.
- Chữ ký cũ không bị phá.

### Sprint 8 — PKCS#11/HSM ✅

Tasks:

- Implement PKCS#11 session abstraction.
- Implement SoftHSM test adapter.
- Implement session pool.
- Implement reconnect.
- Implement `Pkcs11SignatureProvider`.

Deliverables:

- Ký PDF bằng SoftHSM trên Linux.
- Ký parallel ổn định.

### Sprint 9 — Validation ❌

Tasks:

- Extract signatures.
- Verify ByteRange.
- Verify CMS.
- Verify certificate chain.
- Verify OCSP/CRL.
- Verify timestamp.

Deliverables:

- Trả validation report chi tiết.

### Sprint 10 — Performance + .NET 8/Linux ❌

Tasks:

- Optimize incremental update.
- Test file lớn.
- Test Docker Linux.
- Test font Linux.
- Fix stream/file lock.

Deliverables:

- Build và test pass trên Windows/Linux.
- Ký file lớn không OOM.

## 18. Test matrix

| Nhóm test | Test case |
|---|---|
| Digest | SHA256/SHA384/SHA512 khớp OpenSSL |
| CMS | Detached CMS verify pass |
| PDF Signing | Adobe Reader verify pass |
| Appearance | Text/image/Unicode/rotation |
| TSA | Timestamp valid/invalid/timeout |
| OCSP | good/revoked/unknown |
| CRL | download/cache/expired CRL |
| LTV | DSS/VRI valid, không phá chữ ký |
| PKCS#11 | SoftHSM, session pool, reconnect |
| Validation | valid/tampered/revoked/expired |
| Performance | 100MB/500MB PDF |
| Compatibility | Windows/Linux/.NET 8 |

## 19. Definition of Done cho từng feature

Một feature chỉ được coi là hoàn thành khi:

1. Có interface rõ ràng.
2. Có implementation.
3. Có unit test.
4. Có integration test nếu liên quan PDF/crypto/network.
5. Có sample code.
6. Có document mô tả.
7. Không copy code từ iText 5/7.
8. Không làm fail CI.
9. Không phá backward compatibility của iTextSharp 4.1.6 nếu không có lý do.

## 20. Prompt mẫu cho AI coding agent

### 20.1. Prompt tạo module digest

```text
You are coding inside a fork of iTextSharp 4.1.6. Do not copy or port code from iText 5, iText 7, or any AGPL/commercial source.

Implement the ModernPdf.Crypto digest module based on the design in docs/iTextPdf.md.

Requirements:
- Create PdfDigestAlgorithm enum.
- Create IDigestService interface.
- Create DefaultDigestService implementation.
- Support SHA256, SHA384, SHA512.
- Return correct digest OIDs.
- Add unit tests comparing known hash vectors.
- Use System.Security.Cryptography.
- Add XML comments for public APIs.
```

### 20.2. Prompt tạo CMS detached

```text
You are coding a clean-room CMS detached signing module. Do not copy from iText 5/iText 7.

Implement ICmsSigner and BouncyCastleCmsSigner based on RFC 5652 and the design in docs/iTextPdf.md.

Requirements:
- Create detached SignedData.
- Include signed attributes: contentType, messageDigest, signingTime.
- Optionally include signingCertificateV2.
- Use ISignatureProvider for the actual private-key signing operation.
- Add tests verifying CMS signature with BouncyCastle.
- Add negative test for tampered content digest.
```

### 20.3. Prompt tạo TSA client

```text
Implement RFC3161 TSA client for ModernPdf.Crypto.

Requirements:
- Create ITsaClient, TsaClientOptions, TsaTokenResult.
- Send HTTP POST with application/timestamp-query.
- Parse application/timestamp-reply.
- Validate nonce.
- Validate status.
- Add retry with exponential backoff.
- Add timeout and cancellation token support.
- Add unit tests using mocked HTTP handler.
```

### 20.4. Prompt tạo OCSP/CRL

```text
Implement revocation clients for ModernPdf.Crypto.

Requirements:
- Create IOcspClient, ICrlClient, IRevocationDataProvider.
- Extract OCSP URL from AIA extension.
- Extract CRL URL from CDP extension.
- Download and parse OCSP/CRL.
- Return raw encoded OCSP/CRL bytes for LTV embedding.
- Add cache by certificate serial and issuer.
- Add tests for good, revoked, unknown, timeout.
```

### 20.5. Prompt tạo DSS/VRI

```text
Implement PDF DSS/VRI writer for LTV.

Requirements:
- Do not rewrite entire PDF.
- Append DSS dictionary using incremental update.
- Add Certs, OCSPs, CRLs, VRI dictionaries.
- Deduplicate binary streams.
- Do not break existing signatures.
- Add integration test: sign PDF, add LTV, verify original signature remains valid.
```

### 20.6. Prompt tạo PKCS#11 adapter

```text
Implement PKCS#11 signing adapter.

Requirements:
- Create IPkcs11SessionFactory and IPkcs11Session.
- Create Pkcs11SignatureProvider implementing ISignatureProvider.
- Support SoftHSM on Linux.
- Support CKM_RSA_PKCS first.
- Add session pool.
- Add reconnect logic.
- Never log PIN.
- Add integration test with SoftHSM in Docker.
```

## 21. Rủi ro chính

| Rủi ro | Mức độ | Cách giảm thiểu |
|---|---:|---|
| Dính license AGPL | Rất cao | Clean-room, không copy code iText mới |
| PDF signature không Adobe-compatible | Cao | Test Adobe Reader/Foxit thường xuyên |
| LTV sai DSS/VRI | Cao | Test nhiều PDF thực tế |
| PKCS#11 khác nhau theo HSM/token | Cao | Adapter theo vendor, test SoftHSM + token thật |
| Unicode font lỗi trên Linux | Trung bình | Embed font, test Docker |
| File lớn OOM | Trung bình | Incremental streaming |
| OCSP/TSA network không ổn định | Trung bình | Retry/cache/timeout |

## 22. Kết luận triển khai

Không nên cố biến iTextSharp 4.1.6 thành bản sao của iText 7. Hướng đúng là giữ 4.1.6 làm low-level PDF core, sau đó xây dựng một bộ module hiện đại xung quanh nó:

```text
iTextSharp 4.1.6 Fork
+ Modern CMS/CAdES
+ TSA
+ OCSP/CRL
+ DSS/VRI LTV
+ PKCS#11/HSM
+ Validation
+ .NET 8/Linux compatibility
```

Cách này giúp giảm rủi ro pháp lý, dễ maintain, dễ kiểm thử, và phù hợp với sản phẩm ký số thương mại.


---

# Reference Source Code

## iTextSharp 4.1.6 LGPL Forks / Mirrors

### LGPL Fork
- https://github.com/schourode/iTextSharp-LGPL

### Source Archive / Legacy Mirrors
- https://sourceforge.net/projects/itextsharp/
- https://www.nuget.org/packages/iTextSharp-LGPL/4.1.6

## Official iText
- https://github.com/itext/itext7
- https://itextpdf.com/

## Important Legal Notes

DO NOT:
- Copy/paste source code from iText5/iText7 into the LGPL fork
- Port AGPL code directly
- Use AI-generated code that is clearly derived from AGPL source

Recommended approach:
- Read PDF/ETSI/RFC specifications
- Design independently
- Implement features using clean-room principles
