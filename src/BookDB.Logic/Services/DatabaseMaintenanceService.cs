using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace BookDB.Logic.Services;

public sealed class DatabaseMaintenanceService : IDatabaseMaintenanceService
{
    private readonly IDbContextFactory<BookDbContext> _factory;
    private readonly AppSettings _appSettings;
    private readonly IBackupService _backupService;

    public DatabaseMaintenanceService(
        IDbContextFactory<BookDbContext> factory,
        AppSettings appSettings,
        IBackupService backupService)
    {
        _factory = factory;
        _appSettings = appSettings;
        _backupService = backupService;
    }

    public async Task<MaintenanceCheckResult> CheckIntegrityAsync(
        CancellationToken ct = default, IProgress<MaintenanceStep>? progress = null)
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

        // integrity_check returns a single "ok" row when the database is sound.
        var integrityOk = integrity.Count == 1
            && string.Equals(integrity[0], "ok", StringComparison.OrdinalIgnoreCase);

        var status = !integrityOk
            ? MaintenanceCheckStatus.IntegrityFailed
            : foreignKeys.Count > 0
                ? MaintenanceCheckStatus.ForeignKeyViolations
                : MaintenanceCheckStatus.Ok;

        Log.Information(
            "DatabaseMaintenanceService: integrity check status {Status} ({IntegrityRows} integrity rows, {FkRows} FK violations)",
            status, integrity.Count, foreignKeys.Count);

        return new MaintenanceCheckResult(status, integrity, foreignKeys);
    }

    public async Task<MaintenanceRepairResult> OptimizeAndRepairAsync(
        CancellationToken ct = default, IProgress<MaintenanceStep>? progress = null)
    {
        string? safetyBackupPath = null;
        var dbPath = Path.GetFullPath(_appSettings.ActiveLibraryPath);
        var sizeBefore = FileLength(dbPath);

        try
        {
            // Safety backup first — a maintenance op must never be what loses data. Independent of the user's
            // auto-backup folder so it works even when none is configured.
            progress?.Report(MaintenanceStep.SafetyBackup);
            var safetyFolder = Path.Combine(Path.GetTempPath(), "bookdb-maintenance");
            Directory.CreateDirectory(safetyFolder);
            var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);
            safetyBackupPath = await _backupService.BackupSqliteAsync(
                safetyFolder, ct, explicitFileName: $"bookdb-maintenance-safety-{stamp}.zip");

            await using var dbContext = await _factory.CreateDbContextAsync(ct);
            await dbContext.Database.OpenConnectionAsync(ct);
            var connection = dbContext.Database.GetDbConnection();

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
                "DatabaseMaintenanceService: optimize/repair complete — {Before} -> {After} bytes (safety backup {Path})",
                sizeBefore, sizeAfter, safetyBackupPath);

            return new MaintenanceRepairResult(true, safetyBackupPath, sizeBefore, sizeAfter, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DatabaseMaintenanceService: OptimizeAndRepairAsync failed");
            return new MaintenanceRepairResult(
                false, safetyBackupPath, sizeBefore, FileLength(dbPath), ex.Message);
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
