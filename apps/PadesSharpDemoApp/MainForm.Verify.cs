using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using ModernPdf.Abstractions.Validation;
using ModernPdf.Validation;

namespace PadesSharpDemoApp;

public partial class MainForm
{
    private readonly List<string> _verifyFiles = new();
    private CancellationTokenSource? _verifyCts;

    private TabControl _workflowTabs = null!;
    private TabPage _signTab = null!;
    private TabPage _verifyTab = null!;
    private ListView _verifyFileList = null!;
    private TreeView _verifyDetails = null!;
    private ComboBox _verifyPolicy = null!;
    private Label _verifyBanner = null!;
    private ProgressBar _verifyProgress = null!;
    private Button _verifyAdd = null!;
    private Button _verifyRemove = null!;
    private Button _verifyClear = null!;
    private Button _verifyStart = null!;
    private Button _verifyStop = null!;

    private void InitializeVerifyUi()
    {
        var signingLayout = Controls.OfType<TableLayoutPanel>().Single();
        Controls.Remove(signingLayout);

        _workflowTabs = new TabControl { Dock = DockStyle.Fill };
        _signTab = new TabPage { Padding = new Padding(0) };
        _verifyTab = new TabPage { Padding = new Padding(8) };
        _signTab.Controls.Add(signingLayout);
        _workflowTabs.TabPages.AddRange(new[] { _signTab, _verifyTab });
        Controls.Add(_workflowTabs);
        _workflowTabs.BringToFront();
        statusStrip.BringToFront();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(4)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 6)
        };
        _verifyAdd = new Button { AutoSize = true };
        _verifyRemove = new Button { AutoSize = true };
        _verifyClear = new Button { AutoSize = true };
        _verifyStart = new Button
        {
            AutoSize = true,
            MinimumSize = new Size(90, 0),
            Margin = new Padding(16, 3, 3, 3)
        };
        var policyLabel = new Label
        {
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(20, 7, 3, 0)
        };
        _verifyPolicy = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 230
        };
        _verifyPolicy.Items.AddRange(new object[] { VerifyPolicy.Integrity, VerifyPolicy.Standard, VerifyPolicy.Strict });
        _verifyPolicy.SelectedIndex = 1;
        toolbar.Controls.AddRange(new Control[]
        {
            _verifyAdd, _verifyRemove, _verifyClear, policyLabel, _verifyPolicy, _verifyStart
        });
        policyLabel.Name = "verifyPolicyLabel";

        _verifyBanner = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(10),
            BackColor = Color.FromArgb(235, 241, 248),
            ForeColor = Color.FromArgb(32, 55, 78)
        };

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 245,
            Panel1MinSize = 140,
            Panel2MinSize = 120
        };
        _verifyFileList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = true,
            AllowDrop = true
        };
        _verifyFileList.Columns.Add("File", 280);
        _verifyFileList.Columns.Add("Status", 130);
        _verifyFileList.Columns.Add("Signatures", 90);
        _verifyFileList.Columns.Add("Policy result", 160);
        _verifyDetails = new TreeView { Dock = DockStyle.Fill, HideSelection = false };
        split.Panel1.Controls.Add(_verifyFileList);
        split.Panel2.Controls.Add(_verifyDetails);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _verifyProgress = new ProgressBar { Dock = DockStyle.Fill, Height = 24, Margin = new Padding(0, 4, 10, 4) };
        var actionButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight
        };
        _verifyStop = new Button { AutoSize = true, Enabled = false };
        actionButtons.Controls.Add(_verifyStop);
        actions.Controls.Add(_verifyProgress, 0, 0);
        actions.Controls.Add(actionButtons, 1, 0);

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_verifyBanner, 0, 1);
        root.Controls.Add(split, 0, 2);
        root.Controls.Add(actions, 0, 3);
        _verifyTab.Controls.Add(root);

        _verifyAdd.Click += VerifyAdd_Click;
        _verifyRemove.Click += (_, _) => RemoveSelectedVerifyFiles();
        _verifyClear.Click += (_, _) => ClearVerifyFiles();
        _verifyStart.Click += VerifyStart_Click;
        _verifyStop.Click += (_, _) =>
        {
            _verifyCts?.Cancel();
            SetStatus(VerifyText("Stopping after the current file...", "Sẽ dừng sau file hiện tại..."));
        };
        _verifyFileList.SelectedIndexChanged += (_, _) => ShowSelectedVerifyDetails();
        _verifyFileList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete) RemoveSelectedVerifyFiles();
        };
        _verifyFileList.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        };
        _verifyFileList.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
                AddVerifyFiles(files);
        };
        _verifyPolicy.SelectedIndexChanged += (_, _) => UpdateVerifyBanner();
    }

    private void ApplyVerifyLanguage()
    {
        if (_workflowTabs is null) return;
        _signTab.Text = VerifyText("Sign", "Ký");
        _verifyTab.Text = VerifyText("Verify", "Xác minh");
        _verifyAdd.Text = VerifyText("Add PDFs...", "Thêm PDF...");
        _verifyRemove.Text = VerifyText("Remove", "Xóa");
        _verifyClear.Text = VerifyText("Clear", "Xóa tất cả");
        _verifyStart.Text = VerifyText("Start", "Bắt đầu");
        _verifyStop.Text = VerifyText("Stop", "Dừng");
        _verifyFileList.Columns[0].Text = VerifyText("File", "Tập tin");
        _verifyFileList.Columns[1].Text = VerifyText("Status", "Trạng thái");
        _verifyFileList.Columns[2].Text = VerifyText("Signatures", "Chữ ký");
        _verifyFileList.Columns[3].Text = VerifyText("Policy result", "Kết quả chính sách");
        if (_verifyTab.Controls[0] is TableLayoutPanel root &&
            root.Controls[0] is FlowLayoutPanel toolbar &&
            toolbar.Controls.Find("verifyPolicyLabel", false).FirstOrDefault() is Label label)
            label.Text = VerifyText("Policy:", "Chính sách:");
        UpdateVerifyBanner();
        ShowSelectedVerifyDetails();
    }

    private string VerifyText(string english, string vietnamese) =>
        AppLocale.Current.Language == AppLanguage.Vietnamese ? vietnamese : english;

    private void UpdateVerifyBanner()
    {
        var policy = SelectedVerifyPolicy;
        _verifyBanner.Text = policy switch
        {
            VerifyPolicy.Integrity => VerifyText(
                "Integrity policy: verifies document bytes and the CMS signature; trust and revocation do not decide the result. Add PDFs, then press Start.",
                "Chính sách toàn vẹn: kiểm tra nội dung và CMS; trust/revocation không quyết định kết quả. Thêm PDF, sau đó bấm Bắt đầu."),
            VerifyPolicy.Strict => VerifyText(
                "Strict policy: requires integrity, CMS, trusted certificate chain, timestamp validity when present, and known revocation status. Add PDFs, then press Start.",
                "Chính sách nghiêm ngặt: yêu cầu toàn vẹn, CMS, chuỗi tin cậy, timestamp hợp lệ và trạng thái thu hồi xác định. Thêm PDF, sau đó bấm Bắt đầu."),
            _ => VerifyText(
                "Standard policy: validates integrity, CMS, certificate period, timestamp when present, and embedded revocation evidence. Add PDFs, then press Start.",
                "Chính sách tiêu chuẩn: kiểm tra toàn vẹn, CMS, thời hạn chứng thư, timestamp và dữ liệu thu hồi nhúng. Thêm PDF, sau đó bấm Bắt đầu.")
        };
    }

    private VerifyPolicy SelectedVerifyPolicy =>
        _verifyPolicy.SelectedItem is VerifyPolicy policy ? policy : VerifyPolicy.Standard;

    private void VerifyAdd_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = VerifyText("Select PDF files to verify", "Chọn PDF cần xác minh"),
            Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) AddVerifyFiles(dialog.FileNames);
    }

    private void AddVerifyFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(File.Exists).Where(p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
        {
            var fullPath = Path.GetFullPath(path);
            if (_verifyFiles.Contains(fullPath, StringComparer.OrdinalIgnoreCase)) continue;
            _verifyFiles.Add(fullPath);
            var item = new ListViewItem(Path.GetFileName(fullPath)) { Tag = new VerifyFileState(fullPath) };
            item.SubItems.Add(VerifyText("Waiting", "Đang chờ"));
            item.SubItems.Add("—");
            item.SubItems.Add("—");
            _verifyFileList.Items.Add(item);
        }
    }

    private void RemoveSelectedVerifyFiles()
    {
        foreach (ListViewItem item in _verifyFileList.SelectedItems.Cast<ListViewItem>().ToArray())
        {
            if (item.Tag is VerifyFileState state) _verifyFiles.Remove(state.Path);
            _verifyFileList.Items.Remove(item);
        }
        _verifyDetails.Nodes.Clear();
    }

    private void ClearVerifyFiles()
    {
        _verifyFiles.Clear();
        _verifyFileList.Items.Clear();
        _verifyDetails.Nodes.Clear();
    }

    private async void VerifyStart_Click(object? sender, EventArgs e)
    {
        if (_verifyFileList.Items.Count == 0)
        {
            MessageBox.Show(this, VerifyText("Add at least one PDF file.", "Hãy thêm ít nhất một file PDF."),
                VerifyText("Verify", "Xác minh"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var policy = SelectedVerifyPolicy;
        var options = CreateValidationOptions(policy);
        _verifyCts?.Dispose();
        _verifyCts = new CancellationTokenSource();
        var token = _verifyCts.Token;
        SetVerifyBusy(true);
        _verifyProgress.Minimum = 0;
        _verifyProgress.Maximum = _verifyFileList.Items.Count;
        _verifyProgress.Value = 0;
        var passed = 0;
        var failed = 0;

        try
        {
            for (var index = 0; index < _verifyFileList.Items.Count; index++)
            {
                if (token.IsCancellationRequested) break;
                var item = _verifyFileList.Items[index];
                if (item.Tag is not VerifyFileState state) continue;
                item.SubItems[1].Text = VerifyText("Verifying...", "Đang xác minh...");
                _logger.LogInformation("[VERIFY] Verifying {Path}", state.Path);
                SetStatus(VerifyText($"Verifying {index + 1}/{_verifyFileList.Items.Count}",
                    $"Xác minh {index + 1}/{_verifyFileList.Items.Count}"));

                try
                {
                    var report = await Task.Run(() =>
                    {
                        var validator = new DefaultPdfSignatureValidator();
                        return PdfValidationFileHelper.ValidateFile(state.Path, validator, options);
                    });
                    state.Report = report;
                    state.Policy = policy;
                    state.PolicyPassed = EvaluatePolicy(report, policy);
                    item.SubItems[1].Text = report.Signatures.Count == 0
                        ? VerifyText("Unsigned", "Chưa ký")
                        : VerifyText("Complete", "Hoàn thành");
                    item.SubItems[2].Text = report.Signatures.Count.ToString();
                    item.SubItems[3].Text = state.PolicyPassed
                        ? VerifyText("Passed", "Đạt")
                        : VerifyText("Failed", "Không đạt");
                    item.ForeColor = state.PolicyPassed ? Color.DarkGreen : Color.DarkRed;
                    _logger.LogInformation("[VERIFY] {Path}: {Result} ({SignatureCount} signature(s), {Policy})",
                        state.Path, state.PolicyPassed ? "passed" : "failed", report.Signatures.Count, policy);
                    if (state.PolicyPassed) passed++; else failed++;
                }
                catch (Exception ex)
                {
                    state.Error = ex.Message;
                    state.PolicyPassed = false;
                    item.SubItems[1].Text = VerifyText("Error", "Lỗi");
                    item.SubItems[3].Text = VerifyText("Failed", "Không đạt");
                    item.ForeColor = Color.DarkRed;
                    _logger.LogError(ex, "[VERIFY] Error verifying {Path}", state.Path);
                    failed++;
                }

                _verifyProgress.Value = index + 1;
                if (item.Selected) ShowSelectedVerifyDetails();
            }
        }
        finally
        {
            SetVerifyBusy(false);
            var summary = VerifyText($"Verification complete: {passed} passed, {failed} failed.",
                $"Xác minh hoàn tất: {passed} đạt, {failed} không đạt.");
            SetStatus(token.IsCancellationRequested ? VerifyText("Verification stopped.", "Đã dừng xác minh.") : summary);
        }
    }

    private static PdfValidationOptions CreateValidationOptions(VerifyPolicy policy) => policy switch
    {
        VerifyPolicy.Integrity => new PdfValidationOptions
        {
            ValidateCertificateChain = false,
            ValidateChainTrust = false,
            ValidateRevocation = false,
            ValidateTimestamp = false,
            UseEmbeddedDss = false,
            AllowOnlineRevocationFallback = false
        },
        VerifyPolicy.Strict => new PdfValidationOptions
        {
            ValidateCertificateChain = true,
            ValidateChainTrust = true,
            ValidateRevocation = true,
            AllowUnknownRevocationStatus = false,
            ValidateTimestamp = true,
            ValidateTimestampMessageImprint = true,
            ValidateTimestampCertificatePeriod = true,
            ValidateTimestampChainTrust = true,
            ValidateTimestampRevocation = true,
            UseEmbeddedDss = true,
            AllowOnlineRevocationFallback = false,
            ValidateEntireCertificateChainRevocation = true
        },
        _ => new PdfValidationOptions
        {
            ValidateCertificateChain = true,
            ValidateChainTrust = false,
            ValidateRevocation = true,
            AllowUnknownRevocationStatus = true,
            ValidateTimestamp = true,
            ValidateTimestampMessageImprint = true,
            ValidateTimestampCertificatePeriod = true,
            UseEmbeddedDss = true,
            AllowOnlineRevocationFallback = false
        }
    };

    internal static bool EvaluatePolicy(PdfValidationReport report, VerifyPolicy policy)
    {
        if (report.Signatures.Count == 0) return false;
        return report.Signatures.All(signature => policy switch
        {
            VerifyPolicy.Integrity => signature.DocumentIntegrityValid && signature.CmsSignatureValid,
            VerifyPolicy.Strict => signature.DocumentIntegrityValid && signature.CmsSignatureValid &&
                                   signature.CertificatePeriodValid && signature.CertificateChainTrusted &&
                                   signature.RevocationValid && (!signature.TimestampPresent || signature.TimestampValid),
            _ => signature.DocumentIntegrityValid && signature.CmsSignatureValid &&
                 signature.CertificatePeriodValid && (!signature.TimestampPresent || signature.TimestampValid)
        });
    }

    private void ShowSelectedVerifyDetails()
    {
        _verifyDetails.BeginUpdate();
        _verifyDetails.Nodes.Clear();
        if (_verifyFileList.SelectedItems.Count == 0 ||
            _verifyFileList.SelectedItems[0].Tag is not VerifyFileState state)
        {
            _verifyDetails.EndUpdate();
            return;
        }

        var fileNode = _verifyDetails.Nodes.Add(Path.GetFileName(state.Path));
        fileNode.Nodes.Add($"{VerifyText("Path", "Đường dẫn")}: {state.Path}");
        fileNode.Nodes.Add($"{VerifyText("Policy", "Chính sách")}: {PolicyDisplayName(state.Policy)}");
        if (!string.IsNullOrWhiteSpace(state.Error))
            fileNode.Nodes.Add($"{VerifyText("Error", "Lỗi")}: {state.Error}");
        if (state.Report is not null)
        {
            fileNode.Nodes.Add($"{VerifyText("Policy result", "Kết quả chính sách")}: {PassFail(state.PolicyPassed)}");
            if (state.Report.Signatures.Count == 0)
                fileNode.Nodes.Add(VerifyText("No PDF signatures were found.", "Không tìm thấy chữ ký PDF."));
            foreach (var signature in state.Report.Signatures)
            {
                var signatureNode = fileNode.Nodes.Add(string.IsNullOrWhiteSpace(signature.SignatureName)
                    ? VerifyText("Signature", "Chữ ký")
                    : signature.SignatureName);
                signatureNode.Nodes.Add($"{VerifyText("Signer", "Người ký")}: {DisplayValue(signature.SignerSubject)}");
                signatureNode.Nodes.Add($"{VerifyText("Issuer", "Tổ chức phát hành")}: {DisplayValue(signature.SignerIssuer)}");
                signatureNode.Nodes.Add($"{VerifyText("Signed revision length", "Kích thước phiên bản đã ký")}: {signature.SignedRevisionLength:N0} bytes");
                signatureNode.Nodes.Add($"{VerifyText("Covers current document", "Bao phủ tài liệu hiện tại")}: {YesNo(signature.CoversWholeDocument)}");
                signatureNode.Nodes.Add($"{VerifyText("Document integrity", "Tính toàn vẹn tài liệu")}: {PassFail(signature.DocumentIntegrityValid)}");
                signatureNode.Nodes.Add($"{VerifyText("CMS signature", "Chữ ký CMS")}: {PassFail(signature.CmsSignatureValid)}");
                signatureNode.Nodes.Add($"{VerifyText("Certificate period", "Thời hạn chứng thư")}: {PassFail(signature.CertificatePeriodValid)}");
                signatureNode.Nodes.Add($"{VerifyText("Certificate chain trusted", "Chuỗi chứng thư tin cậy")}: {PassFail(signature.CertificateChainTrusted)}");
                signatureNode.Nodes.Add($"{VerifyText("Timestamp present", "Có dấu thời gian")}: {YesNo(signature.TimestampPresent)}");
                signatureNode.Nodes.Add($"{VerifyText("Timestamp valid", "Dấu thời gian hợp lệ")}: {PassFail(signature.TimestampValid)}");
                signatureNode.Nodes.Add($"{VerifyText("Revocation", "Thu hồi chứng thư")}: {PassFail(signature.RevocationValid)} ({RevocationSourceName(signature.RevocationSource)})");
                AddMessages(signatureNode, VerifyText("Errors", "Lỗi"), signature.Errors);
                AddMessages(signatureNode, VerifyText("Warnings", "Cảnh báo"), signature.Warnings);
            }
        }
        fileNode.Expand();
        _verifyDetails.EndUpdate();
    }

    private string PassFail(bool value) => value
        ? VerifyText("Passed", "Đạt")
        : VerifyText("Failed", "Không đạt");

    private string YesNo(bool value) => value
        ? VerifyText("Yes", "Có")
        : VerifyText("No", "Không");

    private string DisplayValue(string value) =>
        string.IsNullOrWhiteSpace(value) ? VerifyText("Unavailable", "Không có dữ liệu") : value;

    private string PolicyDisplayName(VerifyPolicy policy) => policy switch
    {
        VerifyPolicy.Integrity => VerifyText("Integrity", "Toàn vẹn"),
        VerifyPolicy.Strict => VerifyText("Strict", "Nghiêm ngặt"),
        _ => VerifyText("Standard", "Tiêu chuẩn")
    };

    private string RevocationSourceName(ModernPdf.Abstractions.Validation.RevocationSource source) => source switch
    {
        ModernPdf.Abstractions.Validation.RevocationSource.Dss => VerifyText("Embedded DSS", "DSS nhúng"),
        ModernPdf.Abstractions.Validation.RevocationSource.OcspOnline => VerifyText("Online OCSP", "OCSP trực tuyến"),
        ModernPdf.Abstractions.Validation.RevocationSource.CrlOnline => VerifyText("Online CRL", "CRL trực tuyến"),
        _ => VerifyText("None", "Không có")
    };

    private static void AddMessages(TreeNode parent, string title, IReadOnlyList<string> messages)
    {
        if (messages.Count == 0) return;
        var node = parent.Nodes.Add(title);
        foreach (var message in messages) node.Nodes.Add(message);
    }

    private void SetVerifyBusy(bool busy)
    {
        _verifyStart.Enabled = !busy;
        _verifyStop.Enabled = busy;
        _verifyAdd.Enabled = !busy;
        _verifyRemove.Enabled = !busy;
        _verifyClear.Enabled = !busy;
        _verifyPolicy.Enabled = !busy;
    }

    internal enum VerifyPolicy
    {
        Integrity,
        Standard,
        Strict
    }

    private sealed class VerifyFileState
    {
        public VerifyFileState(string path) => Path = path;
        public string Path { get; }
        public VerifyPolicy Policy { get; set; } = VerifyPolicy.Standard;
        public PdfValidationReport? Report { get; set; }
        public bool PolicyPassed { get; set; }
        public string? Error { get; set; }
    }
}
