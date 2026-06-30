using System;
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

    // The credential library folds the account into the service URL when it builds the OS credential target, so
    // any '/' in the account (our keys are "user@host:port/database") becomes a URL path segment and Set and Get
    // end up computing different targets — the secret is written but can never be read back. Percent-encode the
    // account into a flat, URL-safe token, applied identically on every call. Read also falls back to the raw
    // account so credentials stored before this fix (e.g. an existing PostgreSQL password) keep working.
    private static string Encode(string account) => Uri.EscapeDataString(account);

    public string? Get(string account) =>
        (_store.Get(ServiceName, Encode(account)) ?? _store.Get(ServiceName, account))?.Password;

    public void Set(string account, string secret) => _store.AddOrUpdate(ServiceName, Encode(account), secret);

    public void Delete(string account)
    {
        _store.Remove(ServiceName, Encode(account));
        _store.Remove(ServiceName, account); // also clear any pre-fix entry stored under the raw account
    }
}
