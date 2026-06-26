namespace BookDB.Models;

/// <summary>The database backend the app runs against. SQLite is the zero-config default.</summary>
public enum DatabaseBackend
{
    Sqlite,
    PostgreSql,
}

public sealed class AppSettings
{
    public DatabaseBackend Backend { get; set; } = DatabaseBackend.Sqlite;

    /// <summary>
    /// Path to the local SQLite database file. Null when a non-SQLite backend is active, where
    /// file-based operations (file backup, on-disk size, file copy) do not apply.
    /// </summary>
    public string? SqliteLibraryPath { get; set; }

    /// <summary>Path to the local bootstrap config (<c>config.json</c>), bundled into every backup archive.</summary>
    public string? ConfigPath { get; set; }

    public string ConnectionString { get; set; } = string.Empty;
}
