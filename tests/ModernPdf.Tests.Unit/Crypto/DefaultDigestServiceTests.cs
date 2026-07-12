// Original implementation based on public standards, no code copied from iText 5/7.

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using ModernPdf.Abstractions.Crypto;
using ModernPdf.Crypto;

namespace ModernPdf.Tests.Unit.Crypto;

/// <summary>
/// Unit tests for <see cref="DefaultDigestService"/>.
/// Known hash vectors from NIST FIPS 180-4 test vectors.
/// </summary>
public class DefaultDigestServiceTests
{
    private readonly DefaultDigestService _sut = new();

    // --- OID Tests ---

    [Theory]
    [InlineData(PdfDigestAlgorithm.Sha256, "2.16.840.1.101.3.4.2.1")]
    [InlineData(PdfDigestAlgorithm.Sha384, "2.16.840.1.101.3.4.2.2")]
    [InlineData(PdfDigestAlgorithm.Sha512, "2.16.840.1.101.3.4.2.3")]
    public void GetDigestOid_Returns_CorrectOid(PdfDigestAlgorithm algorithm, string expectedOid)
    {
        _sut.GetDigestOid(algorithm).Should().Be(expectedOid);
    }

    // --- SHA-256 known vector: SHA-256("abc") ---
    [Fact]
    public void ComputeDigest_Sha256_Bytes_MatchesKnownVector()
    {
        // NIST FIPS 180-4: SHA-256("abc")
        const string expected = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        var input = Encoding.ASCII.GetBytes("abc");

        var result = _sut.ComputeDigest(input, PdfDigestAlgorithm.Sha256);

        Convert.ToHexString(result).ToLowerInvariant()
            .Should().Be(expected);
    }

    // --- SHA-384 known vector: SHA-384("abc") ---
    [Fact]
    public void ComputeDigest_Sha384_Bytes_MatchesKnownVector()
    {
        // NIST: SHA-384("abc")
        const string expected = "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7";
        var input = Encoding.ASCII.GetBytes("abc");

        var result = _sut.ComputeDigest(input, PdfDigestAlgorithm.Sha384);

        Convert.ToHexString(result).ToLowerInvariant()
            .Should().Be(expected);
    }

    // --- SHA-512 known vector: SHA-512("abc") ---
    [Fact]
    public void ComputeDigest_Sha512_Bytes_MatchesKnownVector()
    {
        // NIST: SHA-512("abc")
        const string expected = "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f";
        var input = Encoding.ASCII.GetBytes("abc");

        var result = _sut.ComputeDigest(input, PdfDigestAlgorithm.Sha512);

        Convert.ToHexString(result).ToLowerInvariant()
            .Should().Be(expected);
    }

    // --- Stream overload produces same result as byte[] overload ---
    [Theory]
    [InlineData(PdfDigestAlgorithm.Sha256)]
    [InlineData(PdfDigestAlgorithm.Sha384)]
    [InlineData(PdfDigestAlgorithm.Sha512)]
    public void ComputeDigest_Stream_MatchesByteArray(PdfDigestAlgorithm algorithm)
    {
        var data = Encoding.UTF8.GetBytes("PadesSharp test data 2026");
        using var stream = new MemoryStream(data);

        var fromBytes = _sut.ComputeDigest(data, algorithm);
        var fromStream = _sut.ComputeDigest(stream, algorithm);

        fromStream.Should().Equal(fromBytes);
    }

    // --- Digest sizes ---
    [Theory]
    [InlineData(PdfDigestAlgorithm.Sha256, 32)]
    [InlineData(PdfDigestAlgorithm.Sha384, 48)]
    [InlineData(PdfDigestAlgorithm.Sha512, 64)]
    public void ComputeDigest_Returns_CorrectLength(PdfDigestAlgorithm algorithm, int expectedBytes)
    {
        var result = _sut.ComputeDigest(new byte[] { 0x01, 0x02 }, algorithm);
        result.Should().HaveCount(expectedBytes);
    }

    // --- Null guards ---
    [Fact]
    public void ComputeDigest_NullBytes_Throws()
    {
        var act = () => _sut.ComputeDigest((byte[])null!, PdfDigestAlgorithm.Sha256);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ComputeDigest_NullStream_Throws()
    {
        var act = () => _sut.ComputeDigest((Stream)null!, PdfDigestAlgorithm.Sha256);
        act.Should().Throw<ArgumentNullException>();
    }

    // --- Different inputs produce different digests ---
    [Theory]
    [InlineData(PdfDigestAlgorithm.Sha256)]
    [InlineData(PdfDigestAlgorithm.Sha384)]
    [InlineData(PdfDigestAlgorithm.Sha512)]
    public void ComputeDigest_DifferentInput_ProducesDifferentDigest(PdfDigestAlgorithm algorithm)
    {
        var d1 = _sut.ComputeDigest(Encoding.UTF8.GetBytes("hello"), algorithm);
        var d2 = _sut.ComputeDigest(Encoding.UTF8.GetBytes("world"), algorithm);
        d1.Should().NotEqual(d2);
    }
}
