# PadesSharp Demo App — Thiết kế chi tiết WinForms

## 1. Tổng quan

**PadesSharpDemoApp** là ứng dụng WinForms (.NET 9, Windows) cho phép người dùng ký số PDF theo chuẩn PAdES sử dụng thư viện **PadesSharp**. Ứng dụng hỗ trợ:

- Chọn một hoặc nhiều file PDF cần ký
- Chọn thư mục đầu ra
- Chọn mức ký: Basic, TSA (PAdES-T), LTV/DSS (PAdES-LTA)
- Chọn chứng thư từ Windows Certificate Store
- Cấu hình chữ ký hiển thị: trang, vị trí, kích thước, ảnh
- Xem log chi tiết trong output console tích hợp

---

## 2. Cấu trúc dự án

```
tools/
  PadesSharpDemoApp/
    PadesSharpDemoApp.csproj
    Program.cs
    MainForm.cs
    MainForm.Designer.cs
    MainForm.resx
    Models/
      SigningOptions.cs
      AppearanceOptions.cs
      BatchSignJob.cs
      SigningLevel.cs
    Services/
      CertificatePickerService.cs
      SigningService.cs
      LogService.cs
    UI/
      FileListPanel.cs
      OutputPanel.cs
      SigningOptionsPanel.cs
      AppearanceOptionsPanel.cs
      CertificatePanel.cs
      ConsolePanel.cs
    Resources/
      default-stamp.png
```

> Thêm project `PadesSharpDemoApp` vào `PadesSharp.sln` dưới thư mục `tools/`.

---

## 3. File dự án

### `PadesSharpDemoApp.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ApplicationIcon>Resources\app.ico</ApplicationIcon>
    <AssemblyTitle>PadesSharp Demo</AssemblyTitle>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\ModernPdf.Signing\ModernPdf.Signing.csproj" />
    <ProjectReference Include="..\..\src\ModernPdf.Pades\ModernPdf.Pades.csproj" />
    <ProjectReference Include="..\..\src\ModernPdf.Crypto\ModernPdf.Crypto.csproj" />
    <ProjectReference Include="..\..\src\ModernPdf.Appearance\ModernPdf.Appearance.csproj" />
  </ItemGroup>
