using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Models;

namespace BookDB.Data.MySql;

/// <summary>
/// A remote MySQL/MariaDB backend has no client-side file-format backup (there is no local database file to copy,
/// and shelling out to <c>mysqldump</c> is out of scope). It reports <see cref="SupportsFileBackup"/> = false so
/// the calling service falls back to the engine-neutral CSV archive; the file-backup method itself is never a
/// valid call and throws to make a mis-route fail loudly rather than silently produce nothing.
/// </summary>
public sealed class MySqlBackupStrategy : IBackupStrategy
{
    public bool SupportsFileBackup => false;

    public Task<string> BackupAsync(
        string destFolder, CancellationToken ct, string? explicitFileName = null,
        IProgress<ProgressUpdate<BackupProgressStep>>? progress = null) =>
        throw new NotSupportedException(
            "A remote MySQL/MariaDB backend has no file-format backup; use the CSV archive (SupportsFileBackup is false).");

    public Task RestoreFileAsync(string sourceDbPath, CancellationToken ct) =>
        throw new NotSupportedException(
            "A remote MySQL/MariaDB backend has no file-format restore; use the CSV archive (SupportsFileBackup is false).");
}
