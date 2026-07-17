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

    /// <summary>
    /// Replaces the live database file with <paramref name="sourceDbPath"/>, releasing any handles the
    /// provider itself still holds on it first. Only valid when <see cref="SupportsFileBackup"/> is
    /// <c>true</c>; remote backends throw.
    /// </summary>
    Task RestoreFileAsync(string sourceDbPath, CancellationToken ct);
}
