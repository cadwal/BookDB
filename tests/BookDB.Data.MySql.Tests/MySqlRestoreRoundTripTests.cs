using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using BookDB.Data.Sqlite;
using BookDB.Logic.Services;
using BookDB.Models;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Data.MySql.Tests;

/// <summary>
/// Restores a SQLite-produced CSV archive into a live MySQL/MariaDB container: proves the engine-neutral restore on
/// the remote backend, the identity resync running inside the restore transaction (a fresh insert must not collide),
/// and BLOB/UTC fidelity. Runs against both engines via the subclasses below.
/// </summary>
public abstract class MySqlRestoreRoundTripTests : IDisposable
{
    private readonly MySqlTestDbFixture _mysql;
    private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), $"bookdb_restore_src_{Guid.NewGuid():N}.db");
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), $"bookdb_restore_mysql_{Guid.NewGuid():N}");

    protected MySqlRestoreRoundTripTests(MySqlTestDbFixture mysql)
    {
        _mysql = mysql;
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_sqlitePath); } catch { /* best effort */ }
        try { Directory.Delete(_workDir, recursive: true); } catch { /* best effort */ }
    }

    private sealed class KeyResources : IResourceProvider
    {
        public string? GetString(string key) => key;
    }

    private static BackupService BackupServiceFor(IDbContextFactory<BookDbContext> factory, AppSettings settings, IBackupStrategy strategy)
        => new(factory, settings, new LookupService(factory, new KeyResources()), new KeyResources(), new DataChangeTracker(), strategy);

    [Fact]
    public async Task Restore_SqliteArchive_IntoMySql_RoundTrips_AndResyncsInsideTransaction()
    {
        Assert.SkipUnless(_mysql.IsAvailable, _mysql.SkipReason);
        var ct = TestContext.Current.CancellationToken;

        var sqlite = await MigrationTestData.BuildSqliteAsync(_sqlitePath, ct);
        await MigrationTestData.SeedAsync(sqlite, ct);
        var sqliteSettings = new AppSettings { SqliteLibraryPath = _sqlitePath };
        var archive = await BackupServiceFor(sqlite, sqliteSettings, new SqliteBackupStrategy(sqlite, sqliteSettings))
            .BackupCsvArchiveAsync(_workDir, ct, explicitFileName: "archive.zip");

        var mysql = await MigrationTestData.BuildMySqlAsync(_mysql.ConnectionString, ct);
        var mysqlBackup = BackupServiceFor(mysql, new AppSettings { Backend = DatabaseBackend.MySql }, new MySqlBackupStrategy());
        var restore = new CsvArchiveRestoreService(mysql, new MySqlIdentitySequenceResync(), mysqlBackup);

        var result = await restore.RestoreAsync(archive, _workDir, progress: null, ct: ct);

        Assert.True(result.Data.Outcome == MigrationOutcome.Completed, $"failed at {result.Data.FailedTable}: {result.Data.ErrorMessage}");
        Assert.True(result.Data.AllCountsMatch);

        await using var db = await mysql.CreateDbContextAsync(ct);
        var book = await db.Books.SingleAsync(ct);
        Assert.Equal(MigrationTestData.BookAdded, book.Added);
        var image = await db.BookImages.SingleAsync(ct);
        Assert.Equal(MigrationTestData.CoverBytes, image.ImageData);

        // Identity resync ran inside the committed restore transaction: a new insert gets a fresh id, not a collision.
        var fresh = new Book { Title = "Fresh", Added = MigrationTestData.BookAdded, Updated = MigrationTestData.BookAdded };
        db.Books.Add(fresh);
        await db.SaveChangesAsync(ct);
        Assert.True(fresh.BookId > book.BookId, $"expected id past {book.BookId}, got {fresh.BookId}");
    }
}

public sealed class MySqlServerRestoreRoundTripTests : MySqlRestoreRoundTripTests, IClassFixture<MySqlServerFixture>
{
    public MySqlServerRestoreRoundTripTests(MySqlServerFixture fixture) : base(fixture) { }
}

public sealed class MariaDbRestoreRoundTripTests : MySqlRestoreRoundTripTests, IClassFixture<MariaDbFixture>
{
    public MariaDbRestoreRoundTripTests(MariaDbFixture fixture) : base(fixture) { }
}
