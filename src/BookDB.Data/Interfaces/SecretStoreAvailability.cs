namespace BookDB.Data.Interfaces;

/// <summary>
/// Whether an OS credential store is usable on this machine, decided once by a startup probe. When it is not
/// available the app still runs on SQLite, but the PostgreSQL option is disabled (no plaintext fallback).
/// </summary>
public sealed record SecretStoreAvailability(bool IsAvailable, string? UnavailableReason)
{
    public static readonly SecretStoreAvailability Available = new(true, null);

    public static SecretStoreAvailability Unavailable(string reason) => new(false, reason);
}
