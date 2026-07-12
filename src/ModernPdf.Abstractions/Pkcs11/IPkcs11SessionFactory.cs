// Original implementation based on public standards, no code copied from iText 5/7.

namespace ModernPdf.Abstractions.Pkcs11
{
    /// <summary>
    /// Creates PKCS#11 sessions to a token/HSM.
    /// Each call to <see cref="OpenSession"/> produces a new, independent session.
    /// </summary>
    public interface IPkcs11SessionFactory
    {
        /// <summary>
        /// Opens and authenticates a new PKCS#11 session using the supplied parameters.
        /// The returned session must be disposed by the caller.
        /// </summary>
        /// <param name="request">Session parameters (library path, slot, PIN, key alias).</param>
        /// <returns>An open, logged-in <see cref="IPkcs11Session"/>.</returns>
        IPkcs11Session OpenSession(Pkcs11SessionRequest request);
    }
}
