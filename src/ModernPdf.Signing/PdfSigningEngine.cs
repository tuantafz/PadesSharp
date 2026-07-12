// Original implementation based on public standards, no code copied from iText 5/7.
// Reference: ISO 32000-1 §12.8 (Digital Signatures), RFC 5652 (CMS)

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModernPdf.Abstractions.Appearance;
using ModernPdf.Abstractions.Crypto;
using ModernPdf.Abstractions.Signing;
using ModernPdf.Abstractions.Tsa;
using ModernPdf.Crypto.Tsa;
using ModernPdf.Signing.Internal;

namespace ModernPdf.Signing;

/// <summary>
/// Orchestrates the complete PDF signing flow per ISO 32000-1 §12.8:
/// <list type="number">
///   <item>Write a minimal PDF with a signature field and fixed-width placeholders.</item>
///   <item>Calculate the /ByteRange values from exact byte positions.</item>
///   <item>Patch the /ByteRange placeholder.</item>
///   <item>Digest the byte ranges.</item>
///   <item>Build a detached CMS via <see cref="ICmsSigner"/>.</item>
///   <item>Inject the CMS hex string into the /Contents placeholder.</item>
/// </list>
/// </summary>
public sealed class PdfSigningEngine : IPdfSigner
{
    private readonly ICmsSigner _cmsSigner;
    private readonly IDigestService _digestService;
    private readonly IPdfSignatureAppearanceBuilder? _appearanceBuilder;
    private readonly ILogger<PdfSigningEngine> _logger;

    /// <summary>
    /// Initialises the signing engine.
    /// </summary>
    public PdfSigningEngine(
        ICmsSigner cmsSigner,
        IDigestService digestService,
        IPdfSignatureAppearanceBuilder? appearanceBuilder = null,
        ILogger<PdfSigningEngine>? logger = null)
    {
        _cmsSigner = cmsSigner ?? throw new ArgumentNullException(nameof(cmsSigner));
        _digestService = digestService ?? throw new ArgumentNullException(nameof(digestService));
        _appearanceBuilder = appearanceBuilder;
        _logger = logger ?? NullLogger<PdfSigningEngine>.Instance;
    }

    /// <inheritdoc/>
    public Task<PdfSignResult> SignAsync(
        PdfSignRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (request.OutputPdf is null) throw new ArgumentException("OutputPdf must be set.", nameof(request));
        if (request.Certificate is null) throw new ArgumentException("Certificate must be set.", nameof(request));
        if (request.SignatureProvider is null) throw new ArgumentException("SignatureProvider must be set.", nameof(request));
        if (!request.OutputPdf.CanSeek) throw new ArgumentException("OutputPdf must be a seekable stream.", nameof(request));
        if (request.SignatureContentSize < 512 || request.SignatureContentSize % 2 != 0)
            throw new ArgumentException("SignatureContentSize must be a positive even number ≥ 512.", nameof(request));

        return SignCoreAsync(request, cancellationToken);
    }

    // -----------------------------------------------------------------------

