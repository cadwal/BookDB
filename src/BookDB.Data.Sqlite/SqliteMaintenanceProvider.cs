using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using BookDB.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace BookDB.Data.Sqlite;

/// <summary>
/// SQLite maintenance: <c>PRAGMA integrity_check</c> / <c>foreign_key_check</c> for the read-only check, and
/// REINDEX + VACUUM + <c>wal_checkpoint(TRUNCATE)</c> for the safe optimize/repair pass. The safety backup that
/// precedes a repair is taken by <c>DatabaseMaintenanceService</c> (provider-neutral), so this never takes one.
/// </summary>
public sealed class SqliteMaintenanceProvider : IMaintenanceProvider
{
    // Application tables only: exclude SQLite's internal sqlite_% tables and the FTS5 index fts_books plus its
    // shadow tables (all named fts_books*), so the reported list is the tables the user recognises. integrity_check
    // and VACUUM are whole-database, so this is "the tables covered", not a per-table operation list.
    private const string UserTablesSql =
        "SELECT name FROM sqlite_master WHERE type='table' " +
        "AND name NOT LIKE 'sqlite_%' AND name NOT LIKE 'fts\\_books%' ESCAPE '\\' ORDER BY name";

    private readonly IDbContextFactory<BookDbContext> _factory;
    private readonly AppSettings _appSettings;

    public SqliteMaintenanceProvider(IDbContextFactory<BookDbContext> factory, AppSettings appSettings)
    {
        _factory = factory;
        _appSettings = appSettings;
    }

    public async Task<MaintenanceCheckResult> CheckIntegrityAsync(
        IProgress<MaintenanceStep>? progress, CancellationToken ct)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        await dbContext.Database.OpenConnectionAsync(ct);
        var connection = dbContext.Database.GetDbConnection();

        progress?.Report(MaintenanceStep.CheckingIntegrity);
        var integrity = await ReadRowsAsync(
            connection, "PRAGMA integrity_check;", r => r.GetString(0), ct);

        progress?.Report(MaintenanceStep.CheckingForeignKeys);
        var foreignKeys = await ReadRowsAsync(
            connection, "PRAGMA foreign_key_check;", FormatForeignKeyRow, ct);

        var tables = await ReadRowsAsync(connection, UserTablesSql, r => r.GetString(0), ct);

        // integrity_check returns a single "ok" row when the database is sound.
        var integrityOk = integrity.Count == 1
            && string.Equals(integrity[0], "ok", StringComparison.OrdinalIgnoreCase);

        var status = !integrityOk
            ? MaintenanceCheckStatus.IntegrityFailed
            : foreignKeys.Count > 0
                ? MaintenanceCheckStatus.ForeignKeyViolations
                : MaintenanceCheckStatus.Ok;

        Log.Information(
            "SqliteMaintenanceProvider: integrity check status {Status} ({IntegrityRows} integrity rows, {FkRows} FK violations)",
            status, integrity.Count, foreignKeys.Count);

        return new MaintenanceCheckResult(status, integrity, foreignKeys) { TablesChecked = tables };
    }

    public async Task<MaintenanceRepairResult> OptimizeAndRepairAsync(
        IProgress<MaintenanceStep>? progress, CancellationToken ct)
    {
        var dbPath = Path.GetFullPath(
            _appSettings.SqliteLibraryPath
            ?? throw new InvalidOperationException(
                "A local SQLite database path is required for maintenance operations."));
        var sizeBefore = FileLength(dbPath);

        try
        {
            await using var dbContext = await _factory.CreateDbContextAsync(ct);
            await dbContext.Database.OpenConnectionAsync(ct);
            var connection = dbContext.Database.GetDbConnection();

            // REINDEX + VACUUM rebuild the whole file; list the tables covered so the UI can report them.
            var tables = await ReadRowsAsync(connection, UserTablesSql, r => r.GetString(0), ct);

            progress?.Report(MaintenanceStep.Reindex);
            await ExecuteAsync(connection, "REINDEX;", ct);

            // VACUUM rebuilds and compacts the file; it cannot run inside a transaction.
            progress?.Report(MaintenanceStep.Vacuum);
            await ExecuteAsync(connection, "VACUUM;", ct);

            // Flush and truncate the write-ahead log left behind by the rebuild.
            progress?.Report(MaintenanceStep.Checkpoint);
            await ExecuteAsync(connection, "PRAGMA wal_checkpoint(TRUNCATE);", ct);

            var sizeAfter = FileLength(dbPath);
            Log.Information(
                "SqliteMaintenanceProvider: optimize/repair complete — {Before} -> {After} bytes ({Tables} tables)",
                sizeBefore, sizeAfter, tables.Count);

            return new MaintenanceRepairResult(true, null, sizeBefore, sizeAfter, null) { TablesOptimized = tables };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SqliteMaintenanceProvider: OptimizeAndRepairAsync failed");
            return new MaintenanceRepairResult(false, null, sizeBefore, FileLength(dbPath), ex.Message);
        }
    }

    private static async Task<List<string>> ReadRowsAsync(
        DbConnection connection, string sql, Func<DbDataReader, string> map, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var rows = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(map(reader));
        return rows;
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // PRAGMA foreign_key_check columns: child table, rowid, parent table, foreign-key index.
    private static string FormatForeignKeyRow(DbDataReader r)
    {
        var table = r.IsDBNull(0) ? "?" : r.GetString(0);
        var rowId = r.IsDBNull(1) ? "?" : r.GetValue(1).ToString();
        var parent = r.IsDBNull(2) ? "?" : r.GetString(2);
        return $"{table} (rowid {rowId}) -> {parent}";
    }

    private static long FileLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }
}
