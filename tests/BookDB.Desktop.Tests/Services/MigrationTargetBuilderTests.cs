using System;
using System.IO;
using System.Threading.Tasks;
using BookDB.Desktop.Services;
using BookDB.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BookDB.Desktop.Tests.Services;

public sealed class MigrationTargetBuilderTests
{
    // A SQLite target's backup strategy and maintenance provider depend on AppSettings, so the isolated
    // target container must register one — otherwise building the target throws while resolving IBackupStrategy
    // and "Move library → SQLite" fails before any copy.
    [Fact]
    public async Task BuildAsync_SqliteTarget_BuildsWithoutMissingAppSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bookdb_migtarget_{Guid.NewGuid():N}.db");
        try
        {
            var builder = new MigrationTargetBuilder();

            await using var target = await builder.BuildAsync(
                DatabaseBackend.Sqlite, $"Data Source={path}", TestContext.Current.CancellationToken);

            Assert.NotNull(target.Factory);
            Assert.NotNull(target.Resync);
            Assert.NotNull(target.Backup);
        }
        finally
        {
            // This database's pool only — ClearAllPools() is process-wide and disposes the native
            // handle of connections other test classes are using in parallel.
            using (var poolKey = new SqliteConnection($"Data Source={path}"))
                SqliteConnection.ClearPool(poolKey);
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }
}
