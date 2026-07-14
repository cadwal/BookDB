using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Models;

namespace BookDB.Data.Interfaces;

/// <summary>
/// Provider-specific full backup. SQLite performs a file-format backup; remote backends report
/// <see cref="SupportsFileBackup"/> = <c>false</c> and the caller falls back to the engine-neutral
/// CSV archive.
/// </summary>
public interface IBackupStrategy
{
    bool SupportsFileBackup { get; }

    Task<string> BackupAsync(
        string destFolder,
        CancellationToken ct,
        string? explicitFileName = null,
        IProgress<ProgressUpdate<BackupProgressStep>>? progress = null);
}
