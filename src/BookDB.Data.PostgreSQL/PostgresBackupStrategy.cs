using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;

namespace BookDB.Data.PostgreSQL;

/// <summary>
/// A remote PostgreSQL backend has no client-side file-format backup (there is no local database file to copy,
/// and shelling out to <c>pg_dump</c> is out of scope). It reports <see cref="SupportsFileBackup"/> = false so
/// the calling service falls back to the engine-neutral CSV archive; the file-backup method itself is never a
/// valid call and throws to make a mis-route fail loudly rather than silently produce nothing.
/// </summary>
public sealed class PostgresBackupStrategy : IBackupStrategy
{
    public bool SupportsFileBackup => false;

    public Task<string> BackupAsync(
        string destFolder, CancellationToken ct, string? explicitFileName = null, IProgress<string>? progress = null) =>
        throw new NotSupportedException(
            "A remote PostgreSQL backend has no file-format backup; use the CSV archive (SupportsFileBackup is false).");
}
