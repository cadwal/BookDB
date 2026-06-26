using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Data.Sqlite;
using BookDB.Models;
using BookDB.Models.Entities;
using DbUp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Logic.Tests.Services;

public sealed class SqliteBackupStrategyTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TestBookDbContextFactory _factory;
    private readonly AppSettings _appSettings;
    private readonly string _tempWorkDir;

    public SqliteBackupStrategyTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_strategy_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath}";

        var upgrader = SqliteExtensions.SqliteDatabase(DeployChanges.To, connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(SqliteDbUpRunner))!,
                name => name.Contains(".Migrations."))
            .LogToNowhere()
            .Build();
        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException($"DbUp migration failed: {result.Error}");

        var options = new DbContextOptionsBuilder<BookDbContext>()
            .UseSqlite(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;
        _factory = new TestBookDbContextFactory(options);
        _appSettings = new AppSettings { SqliteLibraryPath = _dbPath };

        _tempWorkDir = Path.Combine(Path.GetTempPath(), $"bookdb_strategy_workdir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempWorkDir);
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
        try { Directory.Delete(_tempWorkDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void SupportsFileBackup_IsTrue()
        => Assert.True(new SqliteBackupStrategy(_factory, _appSettings).SupportsFileBackup);

    [Fact]
    public async Task BackupAsync_WritesZipContainingLibraryDb()
    {
        var ct = TestContext.Current.CancellationToken;
        var strategy = new SqliteBackupStrategy(_factory, _appSettings);

        var zipPath = await strategy.BackupAsync(_tempWorkDir, ct, explicitFileName: "backup.zip");

        Assert.True(File.Exists(zipPath));
        using var archive = ZipFile.OpenRead(zipPath);
        Assert.Contains(archive.Entries, e => e.FullName == "library.db");
    }

    [Fact]
    public async Task BackupAsync_SnapshotContainsCommittedData()
    {
        var ct = TestContext.Current.CancellationToken;

        // Commit a row through one context, then back up while the EF connection pool is still open —
        // the scenario the old checkpoint-then-copy could miss. VACUUM INTO must capture it.
        await using (var seed = await _factory.CreateDbContextAsync(ct))
        {
            seed.Publishers.Add(new Publisher { Name = "Backup Snapshot Publisher" });
            await seed.SaveChangesAsync(ct);
        }

        var strategy = new SqliteBackupStrategy(_factory, _appSettings);
        var zipPath = await strategy.BackupAsync(_tempWorkDir, ct, explicitFileName: "data.zip");

        var extractDir = Path.Combine(_tempWorkDir, "extract");
        ZipFile.ExtractToDirectory(zipPath, extractDir);

        await using var connection = new SqliteConnection($"Data Source={Path.Combine(extractDir, "library.db")}");
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM Publisher WHERE Name = 'Backup Snapshot Publisher'";
        var count = Convert.ToInt64(await command.ExecuteScalarAsync(ct));

        Assert.Equal(1, count);
    }
}
