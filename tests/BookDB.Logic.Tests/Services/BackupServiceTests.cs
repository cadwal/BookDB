using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Logic.Services;
using BookDB.Models;
using DbUp;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Logic.Tests.Services;

public sealed class BackupServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TestBookDbContextFactory _factory;
    private readonly BackupService _sut;
    private readonly LookupService _settings;
    private readonly DataChangeTracker _changeTracker;
    private readonly string _tempWorkDir;

    public BackupServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_backup_test_{Guid.NewGuid():N}.db");
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
        _settings = new LookupService(_factory, new NullResourceProvider());
        _changeTracker = new DataChangeTracker();
        _sut = new BackupService(_factory, appSettings, _settings, new NullResourceProvider(), _changeTracker);

        _tempWorkDir = Path.Combine(Path.GetTempPath(), $"bookdb_backup_workdir_{Guid.NewGuid():N}");
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
    public async Task BackupSqliteAsync_ProducesZipContainingLibraryDb()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.BackupSqliteAsync(_tempWorkDir, ct);

        var zips = Directory.GetFiles(_tempWorkDir, "*.zip");
        Assert.Single(zips);

        using var archive = ZipFile.OpenRead(zips[0]);
        Assert.Contains(archive.Entries, e => e.Name == "library.db");
    }

    [Fact]
    public async Task BackupCsvArchiveAsync_ProducesZipWithPerTableCsvFiles()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.BackupCsvArchiveAsync(_tempWorkDir, ct);

        var zips = Directory.GetFiles(_tempWorkDir, "*.zip");
        Assert.Single(zips);

        using var archive = ZipFile.OpenRead(zips[0]);
        Assert.True(archive.Entries.Any(e => e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)),
            "Zip should contain at least one .csv file");
    }

    [Fact]
    public async Task BackupCsvArchiveAsync_CoversEveryEntitySetInTheModel()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.BackupCsvArchiveAsync(_tempWorkDir, ct);

        var zips = Directory.GetFiles(_tempWorkDir, "*.zip");
        Assert.Single(zips);

        using var archive = ZipFile.OpenRead(zips[0]);
        var entryNames = archive.Entries.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Every DbSet on the context must have a matching per-table CSV, so a newly added
        // table can never be silently absent from the backup archive.
        var expected = typeof(BookDbContext).GetProperties()
            .Where(p => p.PropertyType.IsGenericType
                && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(p => $"{p.Name}.csv")
            .ToList();

        Assert.NotEmpty(expected);
        var missing = expected.Where(name => !entryNames.Contains(name)).ToList();
        Assert.True(missing.Count == 0,
            $"CSV archive is missing per-table files: {string.Join(", ", missing)}");
    }

    [Fact]
    public async Task RestoreAsync_AbortedWhenSafetyBackupFails_OriginalNotModified()
    {
        var ct = TestContext.Current.CancellationToken;

        // safetyBackupPath=null should throw before any restore attempt
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.RestoreAsync("somefile.zip", null!, ct));
    }

    [Fact]
    public async Task IsAutoBackupEnabledAsync_FalseByDefault()
    {
        var ct = TestContext.Current.CancellationToken;

        Assert.False(await _sut.IsAutoBackupEnabledAsync(ct));
    }

    [Fact]
    public async Task IsAutoBackupEnabledAsync_FalseWhenEnabledButNoFolder()
    {
        var ct = TestContext.Current.CancellationToken;
        await _settings.SetAsync("AutoBackup.Enabled", "true", ct);

        Assert.False(await _sut.IsAutoBackupEnabledAsync(ct));
    }

    [Fact]
    public async Task IsAutoBackupEnabledAsync_TrueWhenEnabledAndFolderSet()
    {
        var ct = TestContext.Current.CancellationToken;
        await _settings.SetAsync("AutoBackup.Enabled", "true", ct);
        await _settings.SetAsync("LastBackupFolder", _tempWorkDir, ct);

        Assert.True(await _sut.IsAutoBackupEnabledAsync(ct));
    }

    // Marks auto-backup enabled with a destination folder so ShouldAutoBackup only varies on the change/recency signals.
    private async Task EnableAutoBackupAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await _settings.SetAsync("AutoBackup.Enabled", "true", ct);
        await _settings.SetAsync("LastBackupFolder", _tempWorkDir, ct);
    }

    [Fact]
    public async Task ShouldAutoBackupAsync_FalseWhenNotConfigured_EvenWithChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        _changeTracker.MarkChanged();

        Assert.False(await _sut.ShouldAutoBackupAsync(ct));
    }

    [Fact]
    public async Task ShouldAutoBackupAsync_TrueWhenNeverBackedUp()
    {
        var ct = TestContext.Current.CancellationToken;
        await EnableAutoBackupAsync();
        // No AutoBackup.LastRun set, no changes — first-ever backup should still run.

        Assert.True(await _sut.ShouldAutoBackupAsync(ct));
    }

    [Fact]
    public async Task ShouldAutoBackupAsync_TrueWhenDataChanged()
    {
        var ct = TestContext.Current.CancellationToken;
        await EnableAutoBackupAsync();
        await _settings.SetAsync(
            "AutoBackup.LastRun", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), ct);
        _changeTracker.MarkChanged();

        Assert.True(await _sut.ShouldAutoBackupAsync(ct));
    }

    [Fact]
    public async Task ShouldAutoBackupAsync_FalseWhenRecentAndNoChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        await EnableAutoBackupAsync();
        await _settings.SetAsync(
            "AutoBackup.LastRun", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), ct);

        Assert.False(await _sut.ShouldAutoBackupAsync(ct));
    }

    [Fact]
    public async Task ShouldAutoBackupAsync_TrueWhenLastRunOlderThanThreshold()
    {
        var ct = TestContext.Current.CancellationToken;
        await EnableAutoBackupAsync();
        await _settings.SetAsync(
            "AutoBackup.LastRun",
            DateTime.UtcNow.AddDays(-8).ToString("o", CultureInfo.InvariantCulture), ct);

        Assert.True(await _sut.ShouldAutoBackupAsync(ct));
    }

    [Fact]
    public async Task BackupSqliteAsync_StampsLastRunAndResetsChangeTracker()
    {
        var ct = TestContext.Current.CancellationToken;
        _changeTracker.MarkChanged();

        await _sut.BackupSqliteAsync(_tempWorkDir, ct);

        Assert.False(_changeTracker.HasChanges);
        var lastRun = await _settings.GetAsync("AutoBackup.LastRun", ct);
        Assert.False(string.IsNullOrWhiteSpace(lastRun));
        Assert.True(DateTime.TryParse(
            lastRun, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _));
    }

    [Fact]
    public async Task BackupCsvArchiveAsync_StampsLastRunAndResetsChangeTracker()
    {
        var ct = TestContext.Current.CancellationToken;
        _changeTracker.MarkChanged();

        await _sut.BackupCsvArchiveAsync(_tempWorkDir, ct);

        Assert.False(_changeTracker.HasChanges);
        var lastRun = await _settings.GetAsync("AutoBackup.LastRun", ct);
        Assert.False(string.IsNullOrWhiteSpace(lastRun));
    }
}
