// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ModernPdf.Abstractions.Signing;

namespace ModernPdf.Signing
{
    /// <summary>
    /// Convenience helpers for signing to / from file paths.
    /// Internally creates a <see cref="MemoryStream"/> buffer, signs, then writes
    /// atomically to the output file — avoiding partial-write corruption and
    /// cross-platform file-lock issues.
    /// </summary>
    public static class PdfSigningFileHelper
    {
        /// <summary>
        /// Signs using <paramref name="signer"/> and writes the result to
        /// <paramref name="outputPath"/>.  The parent directory is created if absent.
        /// </summary>
        /// <param name="outputPath">Destination file path (created or overwritten).</param>
        /// <param name="request">
        /// Sign request.  <see cref="PdfSignRequest.OutputPdf"/> is set by this method;
        /// any value already assigned is replaced.
        /// </param>
        /// <param name="signer">The signer to use.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The same <see cref="PdfSignResult"/> returned by the signer.</returns>
        public static async Task<PdfSignResult> SignToFileAsync(
            string outputPath,
            PdfSignRequest request,
            IPdfSigner signer,
            CancellationToken cancellationToken = default)
        {
            if (outputPath == null)   throw new ArgumentNullException(nameof(outputPath));
            if (request   == null)   throw new ArgumentNullException(nameof(request));
            if (signer    == null)   throw new ArgumentNullException(nameof(signer));

            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // Sign into a MemoryStream so the engine can seek back to patch placeholders.
            using var ms = new MemoryStream();
            request.OutputPdf = ms;
            PdfSignResult result = await signer.SignAsync(request, cancellationToken).ConfigureAwait(false);

            // Flush signed bytes to the output file atomically (single write).
            ms.Position = 0;
#if NET8_0_OR_GREATER
            await using var fs = new FileStream(
                outputPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true);
            await ms.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
#else
            using (var fs = new FileStream(
                outputPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true))
            {
                await ms.CopyToAsync(fs).ConfigureAwait(false);
                await fs.FlushAsync().ConfigureAwait(false);
            }
#endif
            return result;
        }

        /// <summary>
        /// Reads an existing PDF from <paramref name="inputPath"/>, sets it as
        /// <see cref="PdfSignRequest.InputPdf"/>, then signs and writes the result
        /// to <paramref name="outputPath"/>.  Input and output may be the same path.
        /// </summary>
        public static async Task<PdfSignResult> SignFileAsync(
            string inputPath,
            string outputPath,
            PdfSignRequest request,
            IPdfSigner signer,
            CancellationToken cancellationToken = default)
        {
            if (inputPath  == null) throw new ArgumentNullException(nameof(inputPath));
            if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));
            if (request    == null) throw new ArgumentNullException(nameof(request));
            if (signer     == null) throw new ArgumentNullException(nameof(signer));

            // Read input fully first — avoids file-lock conflicts when input == output.
            byte[] inputBytes;
#if NETSTANDARD2_0 || NET48
            inputBytes = File.ReadAllBytes(inputPath);
#else
            inputBytes = await File.ReadAllBytesAsync(inputPath, cancellationToken).ConfigureAwait(false);
#endif
            request.InputPdf = new MemoryStream(inputBytes, writable: false);

            return await SignToFileAsync(outputPath, request, signer, cancellationToken).ConfigureAwait(false);
        }
    }
}
