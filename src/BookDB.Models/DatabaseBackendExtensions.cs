namespace BookDB.Models;

public static class DatabaseBackendExtensions
{
    /// <summary>
    /// True for backends served by a separate database server over the network — the ones that need a
    /// startup connectivity check, a heartbeat, and connection-loss handling. SQLite (a local file) is not.
    /// </summary>
    public static bool IsRemote(this DatabaseBackend backend) => backend switch
    {
        DatabaseBackend.PostgreSql or DatabaseBackend.MySql => true,
        _ => false,
    };
}
