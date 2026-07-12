using System;
using System.IO;
using System.Text.Json;
using PadesSharpDemoApp.Models;

namespace PadesSharpDemoApp.Services;

public sealed class AppSettings
{
    public string? LastOutputFolder  { get; set; }
    public string? LastTsaUrl        { get; set; }
    public string? LastTsaUser       { get; set; }
    public string? LastLogoPath      { get; set; }
    public string  LastSigningLevel  { get; set; } = "Basic";
    public string? LastReason        { get; set; }
    public string? LastLocation      { get; set; }

    // Appearance
    public bool  AppearanceEnabled  { get; set; } = true;
    public int   AppearancePage     { get; set; } = 1;
    public float AppearanceX        { get; set; } = 36f;
    public float AppearanceY        { get; set; } = 36f;
    public float AppearanceWidth    { get; set; } = 180f;
    public float AppearanceHeight   { get; set; } = 60f;
    public bool  AppearanceShowDate { get; set; } = true;
    public bool  AppearanceShowReason   { get; set; } = true;
    public bool  AppearanceShowLocation { get; set; } = true;
}

public static class SettingsService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PadesSharpDemo", "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts)
                       ?? new AppSettings();
            }
        }
        catch { /* bỏ qua lỗi đọc settings */ }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOpts));
        }
        catch { /* bỏ qua lỗi ghi settings */ }
    }
}
