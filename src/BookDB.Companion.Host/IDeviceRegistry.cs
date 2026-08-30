using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace BookDB.Companion.Host;

public interface IDeviceRegistry
{
    int Count { get; }
    bool IsAtCapacity { get; }

    IReadOnlyList<PairedDevice> List();
    PairedDevice? Find(string thumbprint);
    bool IsRegistered(string thumbprint);

    /// <summary>Registers a device unless the cap is already reached, in which case it returns false.</summary>
    bool TryRegister(PairedDevice device);

    /// <summary>Removes a device, freeing its slot; its next call is refused because the thumbprint is gone.</summary>
    bool Revoke(string thumbprint);

    void TouchLastUsed(string thumbprint);

    /// <summary>
    /// The install's server certificate, minted and persisted on first use. The caller owns the returned
    /// instance and disposes it.
    /// </summary>
    X509Certificate2 GetOrCreateServerCertificate();
}
