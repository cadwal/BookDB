namespace BookDB.Data.Interfaces;

/// <summary>
/// Project-owned wrapper over the OS credential store, isolating the credential library behind a single
/// contract. Keyed by an account string (e.g. <c>user@host:port/database</c>) so multiple server
/// configurations do not collide. A secret can be written or cleared; it is never surfaced in the UI.
/// </summary>
public interface ISecretStore
{
    string? Get(string account);

    void Set(string account, string secret);

    void Delete(string account);
}
