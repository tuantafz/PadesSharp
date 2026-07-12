using System;
using System.IO;

namespace PadesSharpDemoApp;

/// <summary>Ghi crash log ra %APPDATA%\PadesSharpDemo\crash.log.</summary>
internal static class CrashLogger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PadesSharpDemo", "crash.log");

    public static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { /* nếu không ghi được log thì bỏ qua */ }

        MessageBox.Show(
            $"Lỗi không xác định:\n{ex.Message}\n\nChi tiết: {LogPath}",
            "PadesSharp Demo – Lỗi",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
