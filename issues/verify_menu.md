# Kế hoạch bổ sung menu/tab Xác minh chữ ký PDF

## 1. Mục tiêu

Bổ sung một tab **Xác minh** (`Verify`) riêng cho `apps/PadesSharpDemoApp`, nằm cạnh tab **Ký số** (`Sign`). Người dùng có thể chọn hoặc kéo thả một/nhiều PDF, chạy xác minh chữ ký, xem trạng thái tổng quan của từng file và mở chi tiết từng chữ ký.

Thay đổi này chỉ mở rộng demo app; không thay đổi public API của các thư viện lõi. Việc triển khai code chỉ bắt đầu sau khi kế hoạch này được duyệt.

## 2. Hiện trạng đã khảo sát

- `MainForm` hiện là một màn hình ký duy nhất, dựng thủ công trong `MainForm.Designer.cs`; chưa có `TabControl`.
- Header, console log và status bar đang dùng chung toàn form. Phần chọn file, chứng thư, tùy chọn ký, appearance và action bar nằm trong một `TableLayoutPanel` 5 hàng.
- Luồng ký, xử lý event, đa ngôn ngữ và settings đang tập trung trong `MainForm.cs`/`AppLocale.cs`.
- Demo app chưa tham chiếu `src/ModernPdf.Validation`.
- Thư viện đã có đủ nền tảng verify:
  - `DefaultPdfSignatureValidator` xác minh toàn bộ chữ ký trong PDF.
  - `PdfValidationFileHelper.ValidateFile(...)` nhận đường dẫn file.
  - `PdfValidationOptions` điều khiển integrity/CMS, thời hạn và trust chain, timestamp, DSS, OCSP/CRL.
  - `PdfValidationReport` trả kết quả toàn tài liệu; `PdfSignatureValidationResult` trả chi tiết theo chữ ký.
- API xác minh hiện là đồng bộ và không nhận `CancellationToken`. Các OCSP/CRL client có API async nhưng validator gọi đồng bộ bên trong; vì vậy phải chạy verify ngoài UI thread và cần mô tả chính xác giới hạn của nút dừng.
- `PdfValidationReport.IsValid == false` cả khi PDF không có chữ ký. UI phải hiển thị riêng trạng thái **Không có chữ ký**, không gộp với **Chữ ký không hợp lệ**.

## 3. Phạm vi MVP

### Bao gồm

- Hai tab cấp cao: **Ký số** và **Xác minh**.
- Giữ nguyên toàn bộ chức năng và bố cục ký hiện tại trong tab Ký số.
- Chọn nhiều PDF bằng dialog hoặc kéo thả vào tab Xác minh.
- Verify tuần tự theo batch để dễ theo dõi trạng thái và tránh nhiều truy vấn OCSP/CRL đồng thời.
- Hiển thị kết quả theo file và chi tiết cho từng chữ ký.
- Các tùy chọn verify phổ biến, có mặc định an toàn và nhãn giải thích rõ.
- Log, progress, trạng thái, dừng batch và đa ngôn ngữ Việt/Anh.
- Lưu các tùy chọn verify không nhạy cảm vào settings.

### Chưa bao gồm trong MVP

- Xuất báo cáo PDF/JSON/CSV.
- Xem nội dung PDF hoặc vùng chữ ký trực quan.
- Cấu hình proxy, custom trust store hay import CA riêng.
- Validate song song nhiều file.
- Theo dõi thư mục tự động.
- Hủy tức thời một lệnh verify đang chạy trong thư viện lõi; nút dừng chỉ ngăn file tiếp theo được xử lý. Có thể bổ sung cancellation thật ở thư viện trong issue riêng.

## 4. Thiết kế UX đề xuất

### 4.1. Điều hướng cấp cao

Thêm `TabControl tcMain` dưới header, gồm:

1. `tabSign` — **Ký số / Sign**: chứa nguyên phần UI hiện tại.
2. `tabVerify` — **Xác minh / Verify**: chứa UI mới.

Header logo, chọn ngôn ngữ và `StatusStrip` vẫn dùng chung. Console nên dùng chung trong MVP để giảm tái cấu trúc; log phải có tiền tố `[SIGN]`/`[VERIFY]`. Action bar và progress của từng workflow nằm bên trong tab tương ứng để tránh nút Ký xuất hiện trong màn hình Xác minh.

