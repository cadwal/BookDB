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
    private readonly string _configPath;
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
                Assembly.GetAssembly(typeof(BookDB.Data.Sqlite.SqliteDbUpRunner))!,
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

        _configPath = Path.Combine(Path.GetTempPath(), $"bookdb_backup_config_{Guid.NewGuid():N}.json");
        File.WriteAllText(_configPath, "{\"version\":1,\"backend\":\"Sqlite\",\"language\":\"sv\"}");
        var appSettings = new AppSettings { SqliteLibraryPath = _dbPath, ConfigPath = _configPath };
        _settings = new LookupService(_factory);
        _changeTracker = new DataChangeTracker();
        _sut = new BackupService(_factory, appSettings, _settings, _changeTracker,
            new BookDB.Data.Sqlite.SqliteBackupStrategy(_factory, appSettings));

        _tempWorkDir = Path.Combine(Path.GetTempPath(), $"bookdb_backup_workdir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempWorkDir);
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
        try { File.Delete(_configPath); } catch { /* best effort */ }
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
        // table can never be silently absent from the backup archive. ClientSession is the one
        // deliberate exception — it is live process presence, not library data, and is never backed up.
        var expected = typeof(BookDbContext).GetProperties()
            .Where(p => p.PropertyType.IsGenericType
                && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
                && p.Name != nameof(BookDbContext.ClientSessions))
            .Select(p => $"{p.Name}.csv")
            .ToList();

        Assert.NotEmpty(expected);
        var missing = expected.Where(name => !entryNames.Contains(name)).ToList();
        Assert.True(missing.Count == 0,
            $"CSV archive is missing per-table files: {string.Join(", ", missing)}");
    }

    [Fact]
    public async Task BackupSqliteAsync_IncludesConfigJsonAtArchiveRootWithMatchingContent()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.BackupSqliteAsync(_tempWorkDir, ct);

        var zip = Directory.GetFiles(_tempWorkDir, "*.zip").Single();
        using var archive = ZipFile.OpenRead(zip);
        var entry = archive.Entries.SingleOrDefault(e => e.FullName == "config.json");
        Assert.NotNull(entry);

        using var reader = new StreamReader(entry!.Open());
        Assert.Equal(await File.ReadAllTextAsync(_configPath, ct), reader.ReadToEnd());
    }

    [Fact]
    public async Task BackupCsvArchiveAsync_IncludesConfigJsonAtArchiveRootWithMatchingContent()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.BackupCsvArchiveAsync(_tempWorkDir, ct);

        var zip = Directory.GetFiles(_tempWorkDir, "*.zip").Single();
        using var archive = ZipFile.OpenRead(zip);
        var entry = archive.Entries.SingleOrDefault(e => e.FullName == "config.json");
        Assert.NotNull(entry);

        using var reader = new StreamReader(entry!.Open());
        Assert.Equal(await File.ReadAllTextAsync(_configPath, ct), reader.ReadToEnd());
    }

    [Fact]
    public async Task RestoreAsync_AbortedWhenSafetyBackupFails_OriginalNotModified()
    {
        var ct = TestContext.Current.CancellationToken;

        // safetyBackupFolder=null should throw before any restore attempt
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.RestoreAsync("somefile.zip", null!, ct));
    }

    [Fact]
    public async Task RestoreAsync_SafetyBackupGetsSuffixedNameWhenTodaysFileExists()
    {
        var ct = TestContext.Current.CancellationToken;

        var backupZip = await _sut.BackupSqliteAsync(_tempWorkDir, ct);

        var safetyFolder = Path.Combine(_tempWorkDir, "safety");
        Directory.CreateDirectory(safetyFolder);
        var date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var occupied = Path.Combine(safetyFolder, $"bookdb-safety-{date}.zip");
        await File.WriteAllTextAsync(occupied, "left over from an earlier restore", ct);

        await _sut.RestoreAsync(backupZip, safetyFolder, ct);

        Assert.Equal("left over from an earlier restore", await File.ReadAllTextAsync(occupied, ct));

        var suffixed = Path.Combine(safetyFolder, $"bookdb-safety-{date}-1.zip");
        Assert.True(File.Exists(suffixed), "safety backup should be written under a suffixed name");
        using var archive = ZipFile.OpenRead(suffixed);
        Assert.Contains(archive.Entries, e => e.Name == "library.db");
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

    [Fact]
    public async Task AutoBackupIfEnabledAsync_FallsBackToCsvArchive_WhenBackendHasNoFileBackup()
    {
        var ct = TestContext.Current.CancellationToken;
        // A remote backend reports SupportsFileBackup=false; auto-backup must use the CSV archive even though
        // the saved format preference is the default SQLite.
        var appSettings = new AppSettings { SqliteLibraryPath = _dbPath, ConfigPath = _configPath };
        var remoteSut = new BackupService(
            _factory, appSettings, _settings, _changeTracker, new NoFileBackupStrategy());

        await EnableAutoBackupAsync();
        _changeTracker.MarkChanged();

        await remoteSut.AutoBackupIfEnabledAsync(ct);

        Assert.Single(Directory.GetFiles(_tempWorkDir, "bookdb-csv-*.zip"));
    }

    // A backend with no client-side file backup (e.g. remote PostgreSQL).
    private sealed class NoFileBackupStrategy : BookDB.Data.Interfaces.IBackupStrategy
    {
        public bool SupportsFileBackup => false;

        public Task<string> BackupAsync(
            string destFolder, System.Threading.CancellationToken ct, string? explicitFileName = null,
            IProgress<ProgressUpdate<BackupProgressStep>>? progress = null) =>
            throw new InvalidOperationException("File backup must not be called when SupportsFileBackup is false.");
    }
}
