// Original implementation based on public standards, no code copied from iText 5/7.

using System;

namespace ModernPdf.Abstractions.Revocation;

/// <summary>
/// Result of a CRL revocation check (RFC 5280).
/// </summary>
public sealed class CrlResult
{
    /// <summary>The revocation status determined from the CRL.</summary>
    public RevocationStatus Status { get; set; }

    /// <summary>
    /// The <c>thisUpdate</c> field from the CRL (issue date of the CRL).
    /// </summary>
    public DateTimeOffset? ThisUpdate { get; set; }

    /// <summary>
    /// The <c>nextUpdate</c> field from the CRL (date by which the next CRL is issued).
    /// </summary>
    public DateTimeOffset? NextUpdate { get; set; }

    /// <summary>
    /// The <c>revocationDate</c> from the CRL entry, if
    /// <see cref="Status"/> is <see cref="RevocationStatus.Revoked"/>.
    /// </summary>
    public DateTimeOffset? RevocationTime { get; set; }

    /// <summary>
    /// The revocation reason string, if the CRL entry includes a reason code extension.
    /// </summary>
    public string? RevocationReason { get; set; }

    /// <summary>
    /// Human-readable error detail when <see cref="Status"/> is
    /// <see cref="RevocationStatus.Unavailable"/>.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Raw DER bytes of the downloaded CRL, suitable for embedding in the
    /// PAdES DSS dictionary (/CRLs array).
    /// </summary>
    public byte[]? CrlBytes { get; set; }

    /// <summary>CRL distribution point URL that was used. <c>null</c> if lookup failed.</summary>
    public string? CrlUrl { get; set; }
}
