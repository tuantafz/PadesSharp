using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace PadesSharpDemoApp.UI;

/// <summary>
/// Draws the PadesSharp logo programmatically via GDI+ so no external image files are needed.
/// </summary>
internal static class LogoRenderer
{
    private static readonly Color Navy   = Color.FromArgb(0x0A, 0x23, 0x42);
    private static readonly Color Blue   = Color.FromArgb(0x00, 0x78, 0xD4);
    private static readonly Color White  = Color.White;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Creates the compact 32×32 icon bitmap (shield + check).</summary>
    public static Bitmap CreateIconBitmap(int size = 32)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode   = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        float s = size / 32f;
        DrawShield(g, s, 1 * s, 1 * s, 30 * s, 30 * s);
        return bmp;
    }

    /// <summary>Creates the full horizontal logo bitmap (shield + wordmark).</summary>
    public static Bitmap CreateLogoBitmap(int width = 320, int height = 52)
    {
        var bmp = new Bitmap(width, height);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // Shield at 48px tall
        float scale = height / 32f;
        float shieldW = 22 * scale;
        DrawShield(g, scale, 0, 0, shieldW, height);

        // Wordmark
        float textX = shieldW + 8;
        float textY = height * 0.18f;
        float fontSize = height * 0.52f;

        using var boldFont = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        // White text on dark header background
        using var nameBrush = new SolidBrush(Color.White);
        using var sharpBrush = new SolidBrush(Color.FromArgb(0x64, 0xB8, 0xFF)); // light accent blue

        g.DrawString("Pades", boldFont, nameBrush, textX, textY);
        float padesW = g.MeasureString("Pades", boldFont).Width - 4;
        g.DrawString("Sharp", boldFont, sharpBrush, textX + padesW, textY);

        // Tagline
        float tagSize = height * 0.26f;
        using var tagFont  = new Font("Segoe UI", tagSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var tagBrush = new SolidBrush(Color.FromArgb(0xCC, 0xE0, 0xFF));
        g.DrawString("PDF DIGITAL SIGNING · .NET", tagFont, tagBrush, textX, textY + fontSize + 2);

        return bmp;
    }

    /// <summary>Converts a 32×32 Bitmap to a Windows Icon.</summary>
    public static Icon BitmapToIcon(Bitmap bmp)
    {
        IntPtr hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        NativeMethods.DestroyIcon(hIcon);
        return icon;
    }

    // ── Drawing helpers ───────────────────────────────────────────────────────

    private static void DrawShield(Graphics g, float s,
        float x, float y, float w, float h)
    {
        // Shield outline path
        using var path = new GraphicsPath();
        float cx = x + w / 2f;
        float bottom = y + h;
        path.AddLines(new PointF[]
        {
            new(x,      y),
            new(x + w,  y),
            new(x + w,  y + h * 0.66f),
            new(cx,     bottom),
            new(x,      y + h * 0.66f),
        });
        path.CloseFigure();

        using var navyBrush = new SolidBrush(Navy);
        g.FillPath(navyBrush, path);

        // Inner document (white rect)
        float dX = x + w * 0.22f;
        float dY = y + h * 0.18f;
        float dW = w * 0.56f;
        float dH = h * 0.44f;
        g.FillRectangle(Brushes.White, dX, dY, dW, dH);

        // Doc lines
        using var linePen = new Pen(Color.FromArgb(180, Navy), Math.Max(1f, s * 0.8f));
        float lx1 = dX + dW * 0.15f, lx2 = dX + dW * 0.85f;
        g.DrawLine(linePen, lx1, dY + dH * 0.25f, lx2, dY + dH * 0.25f);
        g.DrawLine(linePen, lx1, dY + dH * 0.50f, lx2, dY + dH * 0.50f);
        g.DrawLine(linePen, lx1, dY + dH * 0.75f, lx2 * 0.7f + lx1 * 0.3f, dY + dH * 0.75f);

        // Verified badge (blue circle + checkmark)
        float bR = w * 0.22f;
        float bX = x + w * 0.72f - bR;
        float bY = y + h * 0.60f - bR;
        using var blueBrush = new SolidBrush(Blue);
        g.FillEllipse(blueBrush, bX, bY, bR * 2, bR * 2);

        using var checkPen = new Pen(White, Math.Max(1.2f, s * 0.9f))
            { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float bCx = bX + bR, bCy = bY + bR;
        g.DrawLines(checkPen, new PointF[]
        {
            new(bCx - bR * 0.5f, bCy),
            new(bCx - bR * 0.1f, bCy + bR * 0.45f),
            new(bCx + bR * 0.55f, bCy - bR * 0.45f),
        });
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern bool DestroyIcon(IntPtr handle);
    }
}
