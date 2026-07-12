// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using ModernPdf.Abstractions.Crypto;
using ModernPdf.Abstractions.Pkcs11;
using ModernPdf.Pkcs11;

namespace ModernPdf.Tests.Unit.Pkcs11Tests;

// ===========================================================================
// Helpers
// ===========================================================================

internal static class Pkcs11TestCert
{
    public static X509Certificate2 Create()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=Pkcs11Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
    }
}

internal static class FakeSessionFactory
{
    /// <summary>
    /// Creates a mock factory that returns a mock session which echoes back the data
    /// as its "signature" (useful for verifying the right bytes were sent to Sign).
    /// </summary>
    public static (Mock<IPkcs11SessionFactory> Factory, Mock<IPkcs11Session> Session) CreateEcho()
    {
        var session = new Mock<IPkcs11Session>();
        session
            .Setup(s => s.Sign(It.IsAny<byte[]>(), It.IsAny<Pkcs11SignMechanism>()))
            .Returns<byte[], Pkcs11SignMechanism>((data, _) => data); // echo data back

        var factory = new Mock<IPkcs11SessionFactory>();
        factory
            .Setup(f => f.OpenSession(It.IsAny<Pkcs11SessionRequest>()))
            .Returns(session.Object);

        return (factory, session);
    }

    /// <summary>Returns a factory whose session always throws on Sign.</summary>
    public static Mock<IPkcs11SessionFactory> CreateFailing()
    {
        var session = new Mock<IPkcs11Session>();
        session
            .Setup(s => s.Sign(It.IsAny<byte[]>(), It.IsAny<Pkcs11SignMechanism>()))
            .Throws(new InvalidOperationException("Simulated PKCS#11 session failure."));

        var factory = new Mock<IPkcs11SessionFactory>();
        factory
            .Setup(f => f.OpenSession(It.IsAny<Pkcs11SessionRequest>()))
            .Returns(session.Object);

        return factory;
    }
}

// ===========================================================================
// Pkcs11SignMechanism enum
// ===========================================================================

public class Pkcs11SignMechanismTests
{
    [Fact]
    public void Enum_HasFourExpectedValues()
    {
        Enum.IsDefined(typeof(Pkcs11SignMechanism), Pkcs11SignMechanism.RsaPkcs).Should().BeTrue();
        Enum.IsDefined(typeof(Pkcs11SignMechanism), Pkcs11SignMechanism.Sha256RsaPkcs).Should().BeTrue();
        Enum.IsDefined(typeof(Pkcs11SignMechanism), Pkcs11SignMechanism.Sha384RsaPkcs).Should().BeTrue();
        Enum.IsDefined(typeof(Pkcs11SignMechanism), Pkcs11SignMechanism.Sha512RsaPkcs).Should().BeTrue();
    }

    [Fact]
    public void RsaPkcs_HasZeroValue()
    {
        ((int)Pkcs11SignMechanism.RsaPkcs).Should().Be(0);
    }
}

// ===========================================================================
// Pkcs11SessionRequest
// ===========================================================================

public class Pkcs11SessionRequestTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var req = new Pkcs11SessionRequest();
        req.MaxSessions.Should().Be(4);
        req.SignMechanism.Should().Be(Pkcs11SignMechanism.RsaPkcs);
        req.DigestAlgorithm.Should().Be(PdfDigestAlgorithm.Sha256);
        req.Pin.Should().BeNull();
        req.SlotId.Should().BeNull();
        req.KeyAlias.Should().BeNull();
    }

    [Fact]
    public void DebuggerDisplay_DoesNotContainPin()
    {
        var req = new Pkcs11SessionRequest
        {
            LibraryPath = "/usr/lib/softhsm/libsofthsm2.so",
            Pin         = "s3cr3t",
            KeyAlias    = "mykey",
            SlotId      = "1",
        };

        string display = req.ToString() ?? string.Empty;
        display.Should().NotContain("s3cr3t", "PIN must never appear in string representations");
    }
}

// ===========================================================================
// Pkcs11SignatureProvider — algorithm metadata
// ===========================================================================

