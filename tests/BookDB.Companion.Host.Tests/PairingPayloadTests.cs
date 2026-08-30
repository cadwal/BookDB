using System;
using System.Text;
using BookDB.Contracts;
using Xunit;

namespace BookDB.Companion.Host.Tests;

/// <summary>
/// The QR payload: it has to survive the round trip through a scanner, and above all it has to fit in one
/// QR code — the reason the certificates are P-256 in the first place.
/// </summary>
public sealed class PairingPayloadTests
{
    /// <summary>Byte-mode capacity of the largest QR code at the lowest error-correction level.</summary>
    private const int SingleQrByteCapacity = 2953;

    private static PairingPayload Sample() => new()
    {
        Endpoint = "192.168.1.20:7443",
        ServerThumbprints = ["A1B2C3"],
        ClientPfxBase64 = Convert.ToBase64String([1, 2, 3, 4]),
        IssuedAtUtc = new DateTimeOffset(2026, 7, 23, 9, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void APayloadSurvivesTheTripThroughAQrCode()
    {
        var restored = PairingPayload.FromQrString(Sample().ToQrString());

        Assert.Equal("192.168.1.20:7443", restored.Endpoint);
        Assert.Equal(["A1B2C3"], restored.ServerThumbprints);
        Assert.Equal(Convert.ToBase64String([1, 2, 3, 4]), restored.ClientPfxBase64);
        Assert.Equal(new DateTimeOffset(2026, 7, 23, 9, 0, 0, TimeSpan.Zero), restored.IssuedAtUtc);
    }

    [Fact]
    public void SomethingThatIsNotAPairingCodeIsRejectedAsSuch()
    {
        Assert.Throws<FormatException>(() => PairingPayload.FromQrString("just some text"));
        Assert.Throws<FormatException>(() => PairingPayload.FromQrString("null"));
    }

    [Fact]
    public void ARealPairingFitsInASingleQrCode()
    {
        using var certificate = CertificateFactory.CreateDeviceCertificate(Guid.NewGuid().ToString("N"));
        var payload = new PairingPayload
        {
            Endpoint = "192.168.100.200:7443",
            ServerThumbprints = [CertificateFactory.Sha256Thumbprint(certificate)],
            ClientPfxBase64 = Convert.ToBase64String(CertificateFactory.ExportPfx(certificate)),
            IssuedAtUtc = DateTimeOffset.UtcNow,
        };

        // Measured at ~1800 bytes, so there is real headroom; RSA keys would not have left any.
        Assert.True(
            Encoding.UTF8.GetByteCount(payload.ToQrString()) <= SingleQrByteCapacity,
            "the pairing payload no longer fits one QR code");
    }
}
