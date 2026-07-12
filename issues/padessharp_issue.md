# PadesSharp — Các vấn đề validation và kế hoạch xử lý

## 1. Kết quả kiểm tra implementation hiện tại

### 1.1. Signature extractor dùng text scan thay vì PDF parser

`PdfSignatureExtractor` chuyển toàn bộ PDF sang Latin-1 và tìm các chuỗi cố định như:

- `/Type /Sig\n`
- `/ByteRange `
- `/Contents <`
- ` 0 obj\n`

Hệ quả:

- Có thể không nhận diện PDF dùng CRLF hoặc cách bố trí whitespace khác.
- Không xử lý đáng tin cậy xref stream, object stream và indirect object.
- Không hiểu cấu trúc incremental revision thực sự.
- Tên signature field được suy ra bằng text search nên có thể sai.

Nhận định “luôn bỏ sót chữ ký cũ trong PDF multi-revision” chưa hoàn toàn chính xác: chữ ký cũ vẫn có thể được tìm thấy nếu còn tồn tại đúng dạng literal. Tuy nhiên cách scan hiện tại không đủ tin cậy cho PDF từ phần mềm bên thứ ba.

### 1.2. Timestamp chưa xác thực TSA trust chain

Validator hiện chỉ lấy TSA certificate được nhúng trong timestamp token và gọi `TimeStampToken.Validate()`.

Thiếu các kiểm tra:

- Build TSA certificate chain tới trusted root.
- Thời hạn TSA certificate tại `GenTime`.
- TSA certificate revocation.
- Extended Key Usage dành cho timestamping.

Ngoài ra, nếu timestamp token không chứa TSA certificate, implementation hiện tại vẫn đặt `TimestampValid = true` và chỉ thêm warning.

### 1.3. Timestamp chưa được ràng buộc với chữ ký gốc

Implementation chưa so sánh message imprint trong RFC 3161 token với hash của CMS signature value. Vì vậy một timestamp token có chữ ký TSA hợp lệ nhưng được cấp cho dữ liệu khác vẫn có khả năng được chấp nhận.

### 1.4. Timestamp lỗi không làm kết quả tổng thể thất bại

`TimestampValid` hiện không được sử dụng khi tính `IsValid`. Timestamp sai có thể chỉ tạo warning trong khi chữ ký vẫn được báo hợp lệ tổng thể.

### 1.5. Validator không sử dụng revocation data trong DSS

Code validation không đọc các cấu trúc:

- `/DSS`
- `/VRI`
- `/Certs`
- `/OCSPs`
- `/CRLs`

Khi thực hiện revocation check và đủ điều kiện, validator gọi `IOcspClient` hoặc `ICrlClient` bên ngoài. Do đó PDF đã chứa dữ liệu LTV vẫn chưa thể được validate offline một cách đúng nghĩa.

Nói chính xác hơn, validator không phải lúc nào cũng gọi mạng: nó bỏ qua bước gọi client đối với self-signed certificate, khi không inject client, khi thiếu issuer hoặc khi tắt `ValidateRevocation`. Vấn đề chính là DSS bị bỏ qua hoàn toàn.

### 1.6. Revocation chỉ kiểm tra signing certificate

Implementation chỉ gọi revocation check cho end-entity signing certificate. Các intermediate CA trong chain không được kiểm tra. Bước `X509Chain` cũng đặt `RevocationMode.NoCheck`, nên intermediate CA bị thu hồi có thể không được phát hiện.

### 1.7. Revocation đang có hành vi fail-open

Khi bật `ValidateRevocation` nhưng không inject OCSP/CRL client, validator đặt `RevocationValid = true`, kể cả khi `AllowUnknownRevocationStatus = false`.

Self-signed certificate cũng được gán `RevocationValid = true` mà chưa xét certificate đó có được trust hay không.

### 1.8. Chain validation có thể hạ lỗi thành warning

Khi `X509Chain.Build()` trả về `false`, chỉ một số status được ghi thành error. Các trạng thái chain không hợp lệ khác có thể bị hạ xuống warning, dẫn tới `CertificateChainValid` hoặc `IsValid` không phản ánh đúng kết quả build chain.

Implementation cũng đang suy ra trạng thái certificate chain bằng cách tìm prefix trong chuỗi error, thay vì lưu kết quả có cấu trúc.

### 1.9. ByteRange chưa được kiểm tra an toàn

`AssembleByteRange` ép giá trị `long` sang `int` và sao chép dữ liệu mà chưa kiểm tra đầy đủ:

