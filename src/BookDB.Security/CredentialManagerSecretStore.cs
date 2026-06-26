using BookDB.Data.Interfaces;
using GitCredentialManager;

namespace BookDB.Security;

/// <summary>
/// The single class that touches the credential library (Git Credential Manager via Devlooped). Everything
/// else depends only on <see cref="ISecretStore"/>, so the library can be swapped without touching consumers.
/// All secrets are stored under one service name, keyed by the caller-supplied account.
/// </summary>
public sealed class CredentialManagerSecretStore : ISecretStore
{
    // Groups every BookDB secret under one service. Git Credential Manager's Windows backend builds the
    // credential target with new Uri(service), so this must be an absolute URL — a bare word throws
    // UriFormatException on AddOrUpdate. The per-server password is distinguished by the account key.
    private const string ServiceName = "https://bookdb.local";

    private readonly ICredentialStore _store;

    public CredentialManagerSecretStore(ICredentialStore store)
    {
        _store = store;
    }

    public string? Get(string account) => _store.Get(ServiceName, account)?.Password;

    public void Set(string account, string secret) => _store.AddOrUpdate(ServiceName, account, secret);

    public void Delete(string account) => _store.Remove(ServiceName, account);
}
