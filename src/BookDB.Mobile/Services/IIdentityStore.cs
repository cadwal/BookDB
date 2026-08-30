namespace BookDB.Mobile.Services;

/// <summary>Persists the single paired identity in app-private storage. The private key lives in the sandbox
/// only; that is accepted for the home-LAN threat model (see the mobile design doc).</summary>
public interface IIdentityStore
{
    bool HasIdentity { get; }

    DeviceIdentity? Load();

    void Save(DeviceIdentity identity);

    void Clear();
}
