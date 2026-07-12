namespace PadesSharpDemoApp;

public enum AppLanguage { Vietnamese, English }

public sealed class AppLocale
{
    public static AppLocale Current { get; private set; } = new(AppLanguage.English);

    public static void Set(AppLanguage lang) => Current = new AppLocale(lang);

    public AppLanguage Language { get; }
    private AppLocale(AppLanguage lang) => Language = lang;

    private bool Vi => Language == AppLanguage.Vietnamese;

    // ── Form ─────────────────────────────────────────────────────────────────
    public string FormTitle      => Vi ? "PadesSharp Demo — Ký số PDF"  : "PadesSharp Demo — PDF Digital Signing";
    public string LangToggleText => Vi ? "🇻🇳 Tiếng Việt"               : "🇺🇸 English";

    // ── Groups ────────────────────────────────────────────────────────────────
    public string GrpFiles      => Vi ? "① Files cần ký"               : "① Files to Sign";
    public string GrpOutput     => Vi ? "② Thư mục đầu ra"             : "② Output Folder";
    public string GrpCert       => Vi ? "③ Chứng thư số"               : "③ Digital Certificate";
    public string GrpSignOpts   => Vi ? "④ Tùy chọn ký"                : "④ Signing Options";
    public string GrpAppearance => Vi ? "⑤ Chữ ký hiển thị (Visible Signature)" : "⑤ Visible Signature";
    public string GrpTsa        => Vi ? "Cấu hình TSA"                  : "TSA Configuration";

    // ── File list ─────────────────────────────────────────────────────────────
    public string BtnAddFiles    => Vi ? "＋ Thêm file"   : "＋ Add Files";
    public string BtnRemoveFiles => Vi ? "✕ Xóa"          : "✕ Remove";
    public string BtnClearFiles  => Vi ? "Xóa tất cả"     : "Clear All";
    public string ColFileName    => Vi ? "Tên file"        : "File Name";
    public string ColSize        => Vi ? "Kích thước"      : "Size";
    public string ColFolder      => Vi ? "Thư mục"         : "Folder";
    public string ColStatus      => Vi ? "Trạng thái"      : "Status";

    // ── Output ────────────────────────────────────────────────────────────────
    public string BtnBrowseOutput => Vi ? "📁 Chọn thư mục"                   : "📁 Browse...";
    public string ChkSameFolder   => Vi ? "Cùng thư mục nguồn (suffix _signed)" : "Same source folder (suffix _signed)";

    // ── Certificate ───────────────────────────────────────────────────────────
    public string BtnPickCert    => Vi ? "🔑 Chọn từ Windows Store" : "🔑 Select from Windows Store";
    public string CertSubjectDefault => Vi ? "Subject: —" : "Subject: —";
    public string CertIssuerDefault  => Vi ? "Issuer:  —" : "Issuer:  —";
    public string CertSerialDefault  => Vi ? "Serial:  —" : "Serial:  —";
    public string CertExpiryDefault  => Vi ? "Hết hạn: —" : "Expires: —";
    public string CertExpiryPrefix   => Vi ? "Hết hạn: "  : "Expires: ";

    // ── Signing options ───────────────────────────────────────────────────────
    public string RbBasic   => Vi ? "Basic (adbe.pkcs7.detached)" : "Basic (adbe.pkcs7.detached)";
    public string RbTsa     => Vi ? "TSA – PAdES-T"               : "TSA – PAdES-T";
    public string RbLtv     => Vi ? "LTV/DSS – PAdES-LTA"         : "LTV/DSS – PAdES-LTA";
    public string LblReason   => Vi ? "Lý do:"    : "Reason:";
    public string LblLocation => Vi ? "Địa điểm:" : "Location:";

    // ── Appearance ────────────────────────────────────────────────────────────
    public string ChkShowAppearance => Vi ? "Bật chữ ký trực quan"  : "Enable Visible Signature";
    public string LblPage   => Vi ? "Trang:"  : "Page:";
    public string LblX      => Vi ? "X (pt):" : "X (pt):";
    public string LblY      => Vi ? "Y (pt):" : "Y (pt):";
    public string LblWidth  => Vi ? "Rộng:"   : "Width:";
    public string LblHeight => Vi ? "Cao:"    : "Height:";
    public string ChkShowDate     => Vi ? "Ngày ký"   : "Date";
    public string ChkShowReason   => Vi ? "Lý do"     : "Reason";
    public string ChkShowLocation => Vi ? "Địa điểm"  : "Location";
    public string LblLogo         => Vi ? "Ảnh:"      : "Image:";

    // ── Actions ───────────────────────────────────────────────────────────────
    public string BtnSign     => Vi ? "▶  KÝ SỐ  (F5)" : "▶  SIGN  (F5)";
    public string BtnStopSign => Vi ? "⊗  Dừng"         : "⊗  Stop";
    public string BtnClearLog => Vi ? "🗑 Xóa log"      : "🗑 Clear Log";

