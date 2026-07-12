using System.Drawing;
using System.Windows.Forms;
using PadesSharpDemoApp.UI;

namespace PadesSharpDemoApp;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null!;

    // ── Status strip ─────────────────────────────────────────────────────────
    private StatusStrip             statusStrip = null!;
    private ToolStripStatusLabel    lblStatus   = null!;
    private ToolStripDropDownButton tsddLang    = null!;

    // ── Left: Files ───────────────────────────────────────────────────────────
    private GroupBox  grpFiles       = null!;
    private ListView  lvFiles        = null!;
    private Button    btnAddFiles    = null!;
    private Button    btnRemoveFiles = null!;
    private Button    btnClearFiles  = null!;

    // ── Left: Output ──────────────────────────────────────────────────────────
    private GroupBox  grpOutput       = null!;
    private TextBox   txtOutputFolder = null!;
    private Button    btnBrowseOutput = null!;
    private CheckBox  chkSameFolder   = null!;

    // ── Right: Certificate ────────────────────────────────────────────────────
    private GroupBox grpCert        = null!;
    private Label    lblCertSubject = null!;
    private Label    lblCertIssuer  = null!;
    private Label    lblCertSerial  = null!;
    private Label    lblCertExpiry  = null!;
    private Button   btnPickCert    = null!;

    // ── Right: Signing Options ────────────────────────────────────────────────
    private GroupBox    grpSignOpts = null!;
    private RadioButton rbBasic     = null!;
    private RadioButton rbTsa       = null!;
    private RadioButton rbLtv       = null!;
    private GroupBox    grpTsa      = null!;
    private Label       lblTsaUrl   = null!;
    private TextBox     txtTsaUrl   = null!;
    private Label       lblTsaUser  = null!;
    private TextBox     txtTsaUser  = null!;
    private Label       lblTsaPass  = null!;
    private TextBox     txtTsaPass  = null!;
    private Label       lblReason   = null!;
    private TextBox     txtReason   = null!;
    private Label       lblLocation = null!;
    private TextBox     txtLocation = null!;

    // ── Appearance ────────────────────────────────────────────────────────────
    private GroupBox          grpAppearance     = null!;
    private Panel             pnlAppControls    = null!;
    private CheckBox          chkShowAppearance = null!;
    private Label             lblPage           = null!;
    private NumericUpDown     nudPage           = null!;
    private Label             lblX              = null!;
    private NumericUpDown     nudX              = null!;
    private Label             lblY              = null!;
    private NumericUpDown     nudY              = null!;
    private Label             lblWidth          = null!;
    private NumericUpDown     nudWidth          = null!;
    private Label             lblHeight         = null!;
    private NumericUpDown     nudHeight         = null!;
    private CheckBox          chkShowDate       = null!;
    private CheckBox          chkShowReason     = null!;
    private CheckBox          chkShowLocation   = null!;
    private Label             lblLogo           = null!;
    private TextBox           txtLogoPath       = null!;
    private Button            btnBrowseLogo     = null!;
    private Button            btnClearLogo      = null!;
    private PictureBox        pbLogoPreview     = null!;
    private SignaturePreviewPanel sigPreview    = null!;

    // ── Actions ───────────────────────────────────────────────────────────────
    private Button      btnSign     = null!;
    private Button      btnStopSign = null!;
    private ProgressBar progressBar = null!;
    private Button      btnClearLog = null!;

    // ── Console ───────────────────────────────────────────────────────────────
    private RichTextBox rtbConsole = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        SuspendLayout();

        // ─────────────────────────────────────────────────────────────────────
        // FORM
        // ─────────────────────────────────────────────────────────────────────
        Text          = "PadesSharp Demo — Ký số PDF";
        MinimumSize   = new Size(1000, 800);
        Size          = new Size(1120, 870);
        StartPosition = FormStartPosition.CenterScreen;
        Font          = new Font("Segoe UI", 9f);
        BackColor     = Color.FromArgb(245, 245, 248);

        // ─────────────────────────────────────────────────────────────────────
        // STATUS STRIP (Bottom)
        // ─────────────────────────────────────────────────────────────────────
        statusStrip = new StatusStrip { Dock = DockStyle.Bottom };
        lblStatus   = new ToolStripStatusLabel("Sẵn sàng")
            { Spring = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };

        tsddLang = new ToolStripDropDownButton("🇻🇳 Tiếng Việt")
            { DisplayStyle = ToolStripItemDisplayStyle.Text };
        var mnuVi = new ToolStripMenuItem("🇻🇳 Tiếng Việt") { Tag = AppLanguage.Vietnamese };
        var mnuEn = new ToolStripMenuItem("🇺🇸 English")    { Tag = AppLanguage.English };
        tsddLang.DropDownItems.AddRange(new ToolStripItem[] { mnuVi, mnuEn });

        statusStrip.Items.AddRange(new ToolStripItem[] { lblStatus, tsddLang });

        // ─────────────────────────────────────────────────────────────────────
        // ROOT TableLayoutPanel (4 rows, fills form)
        // ─────────────────────────────────────────────────────────────────────
        var tblRoot = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            RowCount    = 4,
            ColumnCount = 1,
            Padding     = new Padding(6, 6, 6, 4),
            GrowStyle   = TableLayoutPanelGrowStyle.FixedSize,
        };
        tblRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tblRoot.RowStyles.Add(new RowStyle(SizeType.Absolute,  46)); // Row 0: Logo header
        tblRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // Row 1: SplitContainer (Fill)
        tblRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 185)); // Row 2: Appearance
        tblRoot.RowStyles.Add(new RowStyle(SizeType.Absolute,  54)); // Row 3: Actions bar
        tblRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 170)); // Row 4: Console
        tblRoot.RowCount = 5;

        // ─────────────────────────────────────────────────────────────────────
        // ROW 0: Logo header bar
        // ─────────────────────────────────────────────────────────────────────
        var pnlLogoHeader = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = Color.FromArgb(0x0A, 0x23, 0x42),
            Margin    = new Padding(0),
        };
        var pbLogo = new PictureBox
        {
            Dock      = DockStyle.Fill,
            SizeMode  = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Padding   = new Padding(6, 4, 6, 4),
        };
        pbLogo.Image = LogoRenderer.CreateLogoBitmap(320, 38);
        pnlLogoHeader.Controls.Add(pbLogo);
        tblRoot.Controls.Add(pnlLogoHeader, 0, 0);

        // ─────────────────────────────────────────────────────────────────────
        // ROW 1: SplitContainer (Left: Files | Right: Cert + Options)
        // ─────────────────────────────────────────────────────────────────────
        var splitTop = new SplitContainer
        {
            Dock             = DockStyle.Fill,
            Orientation      = Orientation.Vertical,
            SplitterDistance = 480,
            FixedPanel       = FixedPanel.None,
            BackColor        = Color.Transparent,
        };

        // ── Panel1 (Left): Files + Output ─────────────────────────────────────
        var tblLeft = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            RowCount    = 2,
            ColumnCount = 1,
        };
        tblLeft.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tblLeft.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // Files (Fill)
        tblLeft.RowStyles.Add(new RowStyle(SizeType.Absolute, 88)); // Output (fixed)

        // grpFiles
        grpFiles = new GroupBox
        {
            Text    = "① Files cần ký",
            Dock    = DockStyle.Fill,
            Padding = new Padding(6, 4, 6, 4),
            Font    = new Font("Segoe UI", 9f, FontStyle.Bold),
            Margin  = new Padding(0, 0, 0, 3),
        };

        lvFiles = new ListView
        {
            View               = View.Details,
            FullRowSelect      = true,
            MultiSelect        = true,
            AllowColumnReorder = false,
            GridLines          = true,
            Dock               = DockStyle.Fill,
            Font               = new Font("Segoe UI", 8.5f),
            AllowDrop          = true,
            BackColor          = Color.White,
        };
        lvFiles.Columns.Add("Tên file",   220);
        lvFiles.Columns.Add("Kích thước",  78);
        lvFiles.Columns.Add("Thư mục",    145);
        lvFiles.Columns.Add("Trạng thái",  95);

        btnAddFiles    = new Button { Text = "＋ Thêm file", Width = 112, Height = 26 };
        btnRemoveFiles = new Button { Text = "✕ Xóa",        Width = 80,  Height = 26 };
        btnClearFiles  = new Button { Text = "Xóa tất cả",   Width = 92,  Height = 26 };

        var flowFileBtns = new FlowLayoutPanel
        {
            Dock          = DockStyle.Bottom,
            Height        = 32,
            FlowDirection = FlowDirection.LeftToRight,
            Padding       = new Padding(0, 3, 0, 0),
            BackColor     = Color.Transparent,
        };
        flowFileBtns.Controls.AddRange(new Control[] { btnAddFiles, btnRemoveFiles, btnClearFiles });

        var pnlFilesInner = new Panel { Dock = DockStyle.Fill };
        pnlFilesInner.Controls.Add(lvFiles);
        pnlFilesInner.Controls.Add(flowFileBtns);
        grpFiles.Controls.Add(pnlFilesInner);

        // grpOutput
        grpOutput = new GroupBox
        {
            Text    = "② Thư mục đầu ra",
            Dock    = DockStyle.Fill,
            Padding = new Padding(6, 4, 6, 4),
            Font    = new Font("Segoe UI", 9f, FontStyle.Bold),
            Margin  = new Padding(0, 3, 0, 0),
        };

        txtOutputFolder = new TextBox
        {
            ReadOnly  = true,
            BackColor = Color.White,
            Dock      = DockStyle.Fill,
            Font      = new Font("Segoe UI", 8.5f),
        };
        btnBrowseOutput = new Button { Text = "📁 Chọn thư mục", Width = 120, Height = 22, Dock = DockStyle.Right };
        chkSameFolder   = new CheckBox { Text = "Cùng thư mục nguồn (suffix _signed)", Dock = DockStyle.Bottom, Height = 22, Checked = true };

        var pnlOutputRow = new Panel { Dock = DockStyle.Fill };
        pnlOutputRow.Controls.Add(txtOutputFolder);
        pnlOutputRow.Controls.Add(btnBrowseOutput);

        var pnlOutputInner = new Panel { Dock = DockStyle.Fill };
        pnlOutputInner.Controls.Add(pnlOutputRow);
        pnlOutputInner.Controls.Add(chkSameFolder);
        grpOutput.Controls.Add(pnlOutputInner);

        tblLeft.Controls.Add(grpFiles,  0, 0);
        tblLeft.Controls.Add(grpOutput, 0, 1);
        splitTop.Panel1.Controls.Add(tblLeft);

        // ── Panel2 (Right): scrollable container → grpCert on top, grpSignOpts below ──
        // Strategy: the OUTER panel scrolls vertically when the window is short.
        // Both groups have Dock=Top with fixed heights so they always show fully.
        var pnlRight = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        // grpCert — fixed height 138px
        grpCert = new GroupBox
        {
            Text    = "③ Chứng thư số",
            Dock    = DockStyle.Top,
            Height  = 138,
            Padding = new Padding(6, 4, 6, 4),
            Font    = new Font("Segoe UI", 9f, FontStyle.Bold),
            Margin  = new Padding(0, 0, 0, 3),
        };

        lblCertSubject = new Label { Text = "Subject: —", Dock = DockStyle.Top, Height = 20, Font = new Font("Segoe UI", 8.5f), AutoEllipsis = true };
        lblCertIssuer  = new Label { Text = "Issuer:  —", Dock = DockStyle.Top, Height = 20, Font = new Font("Segoe UI", 8.5f), AutoEllipsis = true };
        lblCertSerial  = new Label { Text = "Serial:  —", Dock = DockStyle.Top, Height = 20, Font = new Font("Segoe UI", 8.5f), AutoEllipsis = true };
        lblCertExpiry  = new Label { Text = "Hết hạn: —", Dock = DockStyle.Top, Height = 20, Font = new Font("Segoe UI", 8.5f) };
        btnPickCert    = new Button { Text = "🔑 Chọn từ Windows Store", Dock = DockStyle.Bottom, Height = 28 };

        grpCert.Controls.Add(btnPickCert);
        grpCert.Controls.Add(lblCertExpiry);
        grpCert.Controls.Add(lblCertSerial);
        grpCert.Controls.Add(lblCertIssuer);
        grpCert.Controls.Add(lblCertSubject);

        // grpSignOpts — all children use Dock=Top so they auto-resize with the panel width
        grpSignOpts = new GroupBox
        {
            Text    = "④ Tùy chọn ký",
            Dock    = DockStyle.Top,
            Height  = 300,
            Padding = new Padding(4, 2, 4, 2),
            Font    = new Font("Segoe UI", 9f, FontStyle.Bold),
            Margin  = new Padding(0),
        };

        // Wrap all signing-option content in a fill panel so Dock=Top children stack correctly
        var pnlSignOptsInner = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };

        rbBasic = new RadioButton { Text = "Basic (adbe.pkcs7.detached)", Checked = true, Dock = DockStyle.Top, Height = 22 };
        rbTsa   = new RadioButton { Text = "TSA – PAdES-T",               Dock = DockStyle.Top, Height = 22 };
        rbLtv   = new RadioButton { Text = "LTV/DSS – PAdES-LTA",         Dock = DockStyle.Top, Height = 22 };

        grpTsa = new GroupBox
        {
            Text    = "TSA Configuration",
            Dock    = DockStyle.Top,
            Height  = 118,
            Enabled = false,
            Padding = new Padding(4, 2, 4, 2),
            Font    = new Font("Segoe UI", 8.5f),
        };

        // TSA URL row: label (fixed) + textbox (fills remaining width)
        var pnlTsaUrl = new Panel { Dock = DockStyle.Top, Height = 26 };
        lblTsaUrl = new Label  { Text = "URL:", Dock = DockStyle.Left, Width = 36, TextAlign = ContentAlignment.MiddleLeft };
        txtTsaUrl = new TextBox { Dock = DockStyle.Fill, Text = "http://timestamp.digicert.com" };
        pnlTsaUrl.Controls.Add(txtTsaUrl);
        pnlTsaUrl.Controls.Add(lblTsaUrl);

        // TSA User row
        var pnlTsaUser = new Panel { Dock = DockStyle.Top, Height = 26 };
        lblTsaUser = new Label  { Text = "User:", Dock = DockStyle.Left, Width = 36, TextAlign = ContentAlignment.MiddleLeft };
        txtTsaUser = new TextBox { Dock = DockStyle.Fill };
        pnlTsaUser.Controls.Add(txtTsaUser);
        pnlTsaUser.Controls.Add(lblTsaUser);

        // TSA Pass row
        var pnlTsaPass = new Panel { Dock = DockStyle.Top, Height = 26 };
        lblTsaPass = new Label  { Text = "Pass:", Dock = DockStyle.Left, Width = 36, TextAlign = ContentAlignment.MiddleLeft };
        txtTsaPass = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
        pnlTsaPass.Controls.Add(txtTsaPass);
        pnlTsaPass.Controls.Add(lblTsaPass);

        grpTsa.Controls.Add(pnlTsaPass);
        grpTsa.Controls.Add(pnlTsaUser);
        grpTsa.Controls.Add(pnlTsaUrl);

        lblReason   = new Label  { Text = "Lý do:",    Dock = DockStyle.Top, Height = 18, Font = new Font("Segoe UI", 8.5f) };
        txtReason   = new TextBox { Dock = DockStyle.Top, Font = new Font("Segoe UI", 8.5f) };
        lblLocation = new Label  { Text = "Địa điểm:", Dock = DockStyle.Top, Height = 18, Font = new Font("Segoe UI", 8.5f) };
        txtLocation = new TextBox { Dock = DockStyle.Top, Font = new Font("Segoe UI", 8.5f) };

        // Add in reverse order (Dock=Top stacks: last-added appears topmost visually)
        pnlSignOptsInner.Controls.Add(txtLocation);
        pnlSignOptsInner.Controls.Add(lblLocation);
        pnlSignOptsInner.Controls.Add(txtReason);
        pnlSignOptsInner.Controls.Add(lblReason);
        pnlSignOptsInner.Controls.Add(grpTsa);
        pnlSignOptsInner.Controls.Add(rbLtv);
        pnlSignOptsInner.Controls.Add(rbTsa);
        pnlSignOptsInner.Controls.Add(rbBasic);
        grpSignOpts.Controls.Add(pnlSignOptsInner);

        // Add cert first (DockStyle.Top stacks top-to-bottom, first added = topmost)
        pnlRight.Controls.Add(grpSignOpts); // added first → processed last → below cert
        pnlRight.Controls.Add(grpCert);     // added second → processed first → topmost
        splitTop.Panel2.Controls.Add(pnlRight);

        tblRoot.Controls.Add(splitTop, 0, 1);

        // ─────────────────────────────────────────────────────────────────────
        // ROW 1: Appearance GroupBox (185px)
        // ─────────────────────────────────────────────────────────────────────
        grpAppearance = new GroupBox
        {
            Text    = "⑤ Chữ ký hiển thị (Visible Signature)",
            Dock    = DockStyle.Fill,
            Padding = new Padding(6, 4, 6, 4),
            Font    = new Font("Segoe UI", 9f, FontStyle.Bold),
            Margin  = new Padding(0, 4, 0, 0),
        };

        // Two-column layout: settings (Fill) | preview (290px fixed)
        var tblApp = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            RowCount    = 1,
            Padding     = new Padding(0),
        };
        tblApp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // settings
        tblApp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 292)); // preview
        tblApp.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Left: appearance controls
        var pnlAppLeft = new Panel { Dock = DockStyle.Fill };

        chkShowAppearance = new CheckBox
        {
            Text    = "Bật chữ ký trực quan",
            Checked = true,
            Dock    = DockStyle.Top,
            Height  = 24,
            Font    = new Font("Segoe UI", 9f),
        };

        pnlAppControls = new Panel { Dock = DockStyle.Fill };

        // Placement row: Page / X / Y / Width / Height
        lblPage   = new Label         { Text = "Trang:",  Width = 46, TextAlign = ContentAlignment.MiddleLeft };
        nudPage   = new NumericUpDown { Width = 56, Minimum = 1, Maximum = 9999, Value = 1 };
        lblX      = new Label         { Text = "X (pt):", Width = 48, TextAlign = ContentAlignment.MiddleLeft };
        nudX      = new NumericUpDown { Width = 68, Minimum = 0, Maximum = 9999, Value = 36, DecimalPlaces = 1 };
        lblY      = new Label         { Text = "Y (pt):", Width = 48, TextAlign = ContentAlignment.MiddleLeft };
        nudY      = new NumericUpDown { Width = 68, Minimum = 0, Maximum = 9999, Value = 36, DecimalPlaces = 1 };
        lblWidth  = new Label         { Text = "Rộng:",   Width = 46, TextAlign = ContentAlignment.MiddleLeft };
        nudWidth  = new NumericUpDown { Width = 68, Minimum = 30, Maximum = 9999, Value = 180, DecimalPlaces = 1 };
        lblHeight = new Label         { Text = "Cao:",    Width = 38, TextAlign = ContentAlignment.MiddleLeft };
        nudHeight = new NumericUpDown { Width = 62, Minimum = 20, Maximum = 9999, Value = 60,  DecimalPlaces = 1 };

        var flowPlacement = new FlowLayoutPanel
        {
            Dock          = DockStyle.Top,
            Height        = 32,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
            BackColor     = Color.Transparent,
            Padding       = new Padding(0, 2, 0, 0),
        };
        flowPlacement.Controls.AddRange(new Control[]
            { lblPage, nudPage, lblX, nudX, lblY, nudY, lblWidth, nudWidth, lblHeight, nudHeight });

        // Checks row
        chkShowDate     = new CheckBox { Text = "Ngày ký",   Checked = true, Width = 98,  AutoSize = false };
        chkShowReason   = new CheckBox { Text = "Lý do",     Checked = true, Width = 75,  AutoSize = false };
        chkShowLocation = new CheckBox { Text = "Địa điểm",  Checked = true, Width = 92,  AutoSize = false };

        var flowChecks = new FlowLayoutPanel
        {
            Dock          = DockStyle.Top,
            Height        = 26,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor     = Color.Transparent,
        };
        flowChecks.Controls.AddRange(new Control[] { chkShowDate, chkShowReason, chkShowLocation });

        // Logo row
        lblLogo       = new Label  { Text = "Ảnh:",  Width = 38, TextAlign = ContentAlignment.MiddleLeft };
        txtLogoPath   = new TextBox { Width = 290, ReadOnly = true };
        btnBrowseLogo = new Button  { Text = "📂", Width = 32, Height = 24 };
        btnClearLogo  = new Button  { Text = "✕",  Width = 28, Height = 24 };
        pbLogoPreview = new PictureBox { Width = 52, Height = 28, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };

        var flowLogo = new FlowLayoutPanel
        {
            Dock          = DockStyle.Top,
            Height        = 32,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor     = Color.Transparent,
            Padding       = new Padding(0, 2, 0, 0),
        };
        flowLogo.Controls.AddRange(new Control[] { lblLogo, txtLogoPath, btnBrowseLogo, btnClearLogo, pbLogoPreview });

        // Add sub-rows in reverse (DockStyle.Top stacking)
        pnlAppControls.Controls.Add(flowLogo);
        pnlAppControls.Controls.Add(flowChecks);
        pnlAppControls.Controls.Add(flowPlacement);

        pnlAppLeft.Controls.Add(pnlAppControls);
        pnlAppLeft.Controls.Add(chkShowAppearance);

        // Right: signature preview
        sigPreview = new SignaturePreviewPanel
        {
            Dock   = DockStyle.Fill,
            Margin = new Padding(6, 0, 0, 0),
        };

        tblApp.Controls.Add(pnlAppLeft, 0, 0);
        tblApp.Controls.Add(sigPreview,  1, 0);
        grpAppearance.Controls.Add(tblApp);

        tblRoot.Controls.Add(grpAppearance, 0, 2);

        // ─────────────────────────────────────────────────────────────────────
        // ROW 2: Actions bar — [Clear Log] [====Progress====] [Stop] [▶ SIGN]
        // ─────────────────────────────────────────────────────────────────────
        var tblActions = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            RowCount    = 1,
            ColumnCount = 3,
            Margin      = new Padding(0, 4, 0, 0),
            BackColor   = Color.Transparent,
        };
        tblActions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // Clear Log
        tblActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // Progress (Fill)
        tblActions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // Stop + Sign

        tblActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Col 0: Clear log button
        btnClearLog = new Button
        {
            Text   = "🗑 Xóa log",
            Size   = new Size(92, 32),
            Margin = new Padding(0, 10, 8, 0),
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
        };
        tblActions.Controls.Add(btnClearLog, 0, 0);

        // Col 1: Progress bar inside a padded panel
        var pnlProgress = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 15, 8, 7) };
        progressBar = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Continuous };
        pnlProgress.Controls.Add(progressBar);
        tblActions.Controls.Add(pnlProgress, 1, 0);

        // Col 2: Stop + Sign (prominent, bottom-right)
        btnStopSign = new Button
        {
            Text      = "⊗  Dừng",
            Size      = new Size(92, 44),
            Enabled   = false,
            BackColor = Color.FromArgb(200, 70, 70),
            ForeColor = Color.White,
            Font      = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Margin    = new Padding(0, 4, 6, 4),
        };
        btnStopSign.FlatAppearance.BorderSize = 0;

        btnSign = new Button
        {
            Text      = "▶  KÝ SỐ  (F5)",
            Size      = new Size(165, 44),
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            Font      = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            Margin    = new Padding(0, 4, 0, 4),
        };
        btnSign.FlatAppearance.BorderSize = 0;

        var flowSignBtns = new FlowLayoutPanel
        {
            AutoSize      = true,
            AutoSizeMode  = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
            BackColor     = Color.Transparent,
            Margin        = new Padding(0),
        };
        flowSignBtns.Controls.AddRange(new Control[] { btnStopSign, btnSign });
        tblActions.Controls.Add(flowSignBtns, 2, 0);

        tblRoot.Controls.Add(tblActions, 0, 3);

        // ─────────────────────────────────────────────────────────────────────
        // ROW 3: Console (170px)
        // ─────────────────────────────────────────────────────────────────────
        rtbConsole = new RichTextBox
        {
            Dock       = DockStyle.Fill,
            ReadOnly   = true,
            BackColor  = Color.FromArgb(20, 20, 30),
            ForeColor  = Color.WhiteSmoke,
            Font       = new Font("Consolas", 8.5f),
            ScrollBars = RichTextBoxScrollBars.Vertical,
            WordWrap   = false,
            Margin     = new Padding(0, 4, 0, 0),
        };
        tblRoot.Controls.Add(rtbConsole, 0, 4);

        // ─────────────────────────────────────────────────────────────────────
        // Add to form
        // ─────────────────────────────────────────────────────────────────────
        Controls.Add(tblRoot);
        Controls.Add(statusStrip);

        ResumeLayout(false);
        PerformLayout();
    }
}
