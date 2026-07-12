// Original implementation based on public standards, no code copied from iText 5/7.

using System;

namespace ModernPdf.Abstractions.Revocation;

/// <summary>
/// Result of an OCSP revocation check (RFC 6960).
/// </summary>
public sealed class OcspResult
{
    /// <summary>The revocation status determined from the OCSP response.</summary>
    public RevocationStatus Status { get; set; }

    /// <summary>
    /// The <c>thisUpdate</c> field from the OCSP SingleResponse.
    /// Indicates when the revocation information was last known to be correct.
    /// </summary>
    public DateTimeOffset? ThisUpdate { get; set; }

    /// <summary>
    /// The <c>nextUpdate</c> field from the OCSP SingleResponse.
    /// Indicates when fresher information will be available.
    /// </summary>
    public DateTimeOffset? NextUpdate { get; set; }

    /// <summary>
    /// The <c>revocationTime</c> if <see cref="Status"/> is
    /// <see cref="RevocationStatus.Revoked"/>.
    /// </summary>
    public DateTimeOffset? RevocationTime { get; set; }

    /// <summary>
    /// The <c>revocationReason</c> string if <see cref="Status"/> is
    /// <see cref="RevocationStatus.Revoked"/> and a reason code was supplied.
    /// </summary>
    public string? RevocationReason { get; set; }

    /// <summary>
    /// Human-readable error detail when <see cref="Status"/> is
    /// <see cref="RevocationStatus.Unavailable"/>.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Raw DER bytes of the OCSPResponse, suitable for embedding in the
    /// PAdES DSS dictionary (/OCSPs array).
    /// </summary>
    public byte[]? OcspResponseBytes { get; set; }

    /// <summary>OCSP responder URL that was queried. <c>null</c> if lookup failed.</summary>
    public string? ResponderUrl { get; set; }
}
