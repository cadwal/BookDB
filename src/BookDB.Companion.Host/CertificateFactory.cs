using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BookDB.Companion.Host;

/// <summary>
/// Mints the self-signed certificates the companion link is built on: one long-lived server certificate
/// per desktop install, and one client certificate per paired device. ECDSA P-256 throughout — an RSA
/// key would push the pairing payload past what a scannable QR code can hold.
/// </summary>
public static class CertificateFactory
{
    private const int LifetimeYears = 5;
    private const int MaxDnsLabelLength = 63;
    private const string FallbackDnsName = "bookdb-host";

    private static readonly Oid ServerAuthentication = new("1.3.6.1.5.5.7.3.1");
    private static readonly Oid ClientAuthentication = new("1.3.6.1.5.5.7.3.2");

    /// <summary>
    /// SHA-256 thumbprint as uppercase hex. <see cref="X509Certificate2.Thumbprint"/> is SHA-1 and must
    /// never be used for pinning — every thumbprint that crosses the wire or lands in the registry comes
    /// from here.
    /// </summary>
    public static string Sha256Thumbprint(X509Certificate2 certificate)
        => Convert.ToHexString(SHA256.HashData(certificate.RawData));

    public static X509Certificate2 CreateServerCertificate(string subjectName)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={subjectName}", key, HashAlgorithmName.SHA256);

        AddCommonExtensions(request, ServerAuthentication);

        // The phone pins the thumbprint rather than validating the name, but a SAN is still required for
        // the TLS stacks on both ends to complete an HTTP/2 handshake at all.
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(DnsNameFor(subjectName));
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());

        return SelfSign(request);
    }

    public static X509Certificate2 CreateDeviceCertificate(string deviceId)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN=BookDB-Device-{deviceId}", key, HashAlgorithmName.SHA256);

        AddCommonExtensions(request, ClientAuthentication);

        return SelfSign(request);
    }

    public static byte[] ExportPfx(X509Certificate2 certificate, string? password = null)
        => certificate.Export(X509ContentType.Pfx, password);

    public static X509Certificate2 LoadPfx(byte[] pfx, string? password = null)
        => X509CertificateLoader.LoadPkcs12(pfx, password, X509KeyStorageFlags.Exportable);

    /// <summary>Strips the private key, for anything that only needs to recognise the certificate.</summary>
    public static X509Certificate2 PublicOnly(X509Certificate2 certificate)
        => X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));

    /// <summary>
    /// A DNS name the SAN builder will accept, whatever the machine happens to be called. The subject is
    /// built from <see cref="Environment.MachineName"/>: a GitHub macOS runner's is 61 characters, which
    /// puts the label past the 63-character limit, and a name that comes back empty leaves a bare
    /// "BookDB-", which a label may not end in. Both are rejected as invalid IDN names, and the throw took
    /// the whole host down at startup — the length rule is the one that actually bit.
    /// The name is decorative: the phone pins the thumbprint instead of validating it.
    /// </summary>
    private static string DnsNameFor(string subjectName)
    {
        var labels = subjectName
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(label => (label.Length > MaxDnsLabelLength ? label[..MaxDnsLabelLength] : label).Trim('-'))
            .Where(label => label.Length > 0);

        var name = string.Join('.', labels);
        return name.Length > 0 ? name : FallbackDnsName;
    }

    private static void AddCommonExtensions(CertificateRequest request, Oid enhancedKeyUsage)
    {
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([enhancedKeyUsage], false));
    }

    private static X509Certificate2 SelfSign(CertificateRequest request)
    {
        // Backdated a day so a phone whose clock runs slightly behind the desktop still sees a valid cert.
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(LifetimeYears));

        // Round-tripping through PKCS#12 is what makes the private key exportable and persistable on every
        // platform; the certificate straight out of CreateSelfSigned is not.
        return LoadPfx(certificate.Export(X509ContentType.Pfx));
    }
}