</Project>
```

---

## 4. Thiết kế giao diện (MainForm)

### 4.1. Layout tổng thể

```
┌──────────────────────────────────────────────────────────────────────────┐
│  PadesSharp Demo — PDF Digital Signing Tool                  [─][□][×]   │
├────────────────────────────────┬─────────────────────────────────────────┤
│  [1] FILES TO SIGN             │  [3] CERTIFICATE                        │
│  ┌──────────────────────────┐  │  ┌─────────────────────────────────┐    │
│  │ ListView (multi-select)  │  │  │  Subject: CN=...                │    │
│  │  FileName  |  Size  |Path│  │  │  Issuer:  CA=...                │    │
│  │  doc1.pdf  | 120 KB |... │  │  │  Serial:  3A:...                │    │
│  │  doc2.pdf  | 85 KB  |... │  │  │  Valid:   2025-01-01 →          │    │
│  │  ...                     │  │  │           2027-01-01            │    │
│  └──────────────────────────┘  │  └─────────────────────────────────┘    │
│  [+ Add Files] [✕ Remove]      │  [🔑 Chọn từ Windows Store]             │
│                                 │                                         │
│  [2] OUTPUT FOLDER              │  [4] SIGNING OPTIONS                   │
│  ┌──────────────────────────┐  │  ┌─────────────────────────────────┐    │
│  │ C:\Users\...\signed\     │  │  │ Level:  ◉ Basic                 │    │
│  └──────────────────────────┘  │  │         ○ TSA (PAdES-T)         │    │
│  [📁 Browse...]  ☑ Same as src │  │         ○ LTV/DSS (PAdES-LTA)  │    │
│                                 │  │─────────────────────────────────│    │
│                                 │  │ [TSA] URL: [_______________]    │    │
│                                 │  │       User:[___] Pass:[___]     │    │
│                                 │  │─────────────────────────────────│    │
│                                 │  │ Reason:   [___________________] │    │
│                                 │  │ Location: [___________________] │    │
│                                 │  └─────────────────────────────────┘    │
├────────────────────────────────┴─────────────────────────────────────────┤
│  [5] SIGNATURE APPEARANCE                                                  │
│  ┌──────────────────────────────────────────────────────────────────────┐ │
│  │  ☑ Hiển thị chữ ký trực quan                                        │ │
│  │  Page: [1    ▲▼]   X (pts): [36   ▲▼]   Y (pts): [36   ▲▼]        │ │
│  │  Width:[180  ▲▼]   Height:  [60   ▲▼]                               │ │
│  │  ☑ Hiện ngày ký    ☑ Hiện lý do    ☑ Hiện địa điểm                 │ │
│  │  Ảnh chữ ký: [C:\...\stamp.png           ] [📂 Browse] [✕ Xóa]     │ │
│  └──────────────────────────────────────────────────────────────────────┘ │
├──────────────────────────────────────────────────────────────────────────┤
│  [▶ Ký tất cả (2 files)]                           [⊗ Dừng]  [🗑 Xóa log]│
├──────────────────────────────────────────────────────────────────────────┤
│  [6] OUTPUT CONSOLE                                                        │
│  ┌──────────────────────────────────────────────────────────────────────┐ │
│  │ [11:23:01] INFO  Starting batch: 2 file(s)                          │ │
│  │ [11:23:01] INFO  Signing: doc1.pdf → doc1_signed.pdf               │ │
│  │ [11:23:02] OK    doc1.pdf signed successfully (PAdES-T)             │ │
│  │ [11:23:02] INFO  Signing: doc2.pdf → doc2_signed.pdf               │ │
│  │ [11:23:03] ERROR doc2.pdf: Certificate expired                      │ │
│  └──────────────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────────┘
```

### 4.2. Kích thước Form

| Thuộc tính   | Giá trị            |
|--------------|--------------------|
| MinimumSize  | 900 × 780          |
| StartPosition| CenterScreen       |
| FormBorderStyle | Sizable         |
| AutoScaleDimensions | 96 DPI    |

---

## 5. Models

### `SigningLevel.cs`

```csharp
namespace PadesSharpDemoApp.Models;

public enum SigningLevel
{
    Basic,      // adbe.pkcs7.detached — chữ ký cơ bản
    Tsa,        // PAdES-T  — basic + RFC 3161 timestamp
    LtvDss      // PAdES-LTA — TSA + DSS/VRI + OCSP/CRL embedded
}
```

### `SigningOptions.cs`

```csharp
namespace PadesSharpDemoApp.Models;

public sealed class SigningOptions
{
    // Level
    public SigningLevel Level { get; set; } = SigningLevel.Basic;

    // TSA
    public string? TsaUrl   { get; set; }
    public string? TsaUser  { get; set; }
    public string? TsaPass  { get; set; }   // SecureString tốt hơn; đơn giản hóa cho demo

    // Metadata
    public string? Reason   { get; set; }
    public string? Location { get; set; }

    // SubFilter
    public string SubFilter { get; set; } = "adbe.pkcs7.detached";
}
```

### `AppearanceOptions.cs`

```csharp
using System.Drawing;

namespace PadesSharpDemoApp.Models;

public sealed class AppearanceOptions
{
    public bool Enabled      { get; set; } = true;

    // Placement
    public int   PageNumber  { get; set; } = 1;
    public float X           { get; set; } = 36f;   // pts, lower-left origin
    public float Y           { get; set; } = 36f;
    public float Width       { get; set; } = 180f;
    public float Height      { get; set; } = 60f;

    // Content flags
    public bool ShowDate     { get; set; } = true;
    public bool ShowReason   { get; set; } = true;
    public bool ShowLocation { get; set; } = true;

    // Logo / stamp image — raw bytes loaded from file
    public byte[]? LogoImageBytes { get; set; }
    public string? LogoImagePath  { get; set; }
}
```

### `BatchSignJob.cs`

```csharp
namespace PadesSharpDemoApp.Models;

public sealed class BatchSignJob
{
    public string InputPath   { get; set; } = "";
    public string OutputPath  { get; set; } = "";
    public JobStatus Status   { get; set; } = JobStatus.Pending;
    public string? ErrorMessage { get; set; }
}