    private async Task<PdfSignResult> SignCoreAsync(PdfSignRequest request, CancellationToken cancellationToken)
    {
        var signingTime = DateTimeOffset.UtcNow;

        // Auto-calculate SignatureContentSize based on certificate chain + TSA.
        // We use a local variable — the caller's request object must not be mutated.
        var chain = request.CertificateChain
            ?? (IReadOnlyList<System.Security.Cryptography.X509Certificates.X509Certificate2>)
               new[] { request.Certificate };
        int contentSize = Math.Max(request.SignatureContentSize,
            EstimateSignatureContentSize(chain, request.TsaClient));

        // Build appearance if requested
        PdfSignatureAppearanceResult? appearanceResult = null;
        if (request.Appearance != null)
        {
            if (_appearanceBuilder == null)
                throw new InvalidOperationException(
                    "PdfSignRequest.Appearance is set but no IPdfSignatureAppearanceBuilder was provided.");
            appearanceResult = _appearanceBuilder.Build(request.Appearance);
        }

        // Phase 1 — Produce PDF bytes with signature placeholders.
        byte[] pdfBytes;
        SignaturePlaceholderState placeholderState;

        if (request.InputPdf != null)
        {
            // Sign an existing PDF by appending an incremental update (ISO 32000-1 §12.8.3).
            byte[] inputBytes;
            using (var inputCopy = new MemoryStream())
            {
                request.InputPdf.CopyTo(inputCopy);
                inputBytes = inputCopy.ToArray();
            }
            _logger.LogDebug("Signing existing PDF ({Bytes} bytes) via incremental update.", inputBytes.Length);
            (pdfBytes, placeholderState) = PdfIncrementalSigningCore.AppendSignature(
                inputBytes, request, signingTime, appearanceResult, contentSize);
        }
        else
        {
            // Create a minimal 1-page blank PDF from scratch.
            using var buffer = new MemoryStream();
            var writer = new PdfLiteWriterContext(buffer);
            placeholderState = writer.WriteMinimalSignedPdf(request, signingTime, appearanceResult, contentSize);
            pdfBytes = buffer.ToArray();
        }

        // Phase 2 — Calculate the four ByteRange values.
        //   contentsHexStart   = position of '<' in "/Contents <hex>"
        //   contentsHexEnd     = position of '>' (exclusive: char after last hex digit)
        long contentsHexStart = placeholderState.ContentsHexStart;
        long contentsHexEnd   = contentsHexStart + 1 + placeholderState.ContentsHexLength + 1;
        //  '<'                                           hex chars                         '>'

        long b0 = 0;
        long b1 = contentsHexStart;             // bytes 0 .. (contentsHexStart - 1)
        long b2 = contentsHexEnd;               // bytes after '>'
        long b3 = pdfBytes.Length - contentsHexEnd; // remaining bytes

        long[] byteRange = new[] { b0, b1, b2, b3 };

        _logger.LogDebug("ByteRange = [{B0} {B1} {B2} {B3}]", b0, b1, b2, b3);

        // Phase 3 — Patch /ByteRange with real values.
        using var patchable = new MemoryStream(pdfBytes, writable: true);
        var patcher = new PdfLiteWriter(patchable);
        patcher.PatchByteRange(placeholderState, byteRange);

        // Phase 4 — Digest the signed bytes (everything outside /Contents).
        byte[] digest = DigestByteRange(pdfBytes, byteRange, request.DigestAlgorithm);

        _logger.LogDebug("ByteRange digest ({Alg}): {Hex}",
            request.DigestAlgorithm,
            BitConverter.ToString(digest).Replace("-", string.Empty).ToLowerInvariant());

        // Phase 5 — Build detached CMS.
        var cmsRequest = new CmsSigningRequest
        {
            ContentDigest             = digest,
            SigningCertificate        = request.Certificate,
            CertificateChain          = chain,
            DigestAlgorithm           = request.DigestAlgorithm,
            SignatureProvider         = request.SignatureProvider,
            SigningTime               = signingTime,
            IncludeSigningCertificateV2 = true,
        };

        byte[] cmsBytes = _cmsSigner.CreateDetachedSignature(cmsRequest);

        // Phase 5b — Optional RFC 3161 signature-time-stamp (PAdES-T).
        if (request.TsaClient != null)
        {
            _logger.LogDebug("Adding RFC 3161 signature-time-stamp via TSA.");
            cmsBytes = await TsaAttributeStamper
                .AddSignatureTimestampAsync(cmsBytes, request.TsaClient, _logger, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogDebug("CMS size after TSA timestamp: {Bytes} bytes.", cmsBytes.Length);
        }

        _logger.LogDebug("CMS size: {Bytes} bytes (reserved: {Reserved})",
            cmsBytes.Length, contentSize);

        if (cmsBytes.Length > contentSize)
            throw new InvalidOperationException(
                $"CMS output ({cmsBytes.Length} bytes) exceeds reserved SignatureContentSize " +
                $"({contentSize} bytes). Increase SignatureContentSize.");

        // Phase 6 — Patch /Contents with hex-encoded CMS.
        patcher.PatchContents(placeholderState, cmsBytes);

        // Phase 7 — Write final bytes to the output stream.
        request.OutputPdf.Write(pdfBytes, 0, pdfBytes.Length);

        // Compute VRI key (SHA-256 of the raw CMS bytes).
        byte[] cmsHash;
        using (var sha = SHA256.Create())
            cmsHash = sha.ComputeHash(cmsBytes);

        return new PdfSignResult
        {
            Success            = true,
            SignatureName      = request.SignatureName,
            ByteRange          = byteRange,
            SignatureValueHash = cmsHash,
            SignatureCmsBytes  = cmsBytes,
        };
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// Hashes the two ByteRange segments using the specified algorithm without
    /// allocating a combined intermediate buffer (uses <see cref="System.Security.Cryptography.IncrementalHash"/>).
    /// </summary>
    private byte[] DigestByteRange(byte[] pdfBytes, long[] byteRange, PdfDigestAlgorithm algorithm)
    {
        var algName = algorithm switch
        {
            PdfDigestAlgorithm.Sha256 => System.Security.Cryptography.HashAlgorithmName.SHA256,
            PdfDigestAlgorithm.Sha384 => System.Security.Cryptography.HashAlgorithmName.SHA384,
            PdfDigestAlgorithm.Sha512 => System.Security.Cryptography.HashAlgorithmName.SHA512,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
        };

        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(algName);
        hash.AppendData(pdfBytes, (int)byteRange[0], (int)byteRange[1]);
        hash.AppendData(pdfBytes, (int)byteRange[2], (int)byteRange[3]);
        return hash.GetHashAndReset();
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// Estimates the minimum CMS size needed based on the certificate chain
    /// and optional TSA client, so that <see cref="PdfSignRequest.SignatureContentSize"/>
    /// can be adjusted automatically.
    /// </summary>
    internal static int EstimateSignatureContentSize(
        IReadOnlyList<System.Security.Cryptography.X509Certificates.X509Certificate2> chain,
        ITsaClient? tsaClient)
    {
        // Sum the DER size of every certificate in the chain.
        int chainSize = 0;
        for (int i = 0; i < chain.Count; i++)
            chainSize += chain[i].RawData.Length;

        // Overhead for CMS SignedData envelope + signed attributes
        // (ContentType, MessageDigest, SigningCertificateV2, SigningTime,
        //  SignerInfo BER-TLV, padding for ASN.1 alignment).
        const int cmsOverhead = 2000;

        // TSA response token is typically 2–4 KB.
        const int tsaTokenEstimate = 4096;

        int rawEstimate = cmsOverhead + chainSize + (tsaClient != null ? tsaTokenEstimate : 0);

        // 50 % safety margin, then round up to the nearest even ≥ 512.
        int size = (int)(rawEstimate * 1.5);
        if (size < 512) size = 512;
        if (size % 2 != 0) size++;
        return size;
    }
}

// ---------------------------------------------------------------------------
// Internal helper: wraps PdfLiteWriter with a context that builds the full
// minimal PDF structure and returns the placeholder state.
// ---------------------------------------------------------------------------

internal sealed class PdfLiteWriterContext
{
    private readonly PdfLiteWriter _w;

    internal PdfLiteWriterContext(Stream stream)
    {
        _w = new PdfLiteWriter(stream);
    }

    /// <summary>
    /// Writes a complete minimal PDF with 6–8 objects and returns the
    /// placeholder state for ByteRange and Contents patching.
    /// </summary>
    internal SignaturePlaceholderState WriteMinimalSignedPdf(
        PdfSignRequest request,
        DateTimeOffset signingTime,
        PdfSignatureAppearanceResult? appearance = null,
        int contentSizeOverride = 0)
    {
        // Object numbering (1-based):
        //  1 = Catalog
        //  2 = Pages
        //  3 = Page
        //  4 = AcroForm
        //  5 = Signature Widget (Annot)
        //  6 = Signature Value (/Type /Sig)
        //  7 = Form XObject for normal appearance   (if appearance != null)
        //  8 = Image XObject for logo               (if appearance has image)
        const int catalogObj   = 1;
        const int pagesObj     = 2;
        const int pageObj      = 3;
        const int acroFormObj  = 4;
        const int sigWidgetObj = 5;
        const int sigValueObj  = 6;
        const int formXObjNum  = 7;
        const int imageXObjNum = 8;

        _w.WriteHeader();

        long offCatalog  = _w.WriteCatalog(catalogObj, pagesObj, acroFormObj);
        long offPages    = _w.WritePages(pagesObj, pageObj);
        long offPage     = _w.WritePage(pageObj, pagesObj, sigWidgetObj);
        long offAcroForm = _w.WriteAcroForm(acroFormObj, sigWidgetObj);

        long offWidget;
        if (appearance != null && request.Appearance?.Rectangle != null)
        {
            var r = request.Appearance.Rectangle;
            var rect = new[] { r.X, r.Y, r.X + r.Width, r.Y + r.Height };
            offWidget = _w.WriteVisibleSignatureWidget(
                sigWidgetObj, pageObj, sigValueObj, request.SignatureName,
                rect, formXObjNum);
        }
        else
        {
            offWidget = _w.WriteSignatureWidget(sigWidgetObj, pageObj, sigValueObj, request.SignatureName);
        }

        int finalContentSize = contentSizeOverride > 0 ? contentSizeOverride : request.SignatureContentSize;
        var (offSigVal, state) = _w.WriteSignatureValue(
            sigValueObj,
            request.SubFilter,
            request.Reason,
            request.Location,
            signingTime,
            finalContentSize);

        // Object offsets list (parallel to 1-based object numbers)
        var offsets = new System.Collections.Generic.List<long>
            { offCatalog, offPages, offPage, offAcroForm, offWidget, offSigVal };

        if (appearance != null)
        {
            int rotation = request.Appearance?.PageRotation ?? 0;
            string matrix = ModernPdf.Appearance.DefaultPdfSignatureAppearanceBuilder
                .GetRotationMatrix(rotation, appearance.Width, appearance.Height);

            bool hasImg = appearance.HasImage;
            long offForm = _w.WriteFormXObject(
                formXObjNum,
                appearance.Width, appearance.Height,
                matrix,
                hasImg, imageXObjNum,
                appearance.ContentStream);
            offsets.Add(offForm);

            if (hasImg)
            {
                long offImg = _w.WriteJpegImageXObject(
                    imageXObjNum,
                    appearance.ImagePixelWidth,
                    appearance.ImagePixelHeight,
                    appearance.ImageXObjectData!);
                offsets.Add(offImg);
            }
        }

        long xrefOffset = _w.Position;

        _w.WriteXrefAndTrailer(
            offsets.ToArray(),
            catalogObj,
            xrefOffset);

        return state;
    }
}
