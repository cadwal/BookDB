using System;
using System.Security.Cryptography.X509Certificates;

namespace BookDB.Companion.Host;

/// <summary>
/// A pairing offer that has been minted but not yet claimed. It lives in memory only: the device
/// certificate carries its private key, and an unclaimed offer is worthless a couple of minutes later, so
/// there is nothing here worth writing to disk.
/// </summary>
public sealed class PendingPairing
{
    internal PendingPairing(X509Certificate2 deviceCertificate, DateTimeOffset issuedAtUtc, TimeSpan ttl)
    {
        DeviceCertificate = deviceCertificate;
        Thumbprint = CertificateFactory.Sha256Thumbprint(deviceCertificate);
        IssuedAtUtc = issuedAtUtc;
        ExpiresAtUtc = issuedAtUtc + ttl;
    }

    public X509Certificate2 DeviceCertificate { get; }
    public string Thumbprint { get; }
    public DateTimeOffset IssuedAtUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public bool IsClaimed { get; private set; }

    internal bool HasExpired(DateTimeOffset now) => now >= ExpiresAtUtc;

    internal void MarkClaimed() => IsClaimed = true;
}