public enum JobStatus { Pending, Running, Success, Failed }
```

---

## 6. Services

### `CertificatePickerService.cs`

Mở Windows Certificate Store dialog (native `X509Certificate2UI`) và trả về chứng thư + chain.

```csharp
using System.Security.Cryptography.X509Certificates;

namespace PadesSharpDemoApp.Services;

public static class CertificatePickerService
{
    /// <summary>
    /// Mở hộp thoại chọn chứng thư từ Windows Store.
    /// Trả về null nếu người dùng huỷ.
    /// </summary>
    public static X509Certificate2? Pick()
    {
        var store = new X509Store(StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);

        // Chỉ hiển thị chứng thư có private key, chưa hết hạn
        var eligible = store.Certificates
            .Find(X509FindType.FindByTimeValid, DateTime.Now, false)
            .Where(c => c.HasPrivateKey)
            .ToArray();

        var col = new X509Certificate2Collection(eligible);

        var selected = X509Certificate2UI.SelectFromCollection(
            col,
            "Chọn chứng thư ký số",
            "Chọn chứng thư để ký PDF:",
            X509SelectionFlag.SingleSelection);

        store.Close();
        return selected.Count > 0 ? selected[0] : null;
    }

    /// <summary>
    /// Lấy toàn bộ chain từ chứng thư đã chọn.
    /// </summary>
    public static IReadOnlyList<X509Certificate2> BuildChain(X509Certificate2 cert)
    {
        var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.Build(cert);

        return chain.ChainElements
            .Cast<X509ChainElement>()
            .Select(e => e.Certificate)
            .ToList();
    }
}
```

### `SigningService.cs`

Orchestrator gọi `PdfSigningEngine`, `DssIncrementalWriter`, `LtvDataCollector` từ thư viện core.

```csharp
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using ModernPdf.Abstractions.Appearance;
using ModernPdf.Abstractions.Signing;
using ModernPdf.Abstractions.Tsa;
using ModernPdf.Crypto;
using ModernPdf.Crypto.Tsa;
using ModernPdf.Pades;
using ModernPdf.Signing;
using PadesSharpDemoApp.Models;

namespace PadesSharpDemoApp.Services;

public sealed class SigningService
{
    private readonly ILogger<SigningService> _logger;

    public SigningService(ILogger<SigningService> logger)
        => _logger = logger;

