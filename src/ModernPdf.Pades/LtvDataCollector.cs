// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using ModernPdf.Abstractions.Dss;
using ModernPdf.Abstractions.Revocation;

namespace ModernPdf.Pades
{
    /// <summary>
    /// Collects revocation data (OCSP responses and CRLs) for a certificate chain
    /// and packages them into a <see cref="DssData"/> suitable for PAdES-LTV embedding.
    /// </summary>
    public sealed class LtvDataCollector
    {
        private readonly IOcspClient? _ocspClient;
        private readonly ICrlClient? _crlClient;

        /// <param name="ocspClient">Optional OCSP client (preferred over CRL when both are provided).</param>
        /// <param name="crlClient">Optional CRL client (fallback when OCSP is unavailable).</param>
        public LtvDataCollector(IOcspClient? ocspClient = null, ICrlClient? crlClient = null)
        {
            _ocspClient = ocspClient;
            _crlClient  = crlClient;
        }

        /// <summary>
        /// Collects certificates and revocation data for <paramref name="certChain"/> and
        /// returns a <see cref="DssData"/> ready to embed in the signed PDF.
        /// </summary>
        /// <param name="certChain">
        /// Ordered certificate chain from end-entity (index 0) to root (last index).
        /// </param>
        /// <param name="signatureCmsBytes">
        /// Raw DER-encoded CMS SignedData bytes (from <c>PdfSignResult.SignatureCmsBytes</c>).
        /// Used to compute the SHA-1 VRI key.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        public async Task<DssData> CollectAsync(
            IReadOnlyList<X509Certificate2> certChain,
            byte[] signatureCmsBytes,
            CancellationToken ct = default)
        {
            if (certChain == null) throw new ArgumentNullException(nameof(certChain));
            if (signatureCmsBytes == null) throw new ArgumentNullException(nameof(signatureCmsBytes));

            var dss = new DssData();

            // Add all certs to the global pool and build VRI cert-index list.
            var vri = new VriData();
            for (int i = 0; i < certChain.Count; i++)
            {
                dss.Certificates.Add(certChain[i].RawData);
                vri.CertificateIndices.Add(i);
            }

            // For each non-root cert, collect revocation data.
            for (int i = 0; i < certChain.Count - 1; i++)
            {
                var cert   = certChain[i];
                var issuer = certChain[i + 1];

                // Try OCSP first.
                if (_ocspClient != null)
                {
                    var ocspResult = await _ocspClient.CheckRevocationAsync(cert, issuer, null, ct)
                        .ConfigureAwait(false);
                    if (ocspResult.Status != RevocationStatus.Unavailable &&
                        ocspResult.OcspResponseBytes != null)
                    {
                        int idx = dss.OcspResponses.Count;
                        dss.OcspResponses.Add(ocspResult.OcspResponseBytes);
                        vri.OcspIndices.Add(idx);
                        continue;
                    }
                }

                // Fallback to CRL.
                if (_crlClient != null)
                {
                    var crlResult = await _crlClient.CheckRevocationAsync(cert, null, ct)
                        .ConfigureAwait(false);
                    if (crlResult.Status != RevocationStatus.Unavailable &&
                        crlResult.CrlBytes != null)
                    {
                        int idx = dss.Crls.Count;
                        dss.Crls.Add(crlResult.CrlBytes);
                        vri.CrlIndices.Add(idx);
                    }
                }
            }

            // Register VRI entry keyed by uppercase SHA-1 hex of the CMS bytes.
            string vriKey = ComputeVriKey(signatureCmsBytes);
            dss.Vri[vriKey] = vri;

            return dss;
        }

        // SHA-1 of the DER-encoded CMS SignedData, upper-cased hex — the VRI key per PAdES spec.
        // PAdES (ETSI EN 319 102-1) mandates SHA-1 for the VRI hash key; the suppression is intentional.
#pragma warning disable CA5350 // Do not use weak cryptographic algorithms
        private static string ComputeVriKey(byte[] cmsBytes)
        {
            using (var sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(cmsBytes);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToUpperInvariant();
            }
        }
#pragma warning restore CA5350
    }
}