Phím tắt phải phụ thuộc tab đang chọn:

| Phím | Tab Ký số | Tab Xác minh |
|---|---|---|
| `Ctrl+O` | Thêm PDF để ký | Thêm PDF để verify |
| `F5` | Bắt đầu ký | Bắt đầu verify |
| `Delete` | Xóa file đang chọn | Xóa file đang chọn |
| `Escape` | Yêu cầu dừng batch ký | Yêu cầu dừng batch verify |
| `Ctrl+L` | Xóa console dùng chung | Xóa console dùng chung |

### 4.2. Bố cục tab Xác minh

Đề xuất dùng `SplitContainer`:

- Bên trái (khoảng 55%): danh sách file batch.
- Bên phải (khoảng 45%): tùy chọn verify ở trên và chi tiết kết quả ở dưới.
- Dưới cùng: progress + nút **Dừng** + nút chính **Xác minh (F5)**.

#### Danh sách file `lvVerifyFiles`

Các cột:

| Cột | Nội dung |
|---|---|
| File | Tên file |
| Thư mục | Đường dẫn thư mục |
| Chữ ký | Số chữ ký tìm thấy |
| Kết quả | Chờ / Đang xác minh / Hợp lệ / Có cảnh báo / Không hợp lệ / Không có chữ ký / Lỗi |

Các nút: **Thêm PDF**, **Xóa**, **Xóa tất cả**. Hỗ trợ drag & drop, bỏ qua file không phải `.pdf`, chống thêm trùng bằng so sánh full path không phân biệt hoa thường.

Màu chỉ là tín hiệu phụ; luôn có text/icon để không phụ thuộc khả năng phân biệt màu:

- xanh: mọi kiểm tra được bật đều đạt;
- vàng: integrity/CMS đạt nhưng có warning hoặc revocation không xác định được theo policy cho phép;
- đỏ: một kiểm tra bắt buộc thất bại;
- xám: không có chữ ký/chưa chạy/đã dừng;
- đỏ sẫm: lỗi đọc file hoặc exception.

#### Tùy chọn verify

Hiển thị hai profile để người dùng không phải hiểu toàn bộ cờ kỹ thuật:

- **Tiêu chuẩn (khuyến nghị)**: integrity/CMS, thời hạn chứng thư, timestamp, DSS; kiểm tra trust chain hệ thống; dùng OCSP rồi CRL nếu DSS không đủ; trạng thái thu hồi không xác định là warning.
- **Ngoại tuyến**: integrity/CMS, thời hạn chứng thư, timestamp và DSS; không truy cập mạng; trạng thái thu hồi không xác định là warning.

Nhóm **Nâng cao** có thể mở rộng/thu gọn và ánh xạ trực tiếp tới `PdfValidationOptions`:

- Kiểm tra thời hạn chứng thư (`ValidateCertificateChain`).
- Kiểm tra trust chain hệ thống (`ValidateChainTrust`).
- Kiểm tra thu hồi (`ValidateRevocation`).
- Cho phép trạng thái thu hồi không xác định (`AllowUnknownRevocationStatus`).
- Ưu tiên DSS nhúng (`UseEmbeddedDss`).
- Cho phép OCSP/CRL online (`AllowOnlineRevocationFallback`).
- Kiểm tra timestamp (`ValidateTimestamp`).
- Bắt buộc có timestamp (`RequireTimestamp`).
- Kiểm tra trust chain TSA (`ValidateTimestampChainTrust`).
- Kiểm tra thu hồi TSA (`ValidateTimestampRevocation`).
- Kiểm tra toàn bộ chain (`ValidateEntireCertificateChainRevocation`).

Ràng buộc UI:

- Tắt `ValidateCertificateChain` thì disable trust chain và revocation chain.
- Tắt `ValidateRevocation` thì disable các tùy chọn DSS/online/unknown.
- Chọn Offline thì `AllowOnlineRevocationFallback = false` và không tạo OCSP/CRL client.
- Bật `RequireTimestamp` tự động bật `ValidateTimestamp`.
- Tooltip giải thích trust chain phụ thuộc kho chứng thư Windows và kết quả online phụ thuộc mạng/responder.

#### Chi tiết kết quả

Khi chọn một file đã chạy, hiển thị:

