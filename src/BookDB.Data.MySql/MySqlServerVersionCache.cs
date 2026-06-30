using Microsoft.EntityFrameworkCore;

namespace BookDB.Data.MySql;

/// <summary>
/// Carries the server version the startup connectivity probe detected to the DbContext options builder, so the
/// active MySQL/MariaDB backend's version (family + number) is detected once at startup rather than re-probed —
/// and never guessed by family — when EF first builds its options. The options builder reads this exactly once,
/// during startup DbUp, which runs after the connectivity gate and before any Settings "test connection" probe,
/// so at read time it holds the active server's version. Later probes (Settings tests, a move-target builder's
/// own context) overwrite it harmlessly — nothing reads it again.
/// </summary>
public sealed class MySqlServerVersionCache
{
    public ServerVersion? Detected { get; private set; }

    public void Record(ServerVersion version) => Detected = version;
}
