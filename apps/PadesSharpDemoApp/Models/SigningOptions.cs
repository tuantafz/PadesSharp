namespace PadesSharpDemoApp.Models;

public sealed class SigningOptions
{
    public SigningLevel Level    { get; set; } = SigningLevel.Basic;
    public string?      TsaUrl  { get; set; }
    public string?      TsaUser { get; set; }
    public string?      TsaPass { get; set; }
    public string?      Reason   { get; set; }
    public string?      Location { get; set; }
    public string       SubFilter { get; set; } = "adbe.pkcs7.detached";
    public int SignatureContentSize { get; set; } = 24576;
}