1. Banner tổng quan: **Hợp lệ**, **Có cảnh báo**, **Không hợp lệ**, **Không có chữ ký**, hoặc **Không thể xác minh**.
2. `TreeView` hoặc `ListView` theo cấu trúc `File -> SignatureName -> checks`.
3. Mỗi chữ ký hiển thị tối thiểu:
   - toàn vẹn tài liệu (`DocumentIntegrityValid`);
   - chữ ký CMS (`CmsSignatureValid`);
   - thời hạn chứng thư (`CertificatePeriodValid`);
   - trust chain (`CertificateChainTrusted`);
   - trạng thái thu hồi và nguồn (`RevocationValid`, `RevocationSource`);
   - có timestamp hay không và kết quả timestamp;
   - `Errors` và `Warnings` nguyên văn từ validator.

Không hiển thị một nhãn “Valid” duy nhất cho mọi khái niệm. Banner phải tóm tắt theo policy đã chọn; bảng chi tiết giúp phân biệt **tài liệu không bị sửa**, **chữ ký mật mã đúng**, **chứng thư được tin cậy** và **chưa bị thu hồi**.

## 5. Mô hình và service mới

### `Models/VerifyFileItem.cs`

Lưu state riêng cho mỗi file:

- `FilePath`
- `VerifyFileStatus` (`Pending`, `Running`, `Valid`, `Warning`, `Invalid`, `Unsigned`, `Error`, `Cancelled`)
- `PdfValidationReport? Report`
- `string? ErrorMessage`
- thời điểm/elapsed (tùy chọn, hữu ích cho log)

Không nhét report trực tiếp vào `ListViewItem.Tag` như nguồn dữ liệu duy nhất; giữ model rõ ràng rồi map lên UI.

### `Models/VerificationProfile.cs` và `VerificationOptions.cs`

- Profile enum: `Standard`, `Offline`, `Custom`.
- Model UI độc lập với type thư viện, có hàm map sang `PdfValidationOptions`.
- Khi người dùng đổi checkbox nâng cao, profile chuyển sang `Custom`.

### `Services/VerificationService.cs`

Trách nhiệm:

1. Tạo `DefaultPdfSignatureValidator` với logger.
2. Với profile có online fallback, tạo một `HttpClient` dùng chung trong vòng đời service, `DefaultOcspClient` và `DefaultCrlClient`; đặt timeout hữu hạn.
3. Gọi `PdfValidationFileHelper.ValidateFile` bằng `Task.Run` để không khóa WinForms UI.
4. Phân loại report thành trạng thái UI; không sửa dữ liệu report.
5. Bắt exception theo file, log và trả kết quả lỗi để batch tiếp tục.
6. Dispose tài nguyên mạng khi form đóng.

Lưu ý: không chạy `Task.Run` cho từng file cùng lúc. Batch gọi tuần tự và kiểm tra cancellation trước/sau mỗi file. Không được tuyên bố một file đã “cancelled” nếu validator vẫn đang chạy nền; UI hiển thị **Đang dừng sau file hiện tại**.

## 6. Thay đổi theo file

| File | Thay đổi dự kiến |
|---|---|
| `PadesSharpDemoApp.csproj` | Thêm `ProjectReference` tới `src/ModernPdf.Validation/ModernPdf.Validation.csproj`. |
| `MainForm.Designer.cs` | Thêm `tcMain`, `tabSign`, `tabVerify`; chuyển root UI ký hiện tại vào `tabSign`; dựng danh sách, options, details và action bar verify. |
| `MainForm.cs` | Wire event verify, batch workflow, tab-aware shortcuts, state enable/disable, binding kết quả chi tiết. Nên tách phần verify sang partial `MainForm.Verify.cs` để tránh làm file hiện tại phình thêm. |
| `MainForm.Verify.cs` | Chứa logic UI riêng của tab verify: add/remove/drop, collect options, start/stop, render report. |
| `AppLocale.cs` | Thêm toàn bộ nhãn/tab/trạng thái/message/log Việt-Anh cho verify. Không hard-code chuỗi ở event handler. |
| `SettingsService.cs` | Thêm profile và các tùy chọn verify; giữ default tương thích khi settings cũ thiếu field. Không lưu URL động từ chứng thư hay dữ liệu chứng thư. |
| `Models/VerifyFileItem.cs` | Model file và enum trạng thái. |
| `Models/VerificationOptions.cs` | Model profile/options và mapping sang `PdfValidationOptions`. |
| `Services/VerificationService.cs` | Adapter/orchestrator của `ModernPdf.Validation`, OCSP và CRL. |
| `docs/demo-app.md` | Cập nhật ảnh/bố cục, workflow, phím tắt và dependency sau khi code hoàn tất. |

