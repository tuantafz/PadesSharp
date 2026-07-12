// Original implementation based on public standards, no code copied from iText 5/7.

namespace ModernPdf.Tests.Unit;

/// <summary>
/// Foundation smoke tests for public package metadata.
/// </summary>
public class FoundationTests
{
    [Fact]
    public void AbstractionsAssembly_HasExpectedIdentity()
    {
        var assembly = typeof(ModernPdf.Abstractions.Signing.PdfSignRequest).Assembly;

        Assert.Equal("ModernPdf.Abstractions", assembly.GetName().Name);
    }
}
