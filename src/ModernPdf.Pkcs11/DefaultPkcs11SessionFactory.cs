// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ModernPdf.Abstractions.Pkcs11;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;

namespace ModernPdf.Pkcs11
{
    /// <summary>
    /// Loads a native PKCS#11 shared library and opens authenticated sessions.
    /// One factory instance manages a single library handle; reuse it across
    /// multiple <see cref="Pkcs11SessionPool"/> instances targeting the same library.
    /// </summary>
    public sealed class DefaultPkcs11SessionFactory : IPkcs11SessionFactory, IDisposable
    {
        private readonly IPkcs11Library _library;
        private readonly Pkcs11InteropFactories _factories;
        private bool _disposed;

        /// <summary>
        /// Loads the native PKCS#11 library at <paramref name="libraryPath"/>.
        /// </summary>
        /// <param name="libraryPath">Full path to the .so / .dll PKCS#11 library.</param>
        /// <exception cref="FileNotFoundException">
        /// Thrown when <paramref name="libraryPath"/> does not exist.
        /// </exception>
        public DefaultPkcs11SessionFactory(string libraryPath)
        {
            if (string.IsNullOrWhiteSpace(libraryPath))
                throw new ArgumentNullException(nameof(libraryPath));
            if (!File.Exists(libraryPath))
                throw new FileNotFoundException(
                    $"PKCS#11 library not found: {libraryPath}", libraryPath);

            _factories = new Pkcs11InteropFactories();
            _library   = _factories.Pkcs11LibraryFactory.LoadPkcs11Library(
                _factories, libraryPath, AppType.MultiThreaded);
        }

        /// <inheritdoc/>
        public IPkcs11Session OpenSession(Pkcs11SessionRequest request)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DefaultPkcs11SessionFactory));
            if (request == null) throw new ArgumentNullException(nameof(request));

            // Enumerate all slots (with or without token present) and find the requested one.
            var slots = _library.GetSlotList(SlotsType.WithOrWithoutTokenPresent);
            if (slots == null || slots.Count == 0)
                throw new InvalidOperationException("No PKCS#11 slots reported by the library.");

            ISlot slot = ResolveSlot(slots, request.SlotId);

            // Open a read-write session and log in if a PIN was provided.
            var session = slot.OpenSession(SessionType.ReadWrite);
            try
            {
                if (!string.IsNullOrEmpty(request.Pin))
                    session.Login(CKU.CKU_USER, request.Pin);

                // Locate the private key by label.
                var keyHandle = FindPrivateKey(session, request.KeyAlias);
                return new DefaultPkcs11Session(session, keyHandle, _factories);
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        /// <summary>Disposes the PKCS#11 library handle.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _library?.Dispose();
        }

        // -----------------------------------------------------------------------
        // Private helpers
        // -----------------------------------------------------------------------

        private static ISlot ResolveSlot(IList<ISlot> slots, string? slotId)
        {
            if (string.IsNullOrEmpty(slotId))
                return slots[0];

            if (ulong.TryParse(slotId, out ulong id))
            {
                var match = slots.FirstOrDefault(s => s.SlotId == id);
                if (match != null) return match;
            }

            throw new InvalidOperationException(
                $"PKCS#11 slot '{slotId}' was not found. Available slots: " +
                string.Join(", ", slots.Select(s => s.SlotId)));
        }

        private IObjectHandle FindPrivateKey(ISession session, string? keyAlias)
        {
            var attrs = new List<IObjectAttribute>
            {
                _factories.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_PRIVATE_KEY),
            };

            if (!string.IsNullOrEmpty(keyAlias))
                attrs.Add(_factories.ObjectAttributeFactory.Create(CKA.CKA_LABEL, keyAlias));

            var keys = session.FindAllObjects(attrs);
            if (keys == null || keys.Count == 0)
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(keyAlias)
                        ? "No private key found on the token."
                        : $"Private key with label '{keyAlias}' was not found on the token.");

            return keys[0];
        }
    }
}
