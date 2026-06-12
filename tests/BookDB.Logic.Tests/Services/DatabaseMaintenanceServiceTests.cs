using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Logic.Services;
using BookDB.Models;
using DbUp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Logic.Tests.Services;

public sealed class DatabaseMaintenanceServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TestBookDbContextFactory _factory;
    private readonly StubBackupService _backup = new();
    private readonly DatabaseMaintenanceService _sut;

    public DatabaseMaintenanceServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_maint_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath}";

        var upgrader = SqliteExtensions.SqliteDatabase(DeployChanges.To, connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(BookDbContext))!,
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
        var appSettings = new AppSettings { ActiveLibraryPath = _dbPath };
        _sut = new DatabaseMaintenanceService(_factory, appSettings, _backup);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
        if (_backup.LastBackupPath != null)
            try { File.Delete(_backup.LastBackupPath); } catch { /* best effort */ }
    }

    [Fact]
    public async Task CheckIntegrityAsync_HealthyDatabase_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var progress = new RecordingProgress<MaintenanceStep>();

        var result = await _sut.CheckIntegrityAsync(ct, progress);

        Assert.Equal(MaintenanceCheckStatus.Ok, result.Status);
        Assert.Empty(result.ForeignKeyViolations);
        Assert.Equal(new[] { "ok" }, result.IntegrityMessages);
        Assert.Equal(
            new[] { MaintenanceStep.CheckingIntegrity, MaintenanceStep.CheckingForeignKeys },
            progress.Reports);
    }

    [Fact]
    public async Task OptimizeAndRepairAsync_HealthyDatabase_SucceedsAndWritesSafetyBackup()
    {
        var ct = TestContext.Current.CancellationToken;
        var progress = new RecordingProgress<MaintenanceStep>();

        var result = await _sut.OptimizeAndRepairAsync(ct, progress);

        Assert.True(result.Success);
        Assert.False(string.IsNullOrEmpty(result.SafetyBackupPath));
        Assert.True(File.Exists(result.SafetyBackupPath));
        Assert.Equal(
            new[]
            {
                MaintenanceStep.SafetyBackup,
                MaintenanceStep.Reindex,
                MaintenanceStep.Vacuum,
                MaintenanceStep.Checkpoint,
            },
            progress.Reports);
    }

    [Fact]
    public async Task OptimizeAndRepairAsync_LeavesDatabaseUsable()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.OptimizeAndRepairAsync(ct);

        // The database is still healthy and queryable after a repair pass.
        var after = await _sut.CheckIntegrityAsync(ct);
        Assert.Equal(MaintenanceCheckStatus.Ok, after.Status);

        await using var dbContext = _factory.CreateDbContext();
        var bookCount = await dbContext.Books.CountAsync(ct);
        Assert.Equal(0, bookCount);
    }

    // Synchronous IProgress so reported steps are captured deterministically (no SynchronizationContext hops).
    private sealed class RecordingProgress<T> : IProgress<T>
    {
        public List<T> Reports { get; } = [];
        public void Report(T value) => Reports.Add(value);
    }

    // Records the safety-backup call and writes a real file so the test can assert it exists.
    private sealed class StubBackupService : IBackupService
    {
        public string? LastBackupPath { get; private set; }

        public Task<string> BackupSqliteAsync(
            string destFolder, CancellationToken ct = default,
            string? explicitFileName = null, IProgress<string>? progress = null)
        {
            Directory.CreateDirectory(destFolder);
            var path = Path.Combine(destFolder, explicitFileName ?? "backup.zip");
            File.WriteAllText(path, "stub safety backup");
            LastBackupPath = path;
            return Task.FromResult(path);
        }

        public Task<string> BackupCsvArchiveAsync(
            string destFolder, CancellationToken ct = default,
            string? explicitFileName = null, IProgress<string>? progress = null)
            => throw new NotSupportedException();

        public Task RestoreAsync(
            string backupZipPath, string safetyBackupPath,
            CancellationToken ct = default, IProgress<string>? progress = null)
            => throw new NotSupportedException();

        public Task AutoBackupIfEnabledAsync(CancellationToken ct = default, IProgress<string>? progress = null)
            => Task.CompletedTask;

        public Task<bool> IsAutoBackupEnabledAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> ShouldAutoBackupAsync(CancellationToken ct = default) => Task.FromResult(false);
        public string GetCandidateSqlitePath(string destFolder) => string.Empty;
        public string GetCandidateCsvArchivePath(string destFolder) => string.Empty;
    }
}