- Giá trị âm.
- Overflow.
- Segment overlap.
- Offset/length vượt kích thước file.
- Khoảng trống ByteRange có thực sự tương ứng với vùng `/Contents` hay không.

### 1.10. Integration và Compatibility tests chỉ là placeholder

Hai test suite hiện chỉ chứa `Assert.True(true)`. Chưa có kiểm thử end-to-end với:

- PDF từ công cụ bên thứ ba.
- Xref stream/object stream.
- Multiple incremental signatures.
- TSA/OCSP/CRL server mô phỏng.
- DSS validation offline.

## 2. Kế hoạch xử lý đề xuất

### 2.1. Mở rộng `PdfValidationOptions`

Đề xuất bổ sung:

```csharp
public bool RequireTimestamp { get; set; } = false;
public bool ValidateTimestampMessageImprint { get; set; } = true;
public bool ValidateTimestampCertificatePeriod { get; set; } = true;
public bool ValidateTimestampChainTrust { get; set; } = false;
public bool ValidateTimestampRevocation { get; set; } = false;

public bool UseEmbeddedDss { get; set; } = true;
public bool AllowOnlineRevocationFallback { get; set; } = true;
public bool ValidateEntireCertificateChainRevocation { get; set; } = false;
```

Ý nghĩa chính:

- `ValidateTimestampChainTrust = true`: TSA phải build chain tới system trusted root.
- `RequireTimestamp = true`: thiếu timestamp làm validation thất bại.
- `ValidateTimestampMessageImprint = true`: bắt buộc timestamp phải thuộc đúng signature value.
- `UseEmbeddedDss = true`: ưu tiên revocation data nhúng trong PDF.
- `AllowOnlineRevocationFallback = false`: chỉ validation offline, không gọi client bên ngoài.
- `ValidateEntireCertificateChainRevocation = true`: kiểm tra signer và toàn bộ intermediate CA, trừ trust anchor.

Các option có khả năng gây breaking change nên giữ mặc định tương thích. Riêng message imprint nên bật mặc định vì đây là điều kiện mật mã cơ bản.

### 2.2. Mở rộng validation result

Tách kết quả thành các trạng thái rõ ràng thay vì suy ra từ nội dung error:

- `CertificatePeriodValid`
- `CertificateChainTrusted`
- `TimestampPresent`
- `TimestampMessageImprintValid`
- `TimestampCertificateValid`
- `TimestampChainTrusted`
- `TimestampRevocationValid`
- `RevocationSource` — DSS, OCSP online, CRL online hoặc none

Giữ các property cũ nếu cần tương thích API, nhưng tính chúng từ các trạng thái có cấu trúc.

### 2.3. Thay signature extractor bằng PDF parser có cấu trúc

Ưu tiên tái sử dụng `LegacyPdfCore.PdfReader` đã có trong solution, với namespace alias để tránh type ambiguity.

Parser mới cần:

- Duyệt AcroForm signature fields.
- Resolve indirect objects.
- Hỗ trợ xref table và xref stream.
- Hỗ trợ object stream.
- Đọc nhiều incremental signatures.
- Trích xuất `/ByteRange`, `/Contents`, `/SubFilter` và field name từ object model.

Nếu việc thêm reference trực tiếp tạo phụ thuộc không mong muốn, tách abstraction nội bộ `IPdfSignatureExtractor` và triển khai parser trong assembly phù hợp.

### 2.4. Kiểm tra ByteRange chặt chẽ

Trước khi assemble signed content:

1. Yêu cầu đúng bốn phần tử.
2. Từ chối số âm hoặc giá trị vượt `int`/kích thước file.
3. Kiểm tra các segment được sắp đúng thứ tự và không overlap.
4. Kiểm tra tổng length không overflow.
5. Kiểm tra vùng bị loại trừ tương ứng với serialized `/Contents`.
6. Trả lỗi validation có ý nghĩa thay vì để `Array.Copy` ném exception chung.

### 2.5. Hoàn thiện timestamp validation

Luồng đề xuất:

