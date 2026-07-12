// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModernPdf.Abstractions.Crypto;
using ModernPdf.Abstractions.Pkcs11;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Nist;
using Org.BouncyCastle.Asn1.X509;

namespace ModernPdf.Pkcs11
{
    /// <summary>
    /// <see cref="ISignatureProvider"/> implementation that signs digests using a
    /// PKCS#11 token or HSM via a <see cref="Pkcs11SessionPool"/>.
    /// Thread-safe: concurrent calls share the pool's session queue.
    /// </summary>
    public sealed class Pkcs11SignatureProvider : ISignatureProvider, IDisposable
    {
        private readonly Pkcs11SessionPool _pool;
        private readonly bool _ownsPool;
        private readonly PdfDigestAlgorithm _digestAlgorithm;
        private readonly ILogger _logger;
        private bool _disposed;

        /// <inheritdoc/>
        public string SignatureAlgorithmName { get; }

        /// <inheritdoc/>
        public string SignatureAlgorithmOid { get; }

        /// <summary>
        /// Gets the certificate representing the signing identity.
        /// </summary>
        public X509Certificate2 Certificate { get; }

        // -----------------------------------------------------------------------
        // Constructors
        // -----------------------------------------------------------------------

        /// <summary>
        /// Creates a provider that opens sessions via <paramref name="sessionFactory"/>
        /// and manages them in an internal pool.
        /// </summary>
        public Pkcs11SignatureProvider(
            IPkcs11SessionFactory sessionFactory,
            Pkcs11SessionRequest sessionRequest,
            X509Certificate2 certificate,
            ILogger? logger = null)
        {
            if (sessionFactory == null) throw new ArgumentNullException(nameof(sessionFactory));
            if (sessionRequest == null) throw new ArgumentNullException(nameof(sessionRequest));
            Certificate      = certificate ?? throw new ArgumentNullException(nameof(certificate));
            _digestAlgorithm = sessionRequest.DigestAlgorithm;
            _logger          = logger ?? NullLogger.Instance;
            _pool            = new Pkcs11SessionPool(sessionFactory, sessionRequest);
            _ownsPool        = true;

            (SignatureAlgorithmName, SignatureAlgorithmOid) = ResolveAlgorithm(_digestAlgorithm);
        }

        /// <summary>
        /// Creates a provider that borrows sessions from an externally managed pool.
        /// The pool is <em>not</em> disposed when this provider is disposed.
        /// </summary>
        public Pkcs11SignatureProvider(
            Pkcs11SessionPool sessionPool,
            X509Certificate2 certificate,
            PdfDigestAlgorithm digestAlgorithm = PdfDigestAlgorithm.Sha256,
            ILogger? logger = null)
        {
            _pool            = sessionPool   ?? throw new ArgumentNullException(nameof(sessionPool));
            Certificate      = certificate   ?? throw new ArgumentNullException(nameof(certificate));
            _digestAlgorithm = digestAlgorithm;
            _logger          = logger ?? NullLogger.Instance;
            _ownsPool        = false;

            (SignatureAlgorithmName, SignatureAlgorithmOid) = ResolveAlgorithm(_digestAlgorithm);
        }

        // -----------------------------------------------------------------------
        // ISignatureProvider
        // -----------------------------------------------------------------------

        /// <inheritdoc/>
        public byte[] SignDigest(byte[] digest, PdfDigestAlgorithm digestAlgorithm)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Pkcs11SignatureProvider));
            if (digest == null) throw new ArgumentNullException(nameof(digest));

            // For CKM_RSA_PKCS the token expects a DigestInfo-wrapped structure.
            byte[] dataToSign = BuildDigestInfo(digest, digestAlgorithm);

            using var lease = _pool.Rent(CancellationToken.None);
            try
            {
                byte[] signature = lease.Session.Sign(dataToSign, Pkcs11SignMechanism.RsaPkcs);
                _logger.LogDebug("PKCS#11 signing completed ({AlgName}).", SignatureAlgorithmName);
                return signature;
            }
            catch (Exception ex)
            {
                // Mark the session as unhealthy so the pool discards it (reconnect on next use).
                lease.IsHealthy = false;
                _logger.LogWarning(ex, "PKCS#11 signing failed; session discarded for reconnect.");
                throw;
            }
        }

        // -----------------------------------------------------------------------
        // IDisposable
        // -----------------------------------------------------------------------

        /// <summary>Disposes the owned pool (if any). Does not dispose the certificate.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_ownsPool) _pool.Dispose();
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Wraps <paramref name="digest"/> in a DER-encoded DigestInfo structure
        /// as required by the CKM_RSA_PKCS mechanism.
        /// </summary>
        public static byte[] BuildDigestInfo(byte[] digest, PdfDigestAlgorithm algorithm)
        {
            var oid = algorithm switch
            {
                PdfDigestAlgorithm.Sha256 => NistObjectIdentifiers.IdSha256,
                PdfDigestAlgorithm.Sha384 => NistObjectIdentifiers.IdSha384,
                PdfDigestAlgorithm.Sha512 => NistObjectIdentifiers.IdSha512,
                _ => throw new NotSupportedException(
                    $"Digest algorithm '{algorithm}' is not supported for PKCS#11 DigestInfo.")
            };

            var algId = new AlgorithmIdentifier(oid, DerNull.Instance);
            return new DigestInfo(algId, digest).GetDerEncoded();
        }

        private static (string Name, string Oid) ResolveAlgorithm(PdfDigestAlgorithm algorithm)
        {
            return algorithm switch
            {
                PdfDigestAlgorithm.Sha256 => ("SHA256withRSA", "1.2.840.113549.1.1.11"),
                PdfDigestAlgorithm.Sha384 => ("SHA384withRSA", "1.2.840.113549.1.1.12"),
                PdfDigestAlgorithm.Sha512 => ("SHA512withRSA", "1.2.840.113549.1.1.13"),
                _ => throw new NotSupportedException($"Digest algorithm '{algorithm}' is not supported.")
            };
        }
    }
}
