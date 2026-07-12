using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using PadesSharpDemoApp.Models;
using PadesSharpDemoApp.Services;
using PadesSharpDemoApp.UI;

namespace PadesSharpDemoApp;

public partial class MainForm : Form
{
    // ── State ────────────────────────────────────────────────────────────────
    private X509Certificate2?               _selectedCert;
    private IReadOnlyList<X509Certificate2>? _certChain;
    private CancellationTokenSource?        _cts;
    private ILogger                         _logger = null!;
    private AppSettings                     _settings = null!;
    private Image?                          _logoImage;

    public MainForm()
    {
        InitializeComponent();
        InitializeVerifyUi();
        WireEvents();
    }

    // ════════════════════════════════════════════════════════════════════════
    // Form Load / Close
    // ════════════════════════════════════════════════════════════════════════

    private void MainForm_Load(object? sender, EventArgs e)
    {
        // Set window icon from programmatically-rendered logo
        using var iconBmp = LogoRenderer.CreateIconBitmap(32);
        Icon = LogoRenderer.BitmapToIcon(iconBmp);

        _logger   = new RichTextBoxLogger(rtbConsole);
        _settings = SettingsService.Load();
        ApplySettings(_settings);
        ApplyLanguage();
        RefreshPreview();
        SetStatus(AppLocale.Current.StatusReady);
        _logger.LogInformation(AppLocale.Current.LogStartup);
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        SaveSettings();
        _cts?.Cancel();
    }

    // ════════════════════════════════════════════════════════════════════════
    // Wire events
    // ════════════════════════════════════════════════════════════════════════