1. Xác định timestamp có tồn tại hay không.
2. Nếu `RequireTimestamp = true` và không có token, trả lỗi.
3. Parse RFC 3161 token.
4. Tìm đúng TSA signer certificate; thiếu certificate phải là invalid khi cần verify.
5. Verify CMS signature của TSA.
6. Tính digest của signature value theo thuật toán trong message imprint và so sánh constant-time.
7. Kiểm tra EKU timestamping.
8. Kiểm tra TSA certificate period tại `GenTime`.
9. Nếu `ValidateTimestampChainTrust = true`, build chain tới trusted root.
10. Nếu `ValidateTimestampRevocation = true`, kiểm tra revocation của TSA chain.
11. Đưa `TimestampValid` vào phép tính `IsValid` khi timestamp validation được yêu cầu.

### 2.6. Đọc và sử dụng DSS

Xây dựng `PdfDssReader` để đọc global pools và VRI theo signature hash.

Thứ tự revocation:

1. Tìm VRI tương ứng với signature.
2. Nạp certificate, OCSP response và CRL được nhúng.
3. Xác minh chữ ký OCSP/CRL, issuer, serial number và thời gian hiệu lực/freshness.
4. Dùng dữ liệu DSS nếu hợp lệ.
5. Chỉ fallback sang client online khi dữ liệu nhúng thiếu/không hợp lệ và `AllowOnlineRevocationFallback = true`.

### 2.7. Kiểm tra revocation cho toàn chain

Khi bật `ValidateEntireCertificateChainRevocation`:

1. Build certificate chain trước.
2. Duyệt signing certificate và từng intermediate CA.
3. Bỏ qua trust anchor/root.
4. Xác định đúng issuer của từng certificate.
5. Ưu tiên DSS, sau đó mới fallback online.
6. Tổng hợp kết quả theo từng certificate.

Không có DSS/client phải trả `Unknown` và tuân theo `AllowUnknownRevocationStatus`, không tự động coi là `Good`.

### 2.8. Sửa chain validation

- Khi bật trust validation và `X509Chain.Build()` trả `false`, kết quả chain phải thất bại.
- Ghi lại đầy đủ `X509ChainStatus` để chẩn đoán.
- Không dùng prefix của error message để suy ra trạng thái.
- Phân biệt rõ certificate hết hạn, chain không trusted và revocation failure.

### 2.9. Bổ sung tests

#### Unit tests

- Timestamp message imprint sai.
- TSA self-signed với trust option bật và tắt.
- TSA certificate hết hạn tại `GenTime`.
- TSA certificate sai EKU.
- Timestamp lỗi phải làm `IsValid = false` khi được yêu cầu.
- Thiếu timestamp khi `RequireTimestamp = true`.
- ByteRange âm, overflow, overlap và vượt file.
- DSS OCSP/CRL hợp lệ, revoked, stale và malformed.
- Intermediate CA bị revoke.
- Không DSS, không client và online fallback bị tắt.
- `X509Chain.Build()` thất bại với status ngoài nhóm hiện đang coi là error.

#### Integration và compatibility tests

- PDF dùng CRLF và whitespace khác nhau.
- Xref stream và object stream.
- Nhiều incremental signatures.
- PDF được tạo bởi `LegacyPdfCore`.
- Fixture PDF từ công cụ bên thứ ba nếu có thể lưu hợp pháp trong repository.
- TSA/OCSP/CRL qua WireMock local, không phụ thuộc Internet thật.
- PDF có DSS được validate hoàn toàn offline.

## 3. Thứ tự triển khai

1. Bổ sung options và result model.
2. Sửa timestamp message imprint và đưa timestamp vào `IsValid`.
3. Sửa fail-open revocation và chain-status handling.
4. Bổ sung ByteRange validation.
5. Thay text scanner bằng PDF parser có cấu trúc.
6. Triển khai DSS reader và offline validation.
7. Triển khai revocation cho toàn certificate chain và TSA chain.
8. Hoàn thiện integration/compatibility fixtures.
9. Chạy build/test trên `net48`, `netstandard2.0`, `net8.0` và cập nhật tài liệu migration.

## 4. Tiêu chí hoàn thành

- Không chấp nhận timestamp có message imprint không khớp.
- Có thể yêu cầu TSA phải được system trust store tin tưởng bằng option.
- Timestamp invalid ảnh hưởng đúng tới kết quả tổng thể.
- PDF có DSS hợp lệ có thể validation offline.
- Có thể kiểm tra revocation của toàn certificate chain.
- Không coi trạng thái revocation unknown là good nếu policy không cho phép.
- Extractor làm việc với PDF hợp lệ có serialization khác nhau và nhiều revision.
- Integration và compatibility tests không còn là placeholder.
- Tất cả target framework hiện hỗ trợ đều build và test thành công.
