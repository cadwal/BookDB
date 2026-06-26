using BookDB.Data.Interfaces;

namespace BookDB.Security;

/// <summary>
/// Used when no OS credential store is available on this machine. The app still runs on SQLite; callers gate
/// on <see cref="SecretStoreAvailability"/> and disable the PostgreSQL option, so writes are never reached —
/// the no-op methods exist only so a stray call cannot crash and can never write a plaintext fallback.
/// </summary>
public sealed class NullSecretStore : ISecretStore
{
    public string? Get(string account) => null;

    public void Set(string account, string secret)
    {
    }

    public void Delete(string account)
    {
    }
}