## 7. Luồng xử lý

1. Người dùng chọn tab Xác minh và thêm/kéo thả PDF.
2. Chọn profile hoặc chỉnh tùy chọn nâng cao.
3. Nhấn `F5`/Xác minh.
4. Kiểm tra danh sách không rỗng và các file còn tồn tại/đọc được.
5. Khóa thao tác thay đổi danh sách/options; bật nút Dừng; reset progress và kết quả cũ của batch được chạy lại.
6. Với từng file:
   - kiểm tra cancellation;
   - đổi trạng thái sang Running;
   - chạy `VerificationService` ngoài UI thread;
   - phân loại và cập nhật số chữ ký/kết quả;
   - ghi log tóm tắt cùng errors/warnings;
   - tăng progress.
7. Khi hoàn tất hoặc yêu cầu dừng, mở lại UI và hiển thị thống kê: tổng file, valid, warning, invalid, unsigned, error, skipped.
8. Chọn một dòng để render report chi tiết ở panel bên phải.

## 8. Quy tắc phân loại kết quả UI

Ưu tiên từ cao xuống thấp:

1. Exception/không đọc được file -> `Error`.
2. `report.Signatures.Count == 0` -> `Unsigned`.
3. Bất kỳ signature có `IsValid == false` -> `Invalid`.
4. Tất cả signature valid nhưng có bất kỳ `Warnings` -> `Warning`.
5. Còn lại và `report.IsValid == true` -> `Valid`.

Không suy luận lại tính hợp lệ từ màu hoặc một vài field con. `IsValid` của thư viện là nguồn chuẩn theo `PdfValidationOptions`; các field con chỉ phục vụ giải thích.

## 9. Xử lý lỗi và an toàn UX

- File bị xóa/đổi quyền sau khi thêm: đánh dấu Error, tiếp tục batch.
- PDF lỗi/mã hóa/format không hỗ trợ: hiển thị exception thân thiện và log chi tiết.
- OCSP/CRL timeout hoặc không có endpoint: đưa vào warning/error đúng theo `AllowUnknownRevocationStatus`.
- Không để lỗi một file dừng cả batch.
- Chặn đóng form im lặng khi verify đang chạy: hỏi xác nhận; nếu đồng ý thì yêu cầu dừng và chỉ dispose tài nguyên sau khi tác vụ hiện tại kết thúc an toàn.
- Không ghi nội dung chứng thư, token hoặc dữ liệu PDF vào log; chỉ log đường dẫn, tên signature và kết quả kiểm tra.
- Khi đổi ngôn ngữ, giữ nguyên report/model và chỉ render lại nhãn; không chạy verify lại.

## 10. Kiểm thử và tiêu chí nghiệm thu

### Unit test nên bổ sung

- Mapping `VerificationOptions -> PdfValidationOptions` cho Standard, Offline và Custom.
- Phân loại `Valid/Warning/Invalid/Unsigned/Error` từ report.
- Chống file trùng và chỉ nhận `.pdf`.
- Chuỗi tổng kết batch và trạng thái cancellation.
- Settings cũ vẫn deserialize được và nhận default verify mới.

Ưu tiên tách classifier/mapper thành class thuần để test không cần khởi tạo WinForms. Nếu demo app chưa có test project, tạo `tests/PadesSharpDemoApp.Tests` chỉ tham chiếu các model/service thuần; không test pixel/layout ở đây.

### Test tích hợp/thủ công

Ma trận tối thiểu:

| Dữ liệu | Kỳ vọng |
|---|---|
| PDF không có chữ ký | Unsigned, không ghi chung là Invalid signature |
| PDF ký Basic hợp lệ | Integrity/CMS đạt; trust/revocation theo profile |
| PDF bị sửa sau ký | Invalid; integrity thất bại |
| PDF nhiều chữ ký, tất cả đạt | Valid, đủ từng node chữ ký |
| PDF nhiều chữ ký, một chữ ký hỏng | Toàn file Invalid, chỉ rõ chữ ký hỏng |
| PDF có timestamp hợp lệ | Hiển thị timestamp present/valid |
| PDF thiếu timestamp khi `RequireTimestamp` bật | Invalid |
| PDF LTV có DSS, offline | Dùng nguồn DSS nếu dữ liệu đủ |
| Chứng thư self-signed | Integrity có thể đạt nhưng trust chain thất bại khi strict |
| Mất mạng/OCSP timeout | UI không treo; warning/error theo policy |
| Batch nhiều file và nhấn Escape | Xong file hiện tại rồi dừng, file sau giữ trạng thái skipped/pending |
| Đổi Việt/Anh sau verify | Kết quả còn nguyên, nhãn được render lại |

