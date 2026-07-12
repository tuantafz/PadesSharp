// Original implementation based on public standards, no code copied from iText 5/7.

using System.Threading;
using System.Threading.Tasks;

namespace ModernPdf.Abstractions.Tsa;

/// <summary>
/// Obtains an RFC 3161 TimeStampToken from a Time Stamp Authority.
/// </summary>
public interface ITsaClient
{
    /// <summary>
    /// Sends the raw <paramref name="signatureBytes"/> to the TSA for timestamping.
    /// The client hashes the bytes according to its configured digest algorithm,
    /// sends the TSQ, and returns the parsed token.
    /// </summary>
    /// <param name="signatureBytes">
    /// The raw bytes to be timestamped (typically the CMS signature value /
    /// SignerInfo.signature field).
    /// </param>
    /// <param name="cancellationToken">Cancellation support.</param>
    /// <returns>A <see cref="TsaTokenResult"/> describing the outcome.</returns>
    Task<TsaTokenResult> GetTimestampAsync(
        byte[] signatureBytes,
        CancellationToken cancellationToken = default);
}
