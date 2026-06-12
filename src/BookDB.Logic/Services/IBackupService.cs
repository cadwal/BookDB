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
    /// True when auto-backup is configured (the feature is enabled and a destination folder is set),
    /// regardless of whether a backup is actually due right now.
    /// </summary>
    Task<bool> IsAutoBackupEnabledAsync(CancellationToken ct = default);

    /// <summary>
    /// True when an auto-backup should actually run now: configured AND (library data changed this session,
    /// OR no backup has ever run, OR more than the recency threshold has passed since the last one).
    /// Lets the shutdown path show its progress window only when a backup will really happen.
    /// </summary>
    Task<bool> ShouldAutoBackupAsync(CancellationToken ct = default);

    string GetCandidateSqlitePath(string destFolder);
    string GetCandidateCsvArchivePath(string destFolder);
}
