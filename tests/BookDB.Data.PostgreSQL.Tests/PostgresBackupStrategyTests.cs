using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using BookDB.Logic.Services;
using BookDB.Models;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookDB.Data.PostgreSQL.Tests;

/// <summary>
/// Verifies the PostgreSQL backup strategy reports no file-format backup (callers fall back to CSV) and that
/// the engine-neutral CSV archive builds successfully against a live Postgres-backed context.
/// </summary>
public sealed class PostgresBackupStrategyTests : IClassFixture<PostgresTestDbFixture>
{
    private readonly PostgresTestDbFixture _fixture;

    public PostgresBackupStrategyTests(PostgresTestDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Strategy_ReportsNoFileBackup_AndThrowsOnBackup()
    {
        // Registration is resolvable without a database connection.
        var services = new ServiceCollection();
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();
        services.AddPostgresProvider("Host=localhost;Database=unused");
        await using var sp = services.BuildServiceProvider();

        var strategy = sp.GetRequiredService<IBackupStrategy>();

        Assert.IsType<PostgresBackupStrategy>(strategy);
        Assert.False(strategy.SupportsFileBackup);
        await Assert.ThrowsAsync<NotSupportedException>(
            async () => await strategy.BackupAsync(Path.GetTempPath(), CancellationToken.None));
    }

    [Fact]
    public async Task CsvArchive_BuildsFromContainerBackedContext()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;

        var runner = new PostgresDbUpRunner(_fixture.ConnectionString, NullLogger<DatabaseStartupService>.Instance);
        await runner.RunAsync(new Progress<(int applied, int total)>(), ct);

        var services = new ServiceCollection();
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();
        services.AddPostgresProvider(_fixture.ConnectionString);
        await using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IDbContextFactory<BookDbContext>>();

        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            db.Books.Add(new Book { Title = $"Archive {Guid.NewGuid():N}", Added = DateTime.UtcNow, Updated = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
        }

        var appSettings = new AppSettings { Backend = DatabaseBackend.PostgreSql, ConnectionString = _fixture.ConnectionString };
        var backupService = new BackupService(
            factory, appSettings, new InMemorySettingsService(),
            sp.GetRequiredService<IDataChangeTracker>(), sp.GetRequiredService<IBackupStrategy>());

        var workDir = Path.Combine(Path.GetTempPath(), $"bookdb_pgcsv_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            var zipPath = await backupService.BackupCsvArchiveAsync(workDir, ct);

            using var archive = ZipFile.OpenRead(zipPath);
            Assert.Contains(archive.Entries, e => e.Name == "Books.csv");
            Assert.Contains(archive.Entries, e => e.Name == "Settings.csv");
            Assert.Contains(archive.Entries, e => e.Name == "BookImages.csv");
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class InMemorySettingsService : ISettingsService
    {
        private readonly Dictionary<string, string?> _store = new();
        public Task<string?> GetAsync(string key, CancellationToken ct = default)
            => Task.FromResult(_store.TryGetValue(key, out var v) ? v : null);
        public Task SetAsync(string key, string? value, CancellationToken ct = default)
        {
            _store[key] = value;
            return Task.CompletedTask;
        }
    }
}
