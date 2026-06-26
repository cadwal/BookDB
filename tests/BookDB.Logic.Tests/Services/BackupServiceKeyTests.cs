using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Logic.Services;
using BookDB.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Logic.Tests.Services;

/// <summary>
/// Tests that AutoBackupIfEnabledAsync reads the correct ISettingsService key names.
/// Uses a hand-written spy for ISettingsService — no mocking framework required.
/// </summary>
public sealed class BackupServiceKeyTests
{
    [Fact]
    public async Task AutoBackupIfEnabledAsync_ReadsCorrectEnabledKey()
    {
        var spy = new SpySettingsService();
        spy.SetValue("AutoBackup.Enabled", "false");
        var sut = MakeService(spy);

        await sut.AutoBackupIfEnabledAsync(TestContext.Current.CancellationToken);

        Assert.Contains("AutoBackup.Enabled", spy.RequestedKeys);
    }

    [Fact]
    public async Task AutoBackupIfEnabledAsync_ReadsCorrectFolderKey()
    {
        var spy = new SpySettingsService();
        spy.SetValue("AutoBackup.Enabled", "true");
        // LastBackupFolder returns null → early return before backup runs
        var sut = MakeService(spy);

        await sut.AutoBackupIfEnabledAsync(TestContext.Current.CancellationToken);

        Assert.Contains("LastBackupFolder", spy.RequestedKeys);
    }

    [Fact]
    public async Task AutoBackupIfEnabledAsync_ReadsCorrectFormatKey()
    {
        var spy = new SpySettingsService();
        spy.SetValue("AutoBackup.Enabled", "true");
        spy.SetValue("LastBackupFolder", System.IO.Path.GetTempPath());
        spy.SetValue("AutoBackup.Format", "SQLite");
        // The DB factory throws; the exception is caught by the catch block in AutoBackupIfEnabledAsync.
        var sut = MakeService(spy);

        await sut.AutoBackupIfEnabledAsync(TestContext.Current.CancellationToken);

        Assert.Contains("AutoBackup.Format", spy.RequestedKeys);
    }

    private static BackupService MakeService(ISettingsService settingsService)
    {
        var appSettings = new AppSettings { SqliteLibraryPath = System.IO.Path.GetTempFileName() };
        return new BackupService(
            new ThrowingDbContextFactory(), appSettings, settingsService, new NullResourceProvider(), new DataChangeTracker(),
            new BookDB.Data.Sqlite.SqliteBackupStrategy(new ThrowingDbContextFactory(), appSettings));
    }

    // Records every key passed to GetAsync so tests can assert on them.
    private sealed class SpySettingsService : ISettingsService
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);
        private readonly List<string> _requestedKeys = [];

        public IReadOnlyList<string> RequestedKeys => _requestedKeys;

        public void SetValue(string key, string? value) => _values[key] = value;

        public Task<string?> GetAsync(string key, CancellationToken ct = default)
        {
            _requestedKeys.Add(key);
            _values.TryGetValue(key, out var value);
            return Task.FromResult(value);
        }

        public Task SetAsync(string key, string? value, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    // Forces the backup step to fail (caught by AutoBackupIfEnabledAsync's catch block).
    private sealed class ThrowingDbContextFactory : IDbContextFactory<BookDbContext>
    {
        public BookDbContext CreateDbContext()
            => throw new InvalidOperationException("ThrowingDbContextFactory: not available in key tests.");

        public Task<BookDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("ThrowingDbContextFactory: not available in key tests.");
    }
}