    public async Task<bool> SignAsync(
        BatchSignJob job,
        X509Certificate2 certificate,
        IReadOnlyList<X509Certificate2> chain,
        SigningOptions opts,
        AppearanceOptions appearance,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Signing: {Input} → {Output}", job.InputPath, job.OutputPath);
            job.Status = JobStatus.Running;

            // 1. Tạo signature provider từ private key của chứng thư
            var provider = new RsaSoftwareSignatureProvider(certificate);

            // 2. Tạo TSA client nếu cần
            ITsaClient? tsaClient = null;
            if (opts.Level is SigningLevel.Tsa or SigningLevel.LtvDss
                && !string.IsNullOrWhiteSpace(opts.TsaUrl))
            {
                tsaClient = new HttpTsaClient(opts.TsaUrl,
                    opts.TsaUser, opts.TsaPass);
            }

            // 3. Tạo appearance request
            PdfSignatureAppearanceRequest? appearanceReq = null;
            if (appearance.Enabled)
            {
                appearanceReq = new PdfSignatureAppearanceRequest
                {
                    SignerName     = certificate.GetNameInfo(X509NameType.SimpleName, false),
                    Reason         = opts.Reason,
                    Location       = opts.Location,
                    PageNumber     = appearance.PageNumber,
                    Width          = appearance.Width,
                    Height         = appearance.Height,
                    ShowDate       = appearance.ShowDate,
                    ShowReason     = appearance.ShowReason,
                    ShowLocation   = appearance.ShowLocation,
                    LogoImageBytes = appearance.LogoImageBytes,
                    Rectangle = new PdfSignatureRectangle(
                        appearance.X, appearance.Y,
                        appearance.X + appearance.Width,
                        appearance.Y + appearance.Height)
                };
            }

            // 4. Thực hiện ký (Basic / PAdES-T)
            await using var inputStream  = File.OpenRead(job.InputPath);
            await using var outputStream = File.Create(job.OutputPath);

            var engine  = new PdfSigningEngine();
            var request = new PdfSignRequest
            {
                InputPdf          = inputStream,
                OutputPdf         = outputStream,
                SignatureProvider = provider,
                Certificate       = certificate,
                CertificateChain  = chain,
                Reason            = opts.Reason,
                Location          = opts.Location,
                SubFilter         = opts.SubFilter,
                TsaClient         = tsaClient,
                Appearance        = appearanceReq
            };

            var result = await engine.SignAsync(request, ct);

            if (!result.Success)
            {
                job.Status       = JobStatus.Failed;
                job.ErrorMessage = result.ErrorMessage;
                _logger.LogError("{Input}: {Error}", job.InputPath, result.ErrorMessage);
                return false;
            }

            // 5. LTV/DSS — incremental update embed DSS
            if (opts.Level == SigningLevel.LtvDss && tsaClient != null)
            {
                await using var ltvInput  = File.OpenRead(job.OutputPath);
                var tmpPath = job.OutputPath + ".ltv.tmp";
                await using var ltvOutput = File.Create(tmpPath);

                var collector = new LtvDataCollector();
                var dssData   = await collector.CollectAsync(
                    result.SignatureBytes!, certificate, chain, tsaClient, ct);

                var dssWriter = new DssIncrementalWriter();
                await dssWriter.WriteAsync(ltvInput, ltvOutput, dssData, ct);

                ltvOutput.Close();
                ltvInput.Close();
                File.Move(tmpPath, job.OutputPath, overwrite: true);
            }

            job.Status = JobStatus.Success;
            _logger.LogInformation("OK  {Input} signed ({Level})",
                job.InputPath, opts.Level);
            return true;
        }
        catch (Exception ex)
        {
            job.Status       = JobStatus.Failed;
            job.ErrorMessage = ex.Message;
            _logger.LogError(ex, "ERROR {Input}", job.InputPath);
            return false;
        }
    }
}
```

### `LogService.cs`

Cầu nối giữa `ILogger` và `RichTextBox` console trên UI thread.

```csharp
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace PadesSharpDemoApp.Services;

public sealed class LogService : ILogger<object>
{
    private readonly RichTextBox _console;
    private readonly SynchronizationContext _uiCtx;

    public LogService(RichTextBox console)
    {
        _console = console;
        _uiCtx   = SynchronizationContext.Current
                   ?? new SynchronizationContext();
    }

    public void Log<TState>(LogLevel level, EventId eventId,
        TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var msg  = formatter(state, exception);
        var time = DateTime.Now.ToString("HH:mm:ss");
        var tag  = level switch
        {
            LogLevel.Error       => "ERROR",
            LogLevel.Warning     => "WARN ",
            LogLevel.Information => "INFO ",
            _                    => "DEBUG"
        };
        var color = level switch
        {
            LogLevel.Error   => Color.Tomato,
            LogLevel.Warning => Color.Gold,
            _                => Color.LightGreen
        };

        _uiCtx.Post(_ =>
        {
            _console.SelectionStart  = _console.TextLength;
            _console.SelectionLength = 0;
            _console.SelectionColor  = Color.Gray;
            _console.AppendText($"[{time}] ");
            _console.SelectionColor  = color;
            _console.AppendText($"{tag}  ");
            _console.SelectionColor  = Color.WhiteSmoke;
            _console.AppendText(msg + "\n");
            _console.ScrollToCaret();
        }, null);
    }

