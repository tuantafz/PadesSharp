// Original implementation based on public standards, no code copied from iText 5/7.

namespace ModernPdf.Abstractions.Revocation;

/// <summary>
/// Certificate revocation status as reported by OCSP or CRL.
/// </summary>
public enum RevocationStatus
{
    /// <summary>Certificate is not revoked (OCSP: good; CRL: not present).</summary>
    Good,

    /// <summary>Certificate has been revoked.</summary>
    Revoked,

    /// <summary>Revocation status is unknown (OCSP: unknown response).</summary>
    Unknown,

    /// <summary>
    /// Revocation information could not be obtained (network error, missing URL, etc.).
    /// </summary>
    Unavailable,
}
