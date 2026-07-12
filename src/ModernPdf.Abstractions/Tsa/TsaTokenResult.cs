// Original implementation based on public standards, no code copied from iText 5/7.

using System;

namespace ModernPdf.Abstractions.Tsa;

/// <summary>
/// Represents the result of requesting a timestamp token from an RFC 3161 TSA.
/// </summary>
public sealed class TsaTokenResult
{
    /// <summary>
    /// <c>true</c> if the TSA returned a valid, parseable timestamp token
    /// with a granted (or grantedWithMods) status.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// DER-encoded TimeStampToken (RFC 3161 §2.4.2 — a CMS ContentInfo).
    /// <c>null</c> when <see cref="Success"/> is <c>false</c>.
    /// </summary>
    public byte[]? TokenBytes { get; set; }

    /// <summary>
    /// The <c>genTime</c> field from the TSTInfo inside the token.
    /// <c>null</c> when <see cref="Success"/> is <c>false</c>.
    /// </summary>
    public DateTimeOffset? TimestampTime { get; set; }

    /// <summary>Human-readable error description when <see cref="Success"/> is <c>false</c>.</summary>
    public string? ErrorMessage { get; set; }
}
