namespace PadesSharpDemoApp.Models;

public sealed class AppearanceOptions
{
    public bool   Enabled      { get; set; } = true;
    public int    PageNumber   { get; set; } = 1;
    public float  X            { get; set; } = 36f;
    public float  Y            { get; set; } = 36f;
    public float  Width        { get; set; } = 180f;
    public float  Height       { get; set; } = 60f;
    public bool   ShowDate     { get; set; } = true;
    public bool   ShowReason   { get; set; } = true;
    public bool   ShowLocation { get; set; } = true;
    public byte[]? LogoImageBytes { get; set; }
    public string? LogoImagePath  { get; set; }
}