    // ── Status ────────────────────────────────────────────────────────────────
    public string StatusReady    => Vi ? "Sẵn sàng"     : "Ready";
    public string StatusStopping => Vi ? "Đang dừng..." : "Stopping...";
    public string StatusWaiting  => Vi ? "Chờ ký"       : "Pending";
    public string StatusSigning  => Vi ? "Đang ký..."   : "Signing...";
    public string StatusDone     => Vi ? "✔ Hoàn thành" : "✔ Done";

    public string StatusSigningProgress(int current, int total) =>
        Vi ? $"Đang ký {current}/{total}..." : $"Signing {current}/{total}...";

    // ── Dialogs ───────────────────────────────────────────────────────────────
    public string DlgPickFilesTitle => Vi ? "Chọn file PDF cần ký"       : "Select PDF files to sign";
    public string DlgPickLogoTitle  => Vi ? "Chọn ảnh chữ ký"            : "Select signature image";
    public string DlgOutputDesc     => Vi ? "Chọn thư mục lưu file đã ký" : "Select output folder";

    // ── Messages / Errors ────────────────────────────────────────────────────
    public string ErrTitle          => Vi ? "Lỗi"      : "Error";
    public string MsgInfoTitle      => Vi ? "Thông báo" : "Information";
    public string MsgSignResultTitle => Vi ? "Kết quả ký" : "Signing Result";
    public string MsgConfirmTitle   => Vi ? "Xác nhận"  : "Confirm";

    public string ErrNoFiles    => Vi ? "Vui lòng thêm ít nhất một file PDF để ký."
                                       : "Please add at least one PDF file to sign.";
    public string ErrNoCert     => Vi ? "Vui lòng chọn chứng thư ký số."
                                       : "Please select a digital certificate.";
    public string ErrCertExpired(string date) =>
        Vi ? $"Chứng thư đã hết hạn vào {date}.\nVui lòng chọn chứng thư khác."
           : $"Certificate expired on {date}.\nPlease select another certificate.";
    public string ErrNoPrivKey  => Vi ? "Chứng thư được chọn không có private key.\nKiểm tra lại thiết bị hoặc USB token."
                                       : "Selected certificate has no private key.\nCheck device or USB token.";
    public string ErrNoTsaUrl   => Vi ? "Vui lòng nhập TSA URL để ký ở mức TSA/LTV."
                                       : "Please enter TSA URL for TSA/LTV signing.";
    public string ErrInvalidTsaUrl => Vi ? "TSA URL không hợp lệ. Phải bắt đầu bằng https:// hoặc http://."
                                          : "Invalid TSA URL. Must start with https:// or http://.";
    public string ErrNoOutput   => Vi ? "Vui lòng chọn thư mục đầu ra."
                                       : "Please select an output folder.";
    public string MsgCreateFolder(string path) =>
        Vi ? $"Thư mục đầu ra không tồn tại:\n{path}\n\nTạo mới?"
           : $"Output folder does not exist:\n{path}\n\nCreate it?";
    public string MsgNoFiles    => Vi ? "Không có file nào để ký." : "No files to sign.";
    public string MsgComplete(int ok, int fail) =>
        Vi ? $"Hoàn thành: {ok} thành công, {fail} thất bại."
           : $"Done: {ok} succeeded, {fail} failed.";

    public string LogStartup => Vi ? "PadesSharp Demo khởi động." : "PadesSharp Demo started.";

    // ── SigningService log messages ───────────────────────────────────────────
    public string LogSignStart(string input, string output) =>
        Vi ? $"Bắt đầu ký: {input} → {output}" : $"Signing: {input} → {output}";
    public string LogSignOk(string input, string level) =>
        Vi ? $"OK  {input} ký xong ({level})" : $"OK  {input} signed ({level})";
    public string LogSignFailed(string input) =>
        Vi ? $"Ký thất bại: {input}" : $"Sign failed: {input}";
    public string LogSignCancelled(string input) =>
        Vi ? $"Đã huỷ ký: {input}" : $"Cancelled: {input}";
    public string LogSignError(string input) =>
        Vi ? $"Lỗi ký {input}" : $"Error signing {input}";
    public string LogEmbedDss(string input) =>
        Vi ? $"Nhúng DSS/VRI cho LTV: {input}" : $"Embedding DSS/VRI for LTV: {input}";
    public string ErrEngineReturnedFalse =>
        Vi ? "Engine trả về Success=false" : "Engine returned Success=false";
    public string ErrJobCancelled =>
        Vi ? "Đã huỷ" : "Cancelled";

    // ── Signature preview panel ───────────────────────────────────────────────
    public string PreviewNoCert  => Vi ? "Chưa chọn chứng thư"  : "No certificate selected";
    public string PreviewSignedBy => Vi ? "Ký bởi: "             : "Signed by: ";
    public string PreviewDate    => Vi ? "Ngày: "                : "Date: ";
    public string PreviewReason  => Vi ? "Lý do: "               : "Reason: ";
    public string PreviewLocation => Vi ? "Địa điểm: "           : "Location: ";
}
