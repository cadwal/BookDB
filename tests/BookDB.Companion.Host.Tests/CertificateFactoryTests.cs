using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BookDB.Companion.Host;
using Xunit;

namespace BookDB.Companion.Host.Tests;

/// <summary>
/// The certificate profile the whole companion link depends on: P-256 keys small enough for a QR code, the
/// right extended key usages on each side, and SHA-256 thumbprints rather than the SHA-1 property.
/// </summary>
public sealed class CertificateFactoryTests
{
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";

    private static string[] EnhancedKeyUsages(X509Certificate2 certificate)
        => certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>()
            .SelectMany(e => e.EnhancedKeyUsages.OfType<Oid>())
            .Select(o => o.Value!)
            .ToArray();

    [Fact]
    public void ServerCertificateUsesAP256KeyAndServerAuthentication()
    {
        using var certificate = CertificateFactory.CreateServerCertificate("BookDB-Test");

        using var key = certificate.GetECDsaPrivateKey();
        Assert.NotNull(key);
        Assert.Equal(256, key!.KeySize);
        Assert.Null(certificate.GetRSAPrivateKey());
        Assert.Equal([ServerAuthenticationOid], EnhancedKeyUsages(certificate));
        Assert.Contains("BookDB-Test", certificate.Subject);
    }

    [Fact]
    public void ServerCertificateCarriesASubjectAlternativeNameForTheTlsHandshake()
    {
        using var certificate = CertificateFactory.CreateServerCertificate("BookDB-Test");

        var san = certificate.Extensions.FirstOrDefault(e => e.Oid?.Value == "2.5.29.17");
        Assert.NotNull(san);
        Assert.Contains("localhost", san!.Format(false));
    }

    [Theory]
    // An empty Environment.MachineName leaves the subject a bare "BookDB-", and a DNS label may not end
    // in a hyphen; a long machine name passes the 63-character label limit. Both were refused as invalid
    // IDN names, and the throw came out of host startup rather than anywhere near the certificate.
    [InlineData("BookDB-")]
    [InlineData("BookDB-----")]
    [InlineData("BookDB-verylongmachinenamethatgoeswellpastthesixtythreecharacterlabellimitforadnsname")]
    [InlineData("BookDB-host..local")]
    [InlineData("-")]
    [InlineData(".")]
    public void ServerCertificateIsMintedWhateverTheMachineIsCalled(string subjectName)
    {
        using var certificate = CertificateFactory.CreateServerCertificate(subjectName);

        var san = certificate.Extensions.FirstOrDefault(e => e.Oid?.Value == "2.5.29.17");
        Assert.NotNull(san);
        Assert.Contains("localhost", san!.Format(false));
    }

    [Fact]
    public void ServerCertificateKeepsTheMachineNameInItsSubjectEvenWhenTheDnsNameIsCleanedUp()
    {
        using var certificate = CertificateFactory.CreateServerCertificate("BookDB-");

        Assert.Contains("BookDB-", certificate.Subject);
    }

    [Fact]
    public void ServerCertificateIsValidNowAndBackdated()
    {
        using var certificate = CertificateFactory.CreateServerCertificate("BookDB-Test");

        Assert.True(certificate.NotBefore.ToUniversalTime() < DateTime.UtcNow);
        Assert.True(certificate.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddYears(4));
    }

    [Fact]
    public void DeviceCertificateUsesClientAuthenticationAndTheDeviceIdAsSubject()
    {
        using var certificate = CertificateFactory.CreateDeviceCertificate("abc123");

        Assert.Equal([ClientAuthenticationOid], EnhancedKeyUsages(certificate));
        Assert.Contains("abc123", certificate.Subject);
        using var key = certificate.GetECDsaPrivateKey();
        Assert.Equal(256, key!.KeySize);
    }

    [Fact]
    public void EachDeviceCertificateIsDistinct()
    {
        using var first = CertificateFactory.CreateDeviceCertificate("one");
        using var second = CertificateFactory.CreateDeviceCertificate("two");

        Assert.NotEqual(CertificateFactory.Sha256Thumbprint(first), CertificateFactory.Sha256Thumbprint(second));
    }

    [Fact]
    public void Sha256ThumbprintIsHexOfTheCertificateHashAndNotTheSha1Property()
    {
        using var certificate = CertificateFactory.CreateServerCertificate("BookDB-Test");

        string thumbprint = CertificateFactory.Sha256Thumbprint(certificate);

        Assert.Equal(64, thumbprint.Length);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(certificate.RawData)), thumbprint);
        Assert.NotEqual(certificate.Thumbprint, thumbprint);
    }

    [Fact]
    public void PfxRoundTripKeepsThePrivateKeyAndTheIdentity()
    {
        using var original = CertificateFactory.CreateDeviceCertificate("roundtrip");

        byte[] pfx = CertificateFactory.ExportPfx(original, "pw");
        using var restored = CertificateFactory.LoadPfx(pfx, "pw");

        Assert.True(restored.HasPrivateKey);
        Assert.Equal(CertificateFactory.Sha256Thumbprint(original), CertificateFactory.Sha256Thumbprint(restored));
    }

    [Fact]
    public void PublicOnlyCopyDropsThePrivateKeyButKeepsTheThumbprint()
    {
        using var original = CertificateFactory.CreateDeviceCertificate("public-only");

        using var stripped = CertificateFactory.PublicOnly(original);

        Assert.False(stripped.HasPrivateKey);
        Assert.Equal(CertificateFactory.Sha256Thumbprint(original), CertificateFactory.Sha256Thumbprint(stripped));
    }

    [Fact]
    public void DeviceCertificatePfxStaysSmallEnoughForAQrCode()
    {
        using var certificate = CertificateFactory.CreateDeviceCertificate("qr-size");

        // The pairing payload has to survive as a scannable QR; an RSA key would blow this budget outright.
        Assert.True(CertificateFactory.ExportPfx(certificate).Length < 1500);
    }
}
