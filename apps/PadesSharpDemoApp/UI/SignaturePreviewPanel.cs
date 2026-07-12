using System;
using System.Drawing;
using System.Windows.Forms;

namespace PadesSharpDemoApp.UI;

/// <summary>
/// Panel hiển thị preview chữ ký số (vẽ lại realtime khi thay đổi thông số).
/// </summary>
public sealed class SignaturePreviewPanel : Panel
{
    private string? _signerName;
    private string? _reason;
    private string? _location;
    private bool    _showDate;
    private bool    _showReason;
    private bool    _showLocation;
    private Image?  _logo;

    public SignaturePreviewPanel()
    {
        DoubleBuffered = true;
        BackColor      = Color.White;
        BorderStyle    = BorderStyle.FixedSingle;
        MinimumSize    = new Size(240, 72);
    }

    public void Update(string? signerName, string? reason, string? location,
        bool showDate, bool showReason, bool showLocation, Image? logo)
    {
        _signerName   = signerName;
        _reason       = reason;
        _location     = location;
        _showDate     = showDate;
        _showReason   = showReason;
        _showLocation = showLocation;
        _logo         = logo;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Clear(Color.White);

        // Khung ngoài
        using var borderPen = new Pen(Color.DarkSlateBlue, 1.5f);
        g.DrawRectangle(borderPen, 1, 1, Width - 3, Height - 3);

        // Nền nhạt
        using var bgBrush = new SolidBrush(Color.FromArgb(240, 245, 255));
        g.FillRectangle(bgBrush, 2, 2, Width - 4, Height - 4);

        bool hasLogo = _logo != null;
        int textX    = 8;

        // Vẽ logo bên trái
        if (hasLogo)
        {
            int logoW   = (int)(Width * 0.38f);
            var logoRect = new Rectangle(4, 4, logoW - 4, Height - 8);
            g.DrawImage(_logo!, logoRect);
            // Đường kẻ phân cách
            using var divPen = new Pen(Color.LightSteelBlue, 1);
            g.DrawLine(divPen, logoW, 4, logoW, Height - 4);
            textX = logoW + 6;
        }

        // Vẽ text
        int textAreaW = Width - textX - 4;
        int y         = 7;

        using var nameFont  = new Font("Segoe UI", 8f, FontStyle.Bold);
        using var labelFont = new Font("Segoe UI", 7f);
        using var nameBrush = new SolidBrush(Color.DarkSlateBlue);
        using var textBrush = new SolidBrush(Color.FromArgb(60, 60, 80));

        var L = AppLocale.Current;
        string signerLabel = string.IsNullOrWhiteSpace(_signerName) ? L.PreviewNoCert : _signerName;

        g.DrawString(L.PreviewSignedBy + Truncate(signerLabel, 28), nameFont, nameBrush, textX, y);
        y += 14;

        if (_showDate)
        {
            g.DrawString(L.PreviewDate + DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                labelFont, textBrush, textX, y);
            y += 11;
        }
        if (_showReason && !string.IsNullOrWhiteSpace(_reason))
        {
            g.DrawString(L.PreviewReason + Truncate(_reason, 30), labelFont, textBrush, textX, y);
            y += 11;
        }
        if (_showLocation && !string.IsNullOrWhiteSpace(_location))
        {
            g.DrawString(L.PreviewLocation + Truncate(_location, 28), labelFont, textBrush, textX, y);
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    protected override void Dispose(bool disposing)
    {
        if (disposing) _logo?.Dispose();
        base.Dispose(disposing);
    }
}