### Tiêu chí nghiệm thu

- Tab Ký số vẫn hoạt động như trước và không có regression về layout/phím tắt/settings.
- Tab Xác minh không khóa UI trong lúc chạy, kể cả khi OCSP/CRL chậm.
- Người dùng nhìn được trạng thái từng file và nguyên nhân ở cấp từng chữ ký.
- Phân biệt rõ unsigned, invalid và error.
- Profile Offline không phát sinh request mạng.
- Profile Standard sử dụng DSS trước, chỉ fallback online khi cần.
- Build app thành công và toàn bộ test hiện có vẫn pass.
- Kiểm tra thủ công ở DPI 100%, 125%, 150% và cả Việt/Anh không bị cắt chữ/control.

## 11. Thứ tự triển khai đề xuất

### Phase 1 — Khung tab, không đổi hành vi ký

- Thêm `TabControl`, đưa UI hiện tại vào `tabSign`.
- Chuyển action bar ký vào tab Ký; giữ console/status dùng chung.
- Làm phím tắt phụ thuộc tab.
- Build và smoke-test toàn bộ luồng ký trước khi thêm verify.

### Phase 2 — Domain model và service verify

- Thêm project reference Validation.
- Tạo option mapper, result classifier và `VerificationService`.
- Viết unit test cho mapper/classifier/settings migration.

### Phase 3 — UI và batch workflow

- Dựng tab Verify, file picker/drag-drop/list/details/options.
- Thêm chạy nền tuần tự, progress, stop-after-current và log.
- Render report nhiều chữ ký, errors/warnings, trạng thái màu + text.

### Phase 4 — Hoàn thiện

- Bổ sung localization và settings.
- Chạy ma trận test offline/online/DSS/timestamp/tampered/multi-signature.
- Kiểm tra DPI, keyboard và accessibility cơ bản.
- Cập nhật `docs/demo-app.md`.

## 12. Rủi ro và việc nên tách riêng

- **Cancellation chưa xuyên suốt:** validator đồng bộ và không nhận token. MVP dùng stop-after-current; issue tiếp theo nên bổ sung async/cancellation cho `IPdfSignatureValidator` hoặc overload mới để không phá API.
- **Kết quả revocation phụ thuộc môi trường:** network, AIA/CDP và responder có thể không ổn định. UI phải hiển thị policy + nguồn dữ liệu và không biến `Unknown` thành “an toàn”.
- **Trust store khác nhau giữa máy:** cùng một chữ ký có thể được trust trên máy này nhưng không trên máy khác; tooltip và report cần nói rõ dùng Windows system trust store.
- **`MainForm` đang quá nhiều trách nhiệm:** dùng partial file là giải pháp ít rủi ro cho MVP. Sau khi tính năng ổn định có thể tách `SignTabControl`/`VerifyTabControl` thành `UserControl` trong refactor riêng.
- **Chuỗi lỗi từ thư viện hiện là tiếng Anh:** MVP có thể hiển thị nguyên văn trong phần kỹ thuật; không nên dịch bằng so khớp chuỗi. Nếu cần localization đầy đủ, thư viện phải trả error code có cấu trúc ở issue riêng.

## 13. Quyết định đề xuất để duyệt

- Dùng **tab cấp cao riêng** cho Verify như đề xuất của issue.
- MVP hỗ trợ **batch nhiều file** và **chi tiết nhiều chữ ký** ngay từ đầu.
- Có hai profile mặc định **Tiêu chuẩn** và **Ngoại tuyến**, cùng phần Nâng cao.
- Giữ **console/status dùng chung**, action/progress nằm trong từng tab.
- Nút Dừng của Verify là **dừng sau file hiện tại** cho đến khi core hỗ trợ cancellation thật.
- Không thêm export report hoặc PDF viewer trong đợt này.
