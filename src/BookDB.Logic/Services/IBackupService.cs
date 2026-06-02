using System;
using System.Threading;
using System.Threading.Tasks;

namespace BookDB.Logic.Services;

public interface IBackupService
{
    // Returns the path of the zip file written.
    // explicitFileName: if provided, uses that name exactly (enables overwrite); if null, auto-generates and auto-suffixes on conflict.
    Task<string> BackupSqliteAsync(string destFolder, CancellationToken ct = default, string? explicitFileName = null, IProgress<string>? progress = null);
    Task<string> BackupCsvArchiveAsync(string destFolder, CancellationToken ct = default, string? explicitFileName = null, IProgress<string>? progress = null);
    Task RestoreAsync(string backupZipPath, string safetyBackupPath, CancellationToken ct = default, IProgress<string>? progress = null);
    Task AutoBackupIfEnabledAsync(CancellationToken ct = default, IProgress<string>? progress = null);

    /// <summary>
    /// True when an auto-backup would actually run on shutdown (the feature is enabled and a
    /// destination folder is configured). Lets callers show shutdown status only when needed.
    /// </summary>
    Task<bool> IsAutoBackupEnabledAsync(CancellationToken ct = default);

    string GetCandidateSqlitePath(string destFolder);
    string GetCandidateCsvArchivePath(string destFolder);
}
