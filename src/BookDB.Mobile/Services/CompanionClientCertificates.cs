using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BookDB.Mobile.Services;

/// <summary>The client half of the certificate handling: load the PKCS#12 the pairing code carried, and
/// compute the SHA-256 thumbprint used to pin the server. Kept in step with the desktop's CertificateFactory
/// (SHA-256 of the raw certificate, never the SHA-1 <see cref="X509Certificate2.Thumbprint"/>).</summary>
public static class CompanionClientCertificates
{
    public static X509Certificate2 LoadPfx(byte[] pfx) =>
        X509CertificateLoader.LoadPkcs12(pfx, password: null, X509KeyStorageFlags.Exportable);

    public static string Sha256Thumbprint(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData));
}
