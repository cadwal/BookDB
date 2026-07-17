using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using BookDB.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace BookDB.Data.Sqlite;

/// <summary>
/// SQLite file-format backup: flush the WAL, copy the database file, and zip it (with config.json) — the
/// fastest, most faithful backup for the local engine. Remote backends report
/// <see cref="SupportsFileBackup"/> = false and the caller falls back to the engine-neutral CSV archive.
/// </summary>
/// <remarks>
/// Progress is reported as typed <see cref="BackupProgressStep"/> values; the Desktop layer maps them to
/// status strings, so this data-layer type carries no localization dependency.
/// </remarks>
public sealed class SqliteBackupStrategy : IBackupStrategy
{
    private readonly IDbContextFactory<BookDbContext> _factory;
    private readonly AppSettings _appSettings;

    public SqliteBackupStrategy(IDbContextFactory<BookDbContext> factory, AppSettings appSettings)
    {
        _factory = factory;
        _appSettings = appSettings;
    }

    public bool SupportsFileBackup => true;

    public async Task<string> BackupAsync(
        string destFolder, CancellationToken ct, string? explicitFileName = null,
        IProgress<ProgressUpdate<BackupProgressStep>>? progress = null)
    {
        destFolder = Path.GetFullPath(destFolder);
        var zipPath = Path.Combine(
            destFolder,
            explicitFileName ?? $"bookdb-{DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.zip");

        // A fresh, non-existent target: VACUUM INTO requires the destination file not to exist.
        var snapshot = Path.Combine(Path.GetTempPath(), $"bookdb_sqlitesnapshot_{Guid.NewGuid():N}.db");
        try
        {
            progress?.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.FlushingLog));
            await using var dbContext = await _factory.CreateDbContextAsync(ct);

            // VACUUM INTO writes a transactionally consistent snapshot of the live database. Unlike a
            // checkpoint-then-file-copy, it is correct against the WAL and concurrent readers without having
            // to quiesce the (pooled, still-open) EF connections, and the result is a self-contained,
            // compacted library.db. ExecuteSqlAsync binds the path as a parameter (VACUUM INTO takes a string
            // expression, so a bound parameter is valid and keeps the analyzer happy).
            progress?.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.CopyingDatabase));
            await dbContext.Database.ExecuteSqlAsync($"VACUUM INTO {snapshot}", ct);

            progress?.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.CreatingArchive));
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            archive.CreateEntryFromFile(snapshot, "library.db");
            if (ExistingConfigPath is { } configPath)
                archive.CreateEntryFromFile(configPath, "config.json");
        }
        finally
        {
            try { File.Delete(snapshot); } catch { /* best effort */ }
        }

        Log.Information("SqliteBackupStrategy: SQLite backup written to {ZipPath}", zipPath);
        return zipPath;
    }

    public async Task RestoreFileAsync(string sourceDbPath, CancellationToken ct)
    {
        var target = Path.GetFullPath(
            _appSettings.SqliteLibraryPath
            ?? throw new InvalidOperationException("A local SQLite database path is required for a file restore."));

        // Idle pooled connections keep the library file open — macOS refuses to overwrite an open
        // file — and a leftover -wal/-shm pair would replay pre-restore pages over the restored
        // database on its next open. Close every pooled handle, then drop the sidecars before copying.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await Task.Run(() =>
        {
            File.Delete(target + "-wal");
            File.Delete(target + "-shm");
            File.Copy(sourceDbPath, target, overwrite: true);
        }, ct);

        Log.Information("SqliteBackupStrategy: library file replaced from {SourcePath}", sourceDbPath);
    }

    // config.json is bundled into the backup so a restore can carry the backend and preferences;
    // guarded in case a backup runs before the file has been written.
    private string? ExistingConfigPath =>
        !string.IsNullOrEmpty(_appSettings.ConfigPath) && File.Exists(_appSettings.ConfigPath)
            ? _appSettings.ConfigPath
            : null;
}
