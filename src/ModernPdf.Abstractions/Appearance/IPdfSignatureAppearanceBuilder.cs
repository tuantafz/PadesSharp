// Original implementation based on public standards, no code copied from iText 5/7.

namespace ModernPdf.Abstractions.Appearance;

/// <summary>
/// Builds the visible appearance (Form XObject content stream) for a
/// PDF signature annotation.
/// </summary>
public interface IPdfSignatureAppearanceBuilder
{
    /// <summary>
    /// Produces a <see cref="PdfSignatureAppearanceResult"/> containing the
    /// Form XObject content stream and optional embedded image data.
    /// </summary>
    PdfSignatureAppearanceResult Build(PdfSignatureAppearanceRequest request);
}
