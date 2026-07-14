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
    private const string StoreVariable = "GCM_CREDENTIAL_STORE";
    private const string LinuxDefaultStore = "secretservice";

    public static (ISecretStore Store, SecretStoreAvailability Availability) Create() =>
        CreateWithPlatformDefault(
            () => Create(() => CredentialManager.Create(Namespace)),
            OperatingSystem.IsLinux(),
            Environment.GetEnvironmentVariable,
            Environment.SetEnvironmentVariable);

    // GCM ships no default credential store on Linux (its CLI may run headless), and resolves the store
    // lazily — the "no store selected" failure surfaces at the probe read, not at Create — so the retry has
    // to wrap the whole attempt. BookDB is a GUI app, so the freedesktop Secret Service (GNOME Keyring,
    // KWallet) is the right default. An explicit GCM_CREDENTIAL_STORE always wins (set-but-broken included);
    // a git-config credential.credentialStore selection wins when it works and is only overridden when it
    // failed anyway, where a working default beats reporting the store unavailable.
    internal static (ISecretStore Store, SecretStoreAvailability Availability) CreateWithPlatformDefault(
        Func<(ISecretStore Store, SecretStoreAvailability Availability)> create,
        bool isLinux, Func<string, string?> getEnv, Action<string, string?> setEnv)
    {
        var result = create();
        if (result.Availability.IsAvailable || !isLinux || !string.IsNullOrEmpty(getEnv(StoreVariable)))
            return result;

        setEnv(StoreVariable, LinuxDefaultStore);
        var retry = create();
        if (retry.Availability.IsAvailable)
            return retry;

        // The default didn't help — undo it and report the original failure, not the retry's.
        setEnv(StoreVariable, null);
        return result;
    }

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
