using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Models;
using Serilog;

namespace BookDB.Logic.Services;

public sealed class DatabaseMaintenanceService : IDatabaseMaintenanceService
{
    private readonly IMaintenanceProvider _provider;
    private readonly IBackupService _backupService;

    public DatabaseMaintenanceService(IMaintenanceProvider provider, IBackupService backupService)
    {
        _provider = provider;
        _backupService = backupService;
    }

    public Task<MaintenanceCheckResult> CheckIntegrityAsync(
        CancellationToken ct = default, IProgress<MaintenanceStep>? progress = null)
        => _provider.CheckIntegrityAsync(progress, ct);

    public async Task<MaintenanceRepairResult> OptimizeAndRepairAsync(
        CancellationToken ct = default, IProgress<MaintenanceStep>? progress = null,
        IProgress<string>? safetyBackupReport = null)
    {
        // Safety backup first — a maintenance op must never be what loses data. This is provider-neutral, so
        // it stays here rather than in the provider. Independent of the user's auto-backup folder so it works
        // even when none is configured.
        string? safetyBackupPath = null;
        try
        {
            progress?.Report(MaintenanceStep.SafetyBackup);
            var safetyFolder = Path.Combine(Path.GetTempPath(), "bookdb-maintenance");
            Directory.CreateDirectory(safetyFolder);
            var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);
            var fileName = $"bookdb-maintenance-safety-{stamp}.zip";
            // A remote backend has no file-format backup — fall back to the engine-neutral CSV archive
            // (mirrors the auto-backup path), so optimize/repair works on PostgreSQL too.
            safetyBackupPath = _backupService.SupportsFileBackup
                ? await _backupService.BackupSqliteAsync(safetyFolder, ct, explicitFileName: fileName)
                : await _backupService.BackupCsvArchiveAsync(safetyFolder, ct, explicitFileName: fileName);
            // Report the location now (in step order, right after the "creating a safety backup" step) rather
            // than leaving the UI to print it last from the result.
            safetyBackupReport?.Report(safetyBackupPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DatabaseMaintenanceService: safety backup before optimize/repair failed");
            return new MaintenanceRepairResult(false, safetyBackupPath, 0, 0, ex.Message);
        }

        var result = await _provider.OptimizeAndRepairAsync(progress, ct);
        return result with { SafetyBackupPath = safetyBackupPath };
    }
}
