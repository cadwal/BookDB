using System;
using BookDB.Data.Interfaces;
using GitCredentialManager;

namespace BookDB.Security;

/// <summary>
/// Decides, once at startup, whether a usable OS credential store exists. It creates the credential store and
/// probes it with a benign read; any failure (e.g. no Secret Service on a headless Linux box) yields a
/// <see cref="NullSecretStore"/> and an unavailable result rather than letting the app crash later.
/// </summary>
public static class SecretStoreFactory
{
    private const string Namespace = "bookdb";
    private const string ProbeAccount = "__availability_probe__";

    public static (ISecretStore Store, SecretStoreAvailability Availability) Create() =>
        Create(() => CredentialManager.Create(Namespace));

    // Seam for testing: the factory delegate is replaced so probe-success and probe-failure can be exercised
    // without a real OS credential store.
    internal static (ISecretStore Store, SecretStoreAvailability Availability) Create(
        Func<ICredentialStore> storeFactory)
    {
        try
        {
            var store = storeFactory();
            // A read confirms the backing store actually responds on this OS before we trust it for writes.
            _ = store.Get(Namespace, ProbeAccount);
            return (new CredentialManagerSecretStore(store), SecretStoreAvailability.Available);
        }
        catch (Exception ex)
        {
            return (new NullSecretStore(), SecretStoreAvailability.Unavailable(ex.Message));
        }
    }
}
