namespace PadesSharpDemoApp.Models;

public enum SigningLevel
{
    Basic,   // adbe.pkcs7.detached
    Tsa,     // PAdES-T: Basic + RFC 3161 timestamp
    LtvDss   // PAdES-LTA: TSA + DSS/VRI embedded
}
