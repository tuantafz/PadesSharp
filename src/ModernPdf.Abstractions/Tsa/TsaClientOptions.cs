// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using ModernPdf.Abstractions.Crypto;

namespace ModernPdf.Abstractions.Tsa;

/// <summary>
/// Configuration for an RFC 3161 Time Stamp Authority (TSA) client.
/// </summary>
public sealed class TsaClientOptions
{
    /// <summary>
    /// URL of the TSA HTTP endpoint. Must be an absolute URI.
    /// </summary>
    public string TsaUrl { get; set; } = string.Empty;

    /// <summary>
    /// Hash algorithm to use for the message imprint in the TSQ.
    /// Default: <see cref="PdfDigestAlgorithm.Sha256"/>.
    /// </summary>
    public PdfDigestAlgorithm DigestAlgorithm { get; set; } = PdfDigestAlgorithm.Sha256;

    /// <summary>
    /// When <c>true</c>, asks the TSA to include its certificate in the response token.
    /// Default: <c>true</c>.
    /// </summary>
    public bool RequestCertificate { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, a 64-bit random nonce is included in each request to
    /// prevent replay attacks. Default: <c>true</c>.
    /// </summary>
    public bool UseNonce { get; set; } = true;

    /// <summary>HTTP request timeout. Default: 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum number of attempts (initial + retries). Default: 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Base delay for exponential back-off between retries. Default: 500 ms.
    /// </summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);
}