public class Pkcs11SignatureProviderAlgorithmTests : IDisposable
{
    private readonly X509Certificate2 _cert = Pkcs11TestCert.Create();

    public void Dispose() => _cert.Dispose();

    private Pkcs11SignatureProvider Build(PdfDigestAlgorithm algorithm)
    {
        var (factory, _) = FakeSessionFactory.CreateEcho();
        var request = new Pkcs11SessionRequest { DigestAlgorithm = algorithm };
        var pool = new Pkcs11SessionPool(factory.Object, request, 1);
        return new Pkcs11SignatureProvider(pool, _cert, algorithm);
    }

    [Fact]
    public void Sha256_AlgorithmName_IsCorrect()
    {
        using var sut = Build(PdfDigestAlgorithm.Sha256);
        sut.SignatureAlgorithmName.Should().Be("SHA256withRSA");
    }

    [Fact]
    public void Sha256_AlgorithmOid_IsCorrect()
    {
        using var sut = Build(PdfDigestAlgorithm.Sha256);
        sut.SignatureAlgorithmOid.Should().Be("1.2.840.113549.1.1.11");
    }

    [Fact]
    public void Sha384_AlgorithmOid_IsCorrect()
    {
        using var sut = Build(PdfDigestAlgorithm.Sha384);
        sut.SignatureAlgorithmOid.Should().Be("1.2.840.113549.1.1.12");
    }

    [Fact]
    public void Sha512_AlgorithmOid_IsCorrect()
    {
        using var sut = Build(PdfDigestAlgorithm.Sha512);
        sut.SignatureAlgorithmOid.Should().Be("1.2.840.113549.1.1.13");
    }
}

// ===========================================================================
// Pkcs11SignatureProvider — signing behavior
// ===========================================================================

public class Pkcs11SignatureProviderSigningTests : IDisposable
{
    private readonly X509Certificate2 _cert = Pkcs11TestCert.Create();

    public void Dispose() => _cert.Dispose();

    [Fact]
    public void SignDigest_CallsSessionSign_WithDigestInfo()
    {
        var (factory, session) = FakeSessionFactory.CreateEcho();
        var request = new Pkcs11SessionRequest { DigestAlgorithm = PdfDigestAlgorithm.Sha256 };
        using var pool = new Pkcs11SessionPool(factory.Object, request, 1);
        using var sut  = new Pkcs11SignatureProvider(pool, _cert, PdfDigestAlgorithm.Sha256);

        var digest = new byte[32]; // 32 zero bytes simulating a SHA-256 hash
        sut.SignDigest(digest, PdfDigestAlgorithm.Sha256);

        // Verify Sign was called exactly once on the session with RsaPkcs mechanism.
        session.Verify(s => s.Sign(It.IsAny<byte[]>(), Pkcs11SignMechanism.RsaPkcs), Times.Once);
    }

    [Fact]
    public void SignDigest_InputToSession_IsDigestInfoDer()
    {
        byte[]? capturedInput = null;
        var session = new Mock<IPkcs11Session>();
        session
            .Setup(s => s.Sign(It.IsAny<byte[]>(), It.IsAny<Pkcs11SignMechanism>()))
            .Callback<byte[], Pkcs11SignMechanism>((data, _) => capturedInput = data)
            .Returns(new byte[256]);

        var factory = new Mock<IPkcs11SessionFactory>();
        factory.Setup(f => f.OpenSession(It.IsAny<Pkcs11SessionRequest>())).Returns(session.Object);

        var request = new Pkcs11SessionRequest();
        using var pool = new Pkcs11SessionPool(factory.Object, request, 1);
        using var sut  = new Pkcs11SignatureProvider(pool, _cert, PdfDigestAlgorithm.Sha256);

        var digest = SHA256.HashData(new byte[] { 1, 2, 3 });
        sut.SignDigest(digest, PdfDigestAlgorithm.Sha256);

        capturedInput.Should().NotBeNull();
        // DigestInfo DER starts with 0x30 (SEQUENCE)
        capturedInput![0].Should().Be(0x30, "DigestInfo must be DER SEQUENCE");
        // Must be longer than the raw digest (wraps AlgorithmIdentifier + OctetString)
        capturedInput.Length.Should().BeGreaterThan(digest.Length);
    }