    private void WireEvents()
    {
        Load        += MainForm_Load;
        FormClosing += MainForm_FormClosing;
        KeyPreview   = true;
        KeyDown     += MainForm_KeyDown;

        // File list
        btnAddFiles.Click    += BtnAddFiles_Click;
        btnRemoveFiles.Click += BtnRemoveFiles_Click;
        btnClearFiles.Click  += BtnClearFiles_Click;
        lvFiles.DragEnter    += LvFiles_DragEnter;
        lvFiles.DragDrop     += LvFiles_DragDrop;
        lvFiles.KeyDown      += LvFiles_KeyDown;

        // Output
        btnBrowseOutput.Click += BtnBrowseOutput_Click;
        chkSameFolder.CheckedChanged += (_, _) => {
            bool useSame = chkSameFolder.Checked;
            txtOutputFolder.Enabled = !useSame;
            btnBrowseOutput.Enabled = !useSame;
        };

        // Certificate
        btnPickCert.Click += BtnPickCert_Click;

        // Signing level
        rbBasic.CheckedChanged += SigningLevel_Changed;
        rbTsa.CheckedChanged   += SigningLevel_Changed;
        rbLtv.CheckedChanged   += SigningLevel_Changed;

        // Appearance
        chkShowAppearance.CheckedChanged += (_, _) => ToggleAppearanceControls();
        nudPage.ValueChanged   += (_, _) => RefreshPreview();
        nudX.ValueChanged      += (_, _) => RefreshPreview();
        nudY.ValueChanged      += (_, _) => RefreshPreview();
        nudWidth.ValueChanged  += (_, _) => RefreshPreview();
        nudHeight.ValueChanged += (_, _) => RefreshPreview();
        chkShowDate.CheckedChanged     += (_, _) => RefreshPreview();
        chkShowReason.CheckedChanged   += (_, _) => RefreshPreview();
        chkShowLocation.CheckedChanged += (_, _) => RefreshPreview();
        txtReason.TextChanged          += (_, _) => RefreshPreview();
        txtLocation.TextChanged        += (_, _) => RefreshPreview();

        btnBrowseLogo.Click += BtnBrowseLogo_Click;
        btnClearLogo.Click  += BtnClearLogo_Click;

        // Actions
        btnSign.Click     += BtnSign_Click;
        btnStopSign.Click += (_, _) => { _cts?.Cancel(); SetStatus(AppLocale.Current.StatusStopping); };
        btnClearLog.Click += (_, _) => rtbConsole.Clear();

        // Language toggle
        foreach (ToolStripMenuItem item in tsddLang.DropDownItems)
            item.Click += LangMenuItem_Click;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Keyboard shortcuts
    // ════════════════════════════════════════════════════════════════════════

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.O) { BtnAddFiles_Click(sender!, e); e.Handled = true; }
        if (e.KeyCode == Keys.F5 && btnSign.Enabled) { BtnSign_Click(sender!, e); e.Handled = true; }
        if (e.KeyCode == Keys.Escape && btnStopSign.Enabled) _cts?.Cancel();
        if (e.Control && e.KeyCode == Keys.L) rtbConsole.Clear();
    }

    private void LvFiles_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete) BtnRemoveFiles_Click(sender!, e);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Language
    // ════════════════════════════════════════════════════════════════════════

    private void LangMenuItem_Click(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem item || item.Tag is not AppLanguage lang) return;
        AppLocale.Set(lang);
        ApplyLanguage();
        RefreshPreview();
    }

    private void ApplyLanguage()
    {
        var L = AppLocale.Current;
        Text             = L.FormTitle;
        tsddLang.Text    = L.LangToggleText;

        // Groups
        grpFiles.Text      = L.GrpFiles;
        grpOutput.Text     = L.GrpOutput;
        grpCert.Text       = L.GrpCert;
        grpSignOpts.Text   = L.GrpSignOpts;
        grpAppearance.Text = L.GrpAppearance;
        grpTsa.Text        = L.GrpTsa;

        // File buttons + columns
        btnAddFiles.Text    = L.BtnAddFiles;
        btnRemoveFiles.Text = L.BtnRemoveFiles;
        btnClearFiles.Text  = L.BtnClearFiles;
        lvFiles.Columns[0].Text = L.ColFileName;
        lvFiles.Columns[1].Text = L.ColSize;
        lvFiles.Columns[2].Text = L.ColFolder;
        lvFiles.Columns[3].Text = L.ColStatus;

        // Output
        btnBrowseOutput.Text = L.BtnBrowseOutput;
        chkSameFolder.Text   = L.ChkSameFolder;

        // Certificate
        btnPickCert.Text = L.BtnPickCert;
        // Reset default labels if no cert is selected
        if (_selectedCert is null)
        {
            lblCertSubject.Text = L.CertSubjectDefault;
            lblCertIssuer.Text  = L.CertIssuerDefault;
            lblCertSerial.Text  = L.CertSerialDefault;
            lblCertExpiry.Text  = L.CertExpiryDefault;
        }
        else
        {
            lblCertExpiry.Text = L.CertExpiryPrefix + _selectedCert.NotAfter.ToString("dd/MM/yyyy");
        }

        // Signing options
        rbBasic.Text   = L.RbBasic;
        rbTsa.Text     = L.RbTsa;
        rbLtv.Text     = L.RbLtv;
        lblReason.Text   = L.LblReason;
        lblLocation.Text = L.LblLocation;

        // Appearance
        chkShowAppearance.Text = L.ChkShowAppearance;
        lblPage.Text    = L.LblPage;
        lblX.Text       = L.LblX;
        lblY.Text       = L.LblY;
        lblWidth.Text   = L.LblWidth;
        lblHeight.Text  = L.LblHeight;
        chkShowDate.Text     = L.ChkShowDate;
        chkShowReason.Text   = L.ChkShowReason;
        chkShowLocation.Text = L.ChkShowLocation;
        lblLogo.Text         = L.LblLogo;

        // Actions
        btnClearLog.Text  = L.BtnClearLog;
        btnStopSign.Text  = L.BtnStopSign;
        btnSign.Text      = L.BtnSign;
        ApplyVerifyLanguage();

        // Status label: refresh with Ready only if not in a different state
        if (!btnStopSign.Enabled)
            SetStatus(L.StatusReady);
    }

    // ════════════════════════════════════════════════════════════════════════
    // File list
    // ════════════════════════════════════════════════════════════════════════

    private void BtnAddFiles_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title       = AppLocale.Current.DlgPickFilesTitle,
            Filter      = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            foreach (var f in dlg.FileNames)
                AddFileToList(f);
    }

    private void BtnRemoveFiles_Click(object? sender, EventArgs e)
    {
        foreach (ListViewItem item in lvFiles.SelectedItems)
            lvFiles.Items.Remove(item);
    }

    private void BtnClearFiles_Click(object? sender, EventArgs e) => lvFiles.Items.Clear();

    private void LvFiles_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    private void LvFiles_DragDrop(object? sender, DragEventArgs e)
    {
        var files = (string[]?)e.Data?.GetData(DataFormats.FileDrop);
        if (files is null) return;
        foreach (var f in files.Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
            AddFileToList(f);
    }

    private void AddFileToList(string path)
    {
        if (lvFiles.Items.Cast<ListViewItem>().Any(i => i.Tag as string == path))
            return;

        var fi   = new FileInfo(path);
        var item = new ListViewItem(fi.Name);
        item.SubItems.Add(FormatFileSize(fi.Length));
        item.SubItems.Add(fi.DirectoryName ?? "");
        item.SubItems.Add(AppLocale.Current.StatusWaiting);
        item.Tag = path;
        lvFiles.Items.Add(item);
    }

    private static string FormatFileSize(long bytes) =>
        bytes < 1024 ? $"{bytes} B"
        : bytes < 1_048_576 ? $"{bytes / 1024} KB"
        : $"{bytes / 1_048_576.0:F1} MB";

    // ════════════════════════════════════════════════════════════════════════
    // Output folder
    // ════════════════════════════════════════════════════════════════════════

    private void BtnBrowseOutput_Click(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description            = AppLocale.Current.DlgOutputDesc,
            UseDescriptionForTitle = true,
            SelectedPath           = txtOutputFolder.Text,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            txtOutputFolder.Text = dlg.SelectedPath;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Certificate
    // ════════════════════════════════════════════════════════════════════════

    private void BtnPickCert_Click(object? sender, EventArgs e)
    {
        try
        {
            var cert = CertificatePickerService.Pick();
            if (cert is null) return;

            _selectedCert = cert;
            _certChain    = CertificatePickerService.BuildChain(cert);

            lblCertSubject.Text = "Subject: " + cert.GetNameInfo(X509NameType.SimpleName, false);
            lblCertIssuer.Text  = "Issuer:  " + cert.GetNameInfo(X509NameType.SimpleName, true);
            lblCertSerial.Text  = "Serial:  " + cert.SerialNumber;

            var expiry = cert.NotAfter;
            lblCertExpiry.Text      = AppLocale.Current.CertExpiryPrefix + expiry.ToString("dd/MM/yyyy");
            lblCertExpiry.ForeColor = expiry < DateTime.Now.AddDays(30)
                ? Color.Tomato : SystemColors.ControlText;

            string subject = cert.GetNameInfo(X509NameType.SimpleName, false);
            _logger.LogInformation("Cert: {Subject} | expires {Expiry:dd/MM/yyyy}", subject, expiry);
            RefreshPreview();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi chọn chứng thư");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Signing level
    // ════════════════════════════════════════════════════════════════════════

    private void SigningLevel_Changed(object? sender, EventArgs e)
    {
        grpTsa.Enabled = rbTsa.Checked || rbLtv.Checked;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Appearance
    // ════════════════════════════════════════════════════════════════════════

    private void ToggleAppearanceControls()
    {
        bool on = chkShowAppearance.Checked;
        pnlAppControls.Enabled = on;
        sigPreview.Enabled     = on;
    }

    private void BtnBrowseLogo_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title  = AppLocale.Current.DlgPickLogoTitle,
            Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _logoImage?.Dispose();
            _logoImage          = Image.FromFile(dlg.FileName);
            txtLogoPath.Text    = dlg.FileName;
            pbLogoPreview.Image = _logoImage;
            RefreshPreview();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không thể tải ảnh: {Path}", dlg.FileName);
        }
    }

    private void BtnClearLogo_Click(object? sender, EventArgs e)
    {
        _logoImage?.Dispose();
        _logoImage          = null;
        txtLogoPath.Text    = "";
        pbLogoPreview.Image = null;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        sigPreview.Update(
            signerName:   _selectedCert?.GetNameInfo(X509NameType.SimpleName, false),
            reason:       txtReason.Text,
            location:     txtLocation.Text,
            showDate:     chkShowDate.Checked,
            showReason:   chkShowReason.Checked,
            showLocation: chkShowLocation.Checked,
            logo:         _logoImage
        );
    }

    // ════════════════════════════════════════════════════════════════════════
    // SIGN BATCH
    // ════════════════════════════════════════════════════════════════════════

    private async void BtnSign_Click(object? sender, EventArgs e)
    {
        if (!ValidateBeforeSigning()) return;

        var jobs = BuildBatchJobs();
        if (jobs.Count == 0)
        {
            MessageBox.Show(AppLocale.Current.MsgNoFiles, AppLocale.Current.MsgInfoTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var opts       = CollectSigningOptions();
        var appearance = CollectAppearanceOptions();

        SaveSettings();
        _cts = new CancellationTokenSource();
        SetSigningUiState(isRunning: true);
        progressBar.Maximum = jobs.Count;
        progressBar.Value   = 0;

        var svc = new SigningService(_logger);
        int success = 0, failed = 0;
        SetStatus(AppLocale.Current.StatusSigningProgress(0, jobs.Count));

        for (int i = 0; i < jobs.Count; i++)
        {
            if (_cts.Token.IsCancellationRequested) break;

            var job  = jobs[i];
            var item = lvFiles.Items.Cast<ListViewItem>()
                .First(x => (x.Tag as string) == job.InputPath);

            SetListItemStatus(item, AppLocale.Current.StatusSigning, Color.Gold, Color.Black);

            bool ok = await svc.SignAsync(job, _selectedCert!, _certChain!,
                opts, appearance, _cts.Token);

            progressBar.Value = i + 1;
            SetStatus(AppLocale.Current.StatusSigningProgress(i + 1, jobs.Count));

            if (ok)
            {
                SetListItemStatus(item, AppLocale.Current.StatusDone, Color.DarkGreen, Color.White);
                success++;
            }
            else
            {
                SetListItemStatus(item, $"✘ {job.ErrorMessage}", Color.DarkRed, Color.White);
                failed++;
            }
        }

        SetSigningUiState(isRunning: false);
        _cts.Dispose();
        _cts = null;

        string summary = AppLocale.Current.MsgComplete(success, failed);
        SetStatus(summary);
        _logger.LogInformation(summary);

        if (failed > 0)
            MessageBox.Show(summary, AppLocale.Current.MsgSignResultTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Validation
    // ════════════════════════════════════════════════════════════════════════

    private bool ValidateBeforeSigning()
    {
        var L = AppLocale.Current;

        if (lvFiles.Items.Count == 0) { ShowError(L.ErrNoFiles); return false; }

        if (_selectedCert is null) { ShowError(L.ErrNoCert); return false; }

        if (_selectedCert.NotAfter < DateTime.Now)
        {
            ShowError(L.ErrCertExpired(_selectedCert.NotAfter.ToString("dd/MM/yyyy")));
            return false;
        }

        if (!_selectedCert.HasPrivateKey) { ShowError(L.ErrNoPrivKey); return false; }

        bool needsTsa = rbTsa.Checked || rbLtv.Checked;
        if (needsTsa && string.IsNullOrWhiteSpace(txtTsaUrl.Text)) { ShowError(L.ErrNoTsaUrl); return false; }

        if (needsTsa && !string.IsNullOrWhiteSpace(txtTsaUrl.Text)
            && !txtTsaUrl.Text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !txtTsaUrl.Text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            ShowError(L.ErrInvalidTsaUrl);
            return false;
        }

        if (!chkSameFolder.Checked)
        {
            string folder = txtOutputFolder.Text.Trim();
            if (string.IsNullOrEmpty(folder)) { ShowError(L.ErrNoOutput); return false; }
            if (!Directory.Exists(folder))
            {
                var res = MessageBox.Show(L.MsgCreateFolder(folder),
                    L.MsgConfirmTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (res != DialogResult.Yes) return false;
                Directory.CreateDirectory(folder);
            }
        }
        return true;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Build jobs & options
    // ════════════════════════════════════════════════════════════════════════

    private List<BatchSignJob> BuildBatchJobs()
    {
        var level    = GetSelectedLevel();
        bool same    = chkSameFolder.Checked;
        string? outFolder = same ? null : txtOutputFolder.Text.Trim();

        var jobs = new List<BatchSignJob>();
        foreach (ListViewItem item in lvFiles.Items)
        {
            string input  = (item.Tag as string)!;
            string output = SigningService.BuildOutputPath(input, outFolder, same, level);
            jobs.Add(new BatchSignJob { InputPath = input, OutputPath = output });
        }
        return jobs;
    }

    private SigningOptions CollectSigningOptions() => new()
    {
        Level    = GetSelectedLevel(),
        TsaUrl   = txtTsaUrl.Text.Trim(),
        TsaUser  = txtTsaUser.Text.Trim(),
        TsaPass  = txtTsaPass.Text,
        Reason   = txtReason.Text.Trim(),
        Location = txtLocation.Text.Trim(),
    };

    private AppearanceOptions CollectAppearanceOptions()
    {
        byte[]? logoBytes = null;
        if (!string.IsNullOrEmpty(txtLogoPath.Text) && File.Exists(txtLogoPath.Text))
            logoBytes = File.ReadAllBytes(txtLogoPath.Text);

        return new AppearanceOptions
        {
            Enabled        = chkShowAppearance.Checked,
            PageNumber     = (int)nudPage.Value,
            X              = (float)nudX.Value,
            Y              = (float)nudY.Value,
            Width          = (float)nudWidth.Value,
            Height         = (float)nudHeight.Value,
            ShowDate       = chkShowDate.Checked,
            ShowReason     = chkShowReason.Checked,
            ShowLocation   = chkShowLocation.Checked,
            LogoImageBytes = logoBytes,
            LogoImagePath  = txtLogoPath.Text,
        };
    }

    private SigningLevel GetSelectedLevel()
    {
        if (rbTsa.Checked) return SigningLevel.Tsa;
        if (rbLtv.Checked) return SigningLevel.LtvDss;
        return SigningLevel.Basic;
    }

    // ════════════════════════════════════════════════════════════════════════
    // UI helpers
    // ════════════════════════════════════════════════════════════════════════

    private void SetSigningUiState(bool isRunning)
    {
        btnSign.Enabled        = !isRunning;
        btnStopSign.Enabled    = isRunning;
        btnAddFiles.Enabled    = !isRunning;
        btnRemoveFiles.Enabled = !isRunning;
        btnClearFiles.Enabled  = !isRunning;
        btnPickCert.Enabled    = !isRunning;
        if (!isRunning) progressBar.Value = 0;
    }

    private void SetListItemStatus(ListViewItem item, string status,
        Color backColor, Color foreColor)
    {
        if (InvokeRequired) { Invoke(() => SetListItemStatus(item, status, backColor, foreColor)); return; }
        item.SubItems[3].Text = status;
        item.BackColor = backColor;
        item.ForeColor = foreColor;
    }

    private void SetStatus(string text)
    {
        if (InvokeRequired) { Invoke(() => SetStatus(text)); return; }
        lblStatus.Text = text;
    }

    private void ShowError(string msg) =>
        MessageBox.Show(msg, AppLocale.Current.ErrTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    // ════════════════════════════════════════════════════════════════════════
    // Settings
    // ════════════════════════════════════════════════════════════════════════

    private void ApplySettings(AppSettings s)
    {
        if (!string.IsNullOrEmpty(s.LastOutputFolder)) txtOutputFolder.Text = s.LastOutputFolder;
        if (!string.IsNullOrEmpty(s.LastTsaUrl))       txtTsaUrl.Text       = s.LastTsaUrl;
        if (!string.IsNullOrEmpty(s.LastTsaUser))      txtTsaUser.Text      = s.LastTsaUser;

        rbBasic.Checked = s.LastSigningLevel == "Basic";
        rbTsa.Checked   = s.LastSigningLevel == "Tsa";
        rbLtv.Checked   = s.LastSigningLevel == "LtvDss";
        if (!rbBasic.Checked && !rbTsa.Checked && !rbLtv.Checked)
            rbBasic.Checked = true;

        if (!string.IsNullOrEmpty(s.LastReason))   txtReason.Text   = s.LastReason;
        if (!string.IsNullOrEmpty(s.LastLocation)) txtLocation.Text = s.LastLocation;

        chkShowAppearance.Checked = s.AppearanceEnabled;
        nudPage.Value   = Math.Max(1, s.AppearancePage);
        nudX.Value      = (decimal)s.AppearanceX;
        nudY.Value      = (decimal)s.AppearanceY;
        nudWidth.Value  = (decimal)s.AppearanceWidth;
        nudHeight.Value = (decimal)s.AppearanceHeight;
        chkShowDate.Checked     = s.AppearanceShowDate;
        chkShowReason.Checked   = s.AppearanceShowReason;
        chkShowLocation.Checked = s.AppearanceShowLocation;

        if (!string.IsNullOrEmpty(s.LastLogoPath) && File.Exists(s.LastLogoPath))
        {
            try
            {
                _logoImage          = Image.FromFile(s.LastLogoPath);
                txtLogoPath.Text    = s.LastLogoPath;
                pbLogoPreview.Image = _logoImage;
            }
            catch { /* ignore invalid logo file */ }
        }

        ToggleAppearanceControls();
        grpTsa.Enabled          = rbTsa.Checked || rbLtv.Checked;
        txtOutputFolder.Enabled = !chkSameFolder.Checked;
        btnBrowseOutput.Enabled = !chkSameFolder.Checked;
    }

    private void SaveSettings()
    {
        var s = new AppSettings
        {
            LastOutputFolder  = txtOutputFolder.Text,
            LastTsaUrl        = txtTsaUrl.Text,
            LastTsaUser       = txtTsaUser.Text,
            LastLogoPath      = txtLogoPath.Text,
            LastSigningLevel  = GetSelectedLevel().ToString(),
            LastReason        = txtReason.Text,
            LastLocation      = txtLocation.Text,
            AppearanceEnabled = chkShowAppearance.Checked,
            AppearancePage    = (int)nudPage.Value,
            AppearanceX       = (float)nudX.Value,
            AppearanceY       = (float)nudY.Value,
            AppearanceWidth   = (float)nudWidth.Value,
            AppearanceHeight  = (float)nudHeight.Value,
            AppearanceShowDate     = chkShowDate.Checked,
            AppearanceShowReason   = chkShowReason.Checked,
            AppearanceShowLocation = chkShowLocation.Checked,
        };
        SettingsService.Save(s);
    }
}