    public bool IsEnabled(LogLevel logLevel) => true;
    public IDisposable BeginScope<TState>(TState state) where TState : notnull
        => NullScope.Instance;

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
```

---

## 7. MainForm — Chi tiết điều khiển

### 7.1. Danh sách controls

| Control | Name | Loại | Mô tả |
|---------|------|------|-------|
| TabControl | `tcMain` | TabControl | 3 tab: Files, Options, Appearance |
| Tab 1 | `tabFiles` | TabPage | Chọn file đầu vào & đầu ra |
| ListView | `lvFiles` | ListView | Danh sách file cần ký (FullRow, MultiSelect) |
| Button | `btnAddFiles` | Button | Mở OpenFileDialog multi-select `*.pdf` |
| Button | `btnRemoveFiles` | Button | Xóa file đã chọn khỏi danh sách |
| Button | `btnClearFiles` | Button | Xóa tất cả |
| TextBox | `txtOutputFolder` | TextBox | Hiển thị thư mục đầu ra |
| Button | `btnBrowseOutput` | Button | Mở FolderBrowserDialog |
| CheckBox | `chkSameFolder` | CheckBox | Lưu cùng thư mục gốc (suffix `_signed`) |
| Tab 2 | `tabOptions` | TabPage | Tùy chọn ký |
| RadioButton | `rbBasic` | RadioButton | Level: Basic |
| RadioButton | `rbTsa` | RadioButton | Level: TSA |
| RadioButton | `rbLtv` | RadioButton | Level: LTV/DSS |
| GroupBox | `grpTsa` | GroupBox | Cấu hình TSA (enable khi rbTsa/rbLtv) |
| TextBox | `txtTsaUrl` | TextBox | URL TSA |
| TextBox | `txtTsaUser` | TextBox | Username TSA |
| TextBox | `txtTsaPass` | TextBox | Password TSA (`PasswordChar='●'`) |
| TextBox | `txtReason` | TextBox | Lý do ký |
| TextBox | `txtLocation` | TextBox | Địa điểm ký |
| Panel | `pnlCert` | Panel | Hiển thị thông tin chứng thư |
| Label | `lblCertSubject` | Label | CN chứng thư |
| Label | `lblCertIssuer` | Label | Issuer |
| Label | `lblCertSerial` | Label | Serial number |
| Label | `lblCertExpiry` | Label | Ngày hết hạn (đỏ nếu < 30 ngày) |
| Button | `btnPickCert` | Button | Mở Certificate Store picker |
| Tab 3 | `tabAppearance` | TabPage | Cấu hình chữ ký hiển thị |
| CheckBox | `chkShowAppearance` | CheckBox | Bật/tắt chữ ký trực quan |
| NumericUpDown | `nudPage` | NumericUpDown | Số trang (min 1) |
| NumericUpDown | `nudX` | NumericUpDown | Tọa độ X (pts, 0–999) |
| NumericUpDown | `nudY` | NumericUpDown | Tọa độ Y (pts, 0–999) |
| NumericUpDown | `nudWidth` | NumericUpDown | Chiều rộng |
| NumericUpDown | `nudHeight` | NumericUpDown | Chiều cao |
| CheckBox | `chkShowDate` | CheckBox | Hiển thị ngày ký |
| CheckBox | `chkShowReason` | CheckBox | Hiển thị lý do |
| CheckBox | `chkShowLocation` | CheckBox | Hiển thị địa điểm |
| TextBox | `txtLogoPath` | TextBox | Đường dẫn ảnh stamp |
| Button | `btnBrowseLogo` | Button | Mở OpenFileDialog `*.png;*.jpg` |
| Button | `btnClearLogo` | Button | Xóa ảnh đã chọn |
| PictureBox | `pbLogoPreview` | PictureBox | Preview ảnh stamp (64×64) |
| Panel | `pnlSignaturePreview` | Panel | Preview chữ ký (vẽ bằng OnPaint) |
| Button | `btnSign` | Button | Bắt đầu ký (primary action) |
| Button | `btnCancel` | Button | Dừng batch đang chạy |
| Button | `btnClearLog` | Button | Xóa console |
| RichTextBox | `rtbConsole` | RichTextBox | Output console, ReadOnly, BackColor=Black |
| ProgressBar | `progressBar` | ProgressBar | Tiến độ ký batch |
| StatusStrip | `statusStrip` | StatusStrip | Trạng thái tổng quát |
| ToolStripStatusLabel | `lblStatus` | ToolStripStatusLabel | "Ready" / "Signing 2/5..." / "Done" |

### 7.2. Keyboard Shortcuts

| Phím tắt | Chức năng |
|----------|-----------|
| Ctrl+O | Thêm file |
| Delete | Xóa file đã chọn trong ListView |
| F5 | Bắt đầu ký |
| Escape | Dừng |
| Ctrl+L | Xóa log |

---

## 8. Luồng xử lý (Workflow)

```
[Người dùng]
     │
     ├─ Kéo thả / btnAddFiles ──► lvFiles.Items.Add(...)
     │
     ├─ btnPickCert ──────────► CertificatePickerService.Pick()
     │                              └─► Cập nhật lblCertSubject/Issuer/Expiry
     │
     ├─ Chọn Level (Basic/TSA/LTV)
     │       └─► grpTsa.Enabled = (level != Basic)
     │
     ├─ Cấu hình Appearance ──► pnlSignaturePreview.Invalidate()
     │
     └─ btnSign.Click
            │
            ├─ Validate:
            │     • lvFiles không rỗng
            │     • Certificate đã chọn và còn hạn
            │     • OutputFolder tồn tại (hoặc tạo)
            │     • TsaUrl hợp lệ nếu level ≠ Basic
            │
            ├─ BuildBatchJobs() — tạo BatchSignJob[] từ lvFiles
            │
            ├─ progressBar.Maximum = jobs.Length
            │
            └─ foreach job in jobs (async, CancellationToken):
                    ├─ lvFiles item → màu vàng (đang ký)
                    ├─ SigningService.SignAsync(job, cert, chain, opts, appearance)
                    ├─ progressBar.Value++
                    └─ lvFiles item:
                          ✔ màu xanh (Success)
                          ✘ màu đỏ  (Failed, hiện lỗi tooltip)
```

---

## 9. Validation Rules

| Điều kiện | Thông báo |
|-----------|-----------|
| Không có file trong danh sách | "Vui lòng thêm ít nhất một file PDF để ký." |
| Chưa chọn chứng thư | "Vui lòng chọn chứng thư ký số." |
| Chứng thư hết hạn | "Chứng thư đã hết hạn vào {date}. Vui lòng chọn chứng thư khác." |
| Chứng thư không có private key | "Chứng thư được chọn không có private key. Kiểm tra lại thiết bị hoặc token." |
| Level TSA/LTV và TsaUrl rỗng | "Vui lòng nhập TSA URL để ký ở mức TSA/LTV." |
| TsaUrl không hợp lệ | "TSA URL không hợp lệ. Phải bắt đầu bằng https://." |
| OutputFolder không tồn tại | Hỏi người dùng: "Thư mục đầu ra không tồn tại, tạo mới?" |
| File đầu ra đã tồn tại | Hỏi: "File {name} đã tồn tại. Ghi đè?" (áp dụng cho tất cả/từng file) |

---

## 10. Output File Naming

| Cấu hình | Ví dụ input | Ví dụ output |
|----------|-------------|--------------|
| Same folder | `C:\docs\report.pdf` | `C:\docs\report_signed.pdf` |
| Custom folder | `C:\docs\report.pdf` | `D:\signed\report_signed.pdf` |
| Custom folder, level LTV | `C:\docs\report.pdf` | `D:\signed\report_signed_ltv.pdf` |

Suffix thêm vào trước phần mở rộng:
- Basic → `_signed`
- TSA   → `_signed_tsa`
- LTV   → `_signed_ltv`

---

## 11. Signature Preview Panel

`pnlSignaturePreview` vẽ realtime trong `OnPaint` theo các thông số `AppearanceOptions`:

```csharp
protected override void OnPaint(PaintEventArgs e)
{
    var g = e.Graphics;
    g.Clear(Color.White);
    // Vẽ border
    g.DrawRectangle(Pens.DarkBlue, 0, 0, Width - 1, Height - 1);

    // Nếu có logo, vẽ ở nửa trái
    if (_logo != null)
    {
        var logoRect = new Rectangle(4, 4, (Width / 2) - 8, Height - 8);
        g.DrawImage(_logo, logoRect);
    }

    // Vẽ text bên phải
    int textX = _logo != null ? Width / 2 : 8;
    using var font = new Font("Segoe UI", 7f);
    using var brush = new SolidBrush(Color.DarkBlue);
    int y = 6;
    g.DrawString($"Digitally signed by: {_signerName}", font, brush, textX, y); y += 14;
    if (_showDate)     { g.DrawString($"Date: {DateTime.Now:yyyy.MM.dd HH:mm}", font, brush, textX, y); y += 12; }
    if (_showReason)   { g.DrawString($"Reason: {_reason}",   font, brush, textX, y); y += 12; }
    if (_showLocation) { g.DrawString($"Location: {_location}", font, brush, textX, y); }
}
```

---

## 12. Drag & Drop support

`lvFiles` hỗ trợ kéo thả file từ Explorer:

```csharp
lvFiles.AllowDrop = true;
lvFiles.DragEnter += (s, e) =>
{
    if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        e.Effect = DragDropEffects.Copy;
};
lvFiles.DragDrop += (s, e) =>
{
    var files = (string[]?)e.Data?.GetData(DataFormats.FileDrop);
    if (files is null) return;
    foreach (var f in files.Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
        AddFileToList(f);
};
```

---

## 13. Settings Persistence (appsettings.json)

Lưu/khôi phục cấu hình giữa các lần chạy bằng `System.Text.Json`:

```json
{
  "LastOutputFolder": "C:\\Users\\...\\signed",
  "LastTsaUrl": "https://tsa.example.com/tsa",
  "LastSigningLevel": "Tsa",
  "LastLogoPath": "",
  "Appearance": {
    "Enabled": true,
    "PageNumber": 1,
    "X": 36,
    "Y": 36,
    "Width": 180,
    "Height": 60,
    "ShowDate": true,
    "ShowReason": true,
    "ShowLocation": true
  }
}
```

File lưu tại `%APPDATA%\PadesSharpDemo\settings.json`.

---

## 14. Error Handling & UX

- **Lỗi ký một file** → hiển thị đỏ trên ListView, ghi ERROR vào console, tiếp tục ký file tiếp theo (không dừng batch).
- **Dừng giữa chừng** → `CancellationTokenSource.Cancel()`, các file chưa ký giữ trạng thái Pending.
- **Unhandled exception** → `Application.SetUnhandledExceptionMode` + `AppDomain.CurrentDomain.UnhandledException` → ghi file `crash.log` vào `%APPDATA%\PadesSharpDemo\`.
- **Progress** → `IProgress<(int current, int total)>` truyền qua `async` để cập nhật `progressBar` và `lblStatus` trên UI thread.

---

## 15. Phụ thuộc & Tích hợp thư viện

| Namespace dùng | Thư viện nguồn |
|----------------|----------------|
| `ModernPdf.Signing.PdfSigningEngine` | `ModernPdf.Signing` |
| `ModernPdf.Abstractions.Signing.PdfSignRequest` | `ModernPdf.Abstractions` |
| `ModernPdf.Abstractions.Appearance.*` | `ModernPdf.Abstractions` |
| `ModernPdf.Crypto.RsaSoftwareSignatureProvider` | `ModernPdf.Crypto` |
| `ModernPdf.Crypto.Tsa.HttpTsaClient` | `ModernPdf.Crypto` |
| `ModernPdf.Pades.LtvDataCollector` | `ModernPdf.Pades` |
| `ModernPdf.Pades.DssIncrementalWriter` | `ModernPdf.Pades` |
| `ModernPdf.Appearance.DefaultPdfSignatureAppearanceBuilder` | `ModernPdf.Appearance` |

---

## 16. Kế hoạch triển khai (phases)

| Phase | Nội dung | Ưu tiên |
|-------|----------|---------|
| P1 | Tạo project, MainForm skeleton, FileList + Output folder | Cao |
| P2 | Certificate picker + info panel | Cao |
| P3 | SigningOptions panel (Basic/TSA/LTV, Reason, Location) | Cao |
| P4 | SigningService wiring + batch ký Basic | Cao |
| P5 | Console output + progress bar | Cao |
| P6 | Appearance panel + preview panel + logo picker | Trung bình |
| P7 | TSA integration | Trung bình |
| P8 | LTV/DSS integration | Trung bình |
| P9 | Settings persistence | Thấp |
| P10 | Drag & Drop, keyboard shortcuts | Thấp |
| P11 | Installer (WiX / publish self-contained) | Thấp |