    [Fact]
    public void SignDigest_NullDigest_ThrowsArgumentNullException()
    {
        var (factory, _) = FakeSessionFactory.CreateEcho();
        var request = new Pkcs11SessionRequest();
        using var pool = new Pkcs11SessionPool(factory.Object, request, 1);
        using var sut  = new Pkcs11SignatureProvider(pool, _cert);

        Action act = () => sut.SignDigest(null!, PdfDigestAlgorithm.Sha256);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SignDigest_AfterDispose_ThrowsObjectDisposedException()
    {
        var (factory, _) = FakeSessionFactory.CreateEcho();
        var request = new Pkcs11SessionRequest();
        using var pool = new Pkcs11SessionPool(factory.Object, request, 1);
        var sut = new Pkcs11SignatureProvider(pool, _cert);

        sut.Dispose();

        Action act = () => sut.SignDigest(new byte[32], PdfDigestAlgorithm.Sha256);
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void SignDigest_WhenSessionFails_SessionIsMarkedUnhealthy()
    {
        int callCount = 0;

        // First session fails, second session succeeds.
        var failSession = new Mock<IPkcs11Session>();
        failSession
            .Setup(s => s.Sign(It.IsAny<byte[]>(), It.IsAny<Pkcs11SignMechanism>()))
            .Throws(new InvalidOperationException("Token disconnected."));

        var goodSession = new Mock<IPkcs11Session>();
        goodSession
            .Setup(s => s.Sign(It.IsAny<byte[]>(), It.IsAny<Pkcs11SignMechanism>()))
            .Returns(new byte[256]);

        var factory = new Mock<IPkcs11SessionFactory>();
        factory
            .Setup(f => f.OpenSession(It.IsAny<Pkcs11SessionRequest>()))
            .Returns(() => ++callCount == 1 ? failSession.Object : goodSession.Object);

        var request = new Pkcs11SessionRequest();
        using var pool = new Pkcs11SessionPool(factory.Object, request, 1);
        using var sut  = new Pkcs11SignatureProvider(pool, _cert);

        // First call fails.
        Action failAct = () => sut.SignDigest(new byte[32], PdfDigestAlgorithm.Sha256);
        failAct.Should().Throw<InvalidOperationException>();

        // Second call should succeed (pool discarded bad session, opened fresh one).
        byte[] result = sut.SignDigest(new byte[32], PdfDigestAlgorithm.Sha256);
        result.Should().NotBeNull();
        callCount.Should().Be(2, "two sessions should have been opened (reconnect)");
    }
}

// ===========================================================================
// Pkcs11SignatureProvider — BuildDigestInfo
// ===========================================================================

public class BuildDigestInfoTests
{
    [Theory]
    [InlineData(PdfDigestAlgorithm.Sha256, 32)]
    [InlineData(PdfDigestAlgorithm.Sha384, 48)]
    [InlineData(PdfDigestAlgorithm.Sha512, 64)]
    public void BuildDigestInfo_IsDerSequence(PdfDigestAlgorithm algorithm, int digestLen)
    {
        var digest = new byte[digestLen];
        byte[] result = Pkcs11SignatureProvider.BuildDigestInfo(digest, algorithm);

        result[0].Should().Be(0x30, "DigestInfo must start with DER SEQUENCE tag");
        result.Length.Should().BeGreaterThan(digestLen, "DigestInfo must be larger than raw digest");
    }

    [Fact]
    public void BuildDigestInfo_UnsupportedAlgorithm_Throws()
    {
        Action act = () => Pkcs11SignatureProvider.BuildDigestInfo(new byte[32], (PdfDigestAlgorithm)99);
        act.Should().Throw<NotSupportedException>();
    }
}

// ===========================================================================
// Pkcs11SessionPool
// ===========================================================================

public class Pkcs11SessionPoolTests
{
    [Fact]
    public void Pool_NullFactory_Throws()
    {
        var request = new Pkcs11SessionRequest();
        Action act = () => new Pkcs11SessionPool(null!, request, 1);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Pool_NullRequest_Throws()
    {
        var factory = new Mock<IPkcs11SessionFactory>();
        Action act = () => new Pkcs11SessionPool(factory.Object, null!, 1);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Rent_CreatesSessionFromFactory()
    {
        var (factory, _) = FakeSessionFactory.CreateEcho();
        var request = new Pkcs11SessionRequest();
        using var pool = new Pkcs11SessionPool(factory.Object, request, 1);

        using var lease = pool.Rent();

        lease.Session.Should().NotBeNull();
        factory.Verify(f => f.OpenSession(request), Times.Once);
    }

    [Fact]
    public void Rent_HealthySession_ReturnedToQueue_NotRecreated()
    {
        var (factory, _) = FakeSessionFactory.CreateEcho();
        var request = new Pkcs11SessionRequest();
        using var pool = new Pkcs11SessionPool(factory.Object, request, 1);

        // Rent and return once.
        using (var lease = pool.Rent()) { /* healthy by default */ }

        // Rent again — should reuse the existing session.
        using (var lease2 = pool.Rent()) { }

        factory.Verify(f => f.OpenSession(request), Times.Once, "session should be reused");
    }

    [Fact]
    public void Rent_UnhealthySession_NotReused()
    {
        var (factory, _) = FakeSessionFactory.CreateEcho();
        var request = new Pkcs11SessionRequest();
        using var pool = new Pkcs11SessionPool(factory.Object, request, 1);

        using (var lease = pool.Rent())
        {
            lease.IsHealthy = false; // mark as bad
        }

        using (var lease2 = pool.Rent()) { }

        factory.Verify(f => f.OpenSession(request), Times.Exactly(2), "unhealthy session should not be reused");
    }

    [Fact]
    public void Rent_AfterDispose_Throws()
    {
        var (factory, _) = FakeSessionFactory.CreateEcho();
        var request = new Pkcs11SessionRequest();
        var pool = new Pkcs11SessionPool(factory.Object, request, 1);
        pool.Dispose();

        Action act = () => pool.Rent();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Rent_ConcurrentUpToMaxSessions_AllSucceed()
    {
        const int max = 4;
        var (factory, _) = FakeSessionFactory.CreateEcho();
        var request = new Pkcs11SessionRequest { MaxSessions = max };
        using var pool = new Pkcs11SessionPool(factory.Object, request, max);

        // Rent all slots simultaneously.
        var leases = new Pkcs11SessionLease[max];
        for (int i = 0; i < max; i++)
            leases[i] = pool.Rent();

        leases.Should().AllSatisfy(l => l.Session.Should().NotBeNull());

        foreach (var l in leases) l.Dispose();
    }

    [Fact]
    public void Rent_BeyondMax_BlocksUntilReturned()
    {
        var (factory, _) = FakeSessionFactory.CreateEcho();
        var request = new Pkcs11SessionRequest();
        using var pool = new Pkcs11SessionPool(factory.Object, request, 1);

        using var lease1 = pool.Rent();

        // Trying to rent while pool is exhausted should time out with CancellationToken.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        Action act = () => pool.Rent(cts.Token);
        act.Should().Throw<OperationCanceledException>();
    }
}

// ===========================================================================
// DefaultPkcs11SessionFactory — guard tests (no real PKCS#11 library needed)
// ===========================================================================

public class DefaultPkcs11SessionFactoryTests
{
    [Fact]
    public void Constructor_NonExistentLibrary_ThrowsFileNotFoundException()
    {
        Action act = () => new DefaultPkcs11SessionFactory(@"C:\DoesNotExist\fake_pkcs11.dll");
        act.Should().Throw<System.IO.FileNotFoundException>()
            .WithMessage("*fake_pkcs11.dll*");
    }

    [Fact]
    public void Constructor_NullOrEmptyPath_Throws()
    {
        Action act1 = () => new DefaultPkcs11SessionFactory(null!);
        Action act2 = () => new DefaultPkcs11SessionFactory(string.Empty);
        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
    }
}
