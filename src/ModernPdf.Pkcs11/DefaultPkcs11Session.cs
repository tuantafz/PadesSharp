// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using ModernPdf.Abstractions.Pkcs11;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;

namespace ModernPdf.Pkcs11
{
    /// <summary>
    /// A PKCS#11 session backed by a real <see cref="ISession"/> from Pkcs11Interop.
    /// Opened and authenticated by <see cref="DefaultPkcs11SessionFactory"/>.
    /// </summary>
    public sealed class DefaultPkcs11Session : IPkcs11Session
    {
        private readonly ISession _session;
        private readonly IObjectHandle _privateKeyHandle;
        private readonly Pkcs11InteropFactories _factories;
        private bool _disposed;

        internal DefaultPkcs11Session(
            ISession session,
            IObjectHandle privateKeyHandle,
            Pkcs11InteropFactories factories)
        {
            _session          = session          ?? throw new ArgumentNullException(nameof(session));
            _privateKeyHandle = privateKeyHandle ?? throw new ArgumentNullException(nameof(privateKeyHandle));
            _factories        = factories        ?? throw new ArgumentNullException(nameof(factories));
        }

        /// <inheritdoc/>
        public byte[] Sign(byte[] dataToSign, Pkcs11SignMechanism mechanism)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DefaultPkcs11Session));
            if (dataToSign == null) throw new ArgumentNullException(nameof(dataToSign));

            var ckm = mechanism switch
            {
                Pkcs11SignMechanism.RsaPkcs      => CKM.CKM_RSA_PKCS,
                Pkcs11SignMechanism.Sha256RsaPkcs => CKM.CKM_SHA256_RSA_PKCS,
                Pkcs11SignMechanism.Sha384RsaPkcs => CKM.CKM_SHA384_RSA_PKCS,
                Pkcs11SignMechanism.Sha512RsaPkcs => CKM.CKM_SHA512_RSA_PKCS,
                _ => throw new NotSupportedException($"PKCS#11 mechanism '{mechanism}' is not supported.")
            };

            var mech = _factories.MechanismFactory.Create(ckm);
            return _session.Sign(mech, _privateKeyHandle, dataToSign);
        }

        /// <inheritdoc/>
        public X509Certificate2 GetCertificate(string certificateLabel)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DefaultPkcs11Session));
            if (certificateLabel == null) throw new ArgumentNullException(nameof(certificateLabel));

            var attrs = new List<IObjectAttribute>
            {
                _factories.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_CERTIFICATE),
                _factories.ObjectAttributeFactory.Create(CKA.CKA_LABEL, certificateLabel),
            };

            var certObjects = _session.FindAllObjects(attrs);
            if (certObjects == null || certObjects.Count == 0)
                throw new InvalidOperationException(
                    $"Certificate with label '{certificateLabel}' was not found on the token.");

            // Read the DER-encoded certificate value.
            var valueAttrList = _session.GetAttributeValue(certObjects[0],
                new List<CKA> { CKA.CKA_VALUE });
            byte[] certDer = valueAttrList[0].GetValueAsByteArray();

            return new X509Certificate2(certDer);
        }

        /// <summary>Logs out and releases the underlying PKCS#11 session.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _session.Logout(); } catch { /* session may already be invalid */ }
            _session.Dispose();
        }
    }
}
