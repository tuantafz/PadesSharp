// Original implementation based on public standards, no code copied from iText 5/7.

using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using ModernPdf.Abstractions.Appearance;
using ModernPdf.Abstractions.Crypto;
using ModernPdf.Abstractions.Tsa;

namespace ModernPdf.Abstractions.Signing;

/// <summary>
/// Parameters for a PDF signing operation.
/// </summary>
public sealed class PdfSignRequest
{
    /// <summary>
    /// Optional existing PDF to sign via incremental update.
    /// If <c>null</c> or empty, a minimal 1-page blank PDF is created.
    /// </summary>
    public Stream? InputPdf { get; set; }

    /// <summary>
    /// Stream where the signed PDF will be written. Must be seekable.
    /// </summary>
    public Stream OutputPdf { get; set; } = null!;

    /// <summary>Digest algorithm for hashing the PDF byte range.</summary>
    public PdfDigestAlgorithm DigestAlgorithm { get; set; } = PdfDigestAlgorithm.Sha256;

    /// <summary>Signing key provider (software RSA, HSM, PKCS#11, etc.).</summary>
    public ISignatureProvider SignatureProvider { get; set; } = null!;

    /// <summary>End-entity (signer) certificate.</summary>
    public X509Certificate2 Certificate { get; set; } = null!;

    /// <summary>Full certificate chain, including the signer certificate.</summary>
    public IReadOnlyList<X509Certificate2>? CertificateChain { get; set; }

    /// <summary>Optional signing reason text embedded in the PDF signature dictionary.</summary>
    public string? Reason { get; set; }

    /// <summary>Optional signing location text.</summary>
    public string? Location { get; set; }

    /// <summary>
    /// Name of the AcroForm signature field.  Default: <c>"Signature1"</c>.
    /// </summary>
    public string SignatureName { get; set; } = "Signature1";

    /// <summary>
    /// Reserved size in <em>bytes</em> for the /Contents hex-encoded CMS blob.
    /// Must be large enough for the actual CMS output.  Default: 8192 bytes.
    /// </summary>
    public int SignatureContentSize { get; set; } = 8192;

    /// <summary>
    /// PDF SubFilter value.  Use <c>adbe.pkcs7.detached</c> (default, Adobe)
    /// or <c>ETSI.CAdES.detached</c> (PAdES-BES).
    /// </summary>
    public string SubFilter { get; set; } = "adbe.pkcs7.detached";

    /// <summary>
    /// Optional visible signature appearance.
    /// When set, a Form XObject with text (and optional logo image) is embedded
    /// and the signature widget annotation becomes visible on the page.
    /// Requires <see cref="IPdfSignatureAppearanceBuilder"/> to be injected into
    /// the signing engine.
    /// </summary>
    public PdfSignatureAppearanceRequest? Appearance { get; set; }

    /// <summary>
    /// Optional RFC 3161 TSA client.
    /// When set, a <c>signature-time-stamp</c> (id-aa-signatureTimeStampToken)
    /// unsigned attribute is added to the CMS SignerInfo after signing,
    /// producing a PAdES-T level signature.
    /// </summary>
    public ITsaClient? TsaClient { get; set; }
}
