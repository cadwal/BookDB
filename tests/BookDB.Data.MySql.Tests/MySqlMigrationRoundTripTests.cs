using System;
using System.IO;
using System.Linq;
using System.Threading;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookDB.Data.MySql.Tests;

/// <summary>
/// Build + seed helpers shared by the cross-engine Move round-trip tests. The migration engine reads through EF
/// entities, so the same seeded catalog round-trips regardless of which engines are the source and target.
/// </summary>
internal static class MigrationTestData
{
    public static readonly byte[] CoverBytes = [0x89, 0x50, 0x4E, 0x47, 0x10, 0x20];
    public static readonly DateTime BookAdded = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

    public static async Task<IDbContextFactory<BookDbContext>> BuildSqliteAsync(string path, CancellationToken ct)
    {
        var cs = $"Data Source={path}";
        await new SqliteDbUpRunner(cs, NullLogger<DatabaseStartupService>.Instance).RunAsync(new Progress<(int, int)>(), ct);

        var services = new ServiceCollection();
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();
        services.AddSqliteProvider(cs);
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<BookDbContext>>();
    }

    public static async Task<IDbContextFactory<BookDbContext>> BuildMySqlAsync(string cs, CancellationToken ct)
    {
        await new MySqlDbUpRunner(cs, NullLogger<DatabaseStartupService>.Instance).RunAsync(new Progress<(int, int)>(), ct);

        var services = new ServiceCollection();
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();
        services.AddMySqlProvider(cs);
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<BookDbContext>>();
    }

    /// <summary>Seeds one book wired to a publisher, contributor, category, image (BLOB), borrower, and loan —
    /// enough to exercise FK-ordered copy and BLOB/UTC fidelity.</summary>
    public static async Task SeedAsync(IDbContextFactory<BookDbContext> factory, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var roleId = await db.ContributorRoles.Select(r => r.ContributorRoleId).FirstAsync(ct);
        var categoryId = await db.Categories.Select(c => c.CategoryId).FirstAsync(ct);
        var imageTypeId = await db.BookImageTypes.Select(t => t.BookImageTypeId).FirstAsync(ct);
        var borrowerStatusId = await db.BorrowerStatuses.Select(s => s.BorrowerStatusId).FirstAsync(ct);

        var publisher = new Publisher { Name = "Acme Press" };
        var person = new Person { DisplayName = "Ada Lovelace", SortName = "Lovelace, Ada" };
        db.Publishers.Add(publisher);
        db.People.Add(person);
        await db.SaveChangesAsync(ct);

        var book = new Book { Title = "A History of Computing", PublisherId = publisher.PublisherId, Added = BookAdded, Updated = BookAdded };
        db.Books.Add(book);
        await db.SaveChangesAsync(ct);

        db.BookContributors.Add(new BookContributor { BookId = book.BookId, PersonId = person.PersonId, ContributorRoleId = roleId });
        db.BookCategories.Add(new BookCategory { BookId = book.BookId, CategoryId = categoryId });
        db.BookImages.Add(new BookImage { BookId = book.BookId, ImageData = CoverBytes, MimeType = "image/png", BookImageTypeId = imageTypeId, IsPrimary = true, Added = BookAdded });
        var borrower = new Borrower { StatusId = borrowerStatusId, FirstName = "Grace" };
        db.Borrowers.Add(borrower);
        await db.SaveChangesAsync(ct);

        db.Loans.Add(new Loan { BookId = book.BookId, BorrowerId = borrower.BorrowerId, LoanedDate = BookAdded });
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Cross-engine Move against a live container: SQLite → MySQL → SQLite. Proves the FK-safe copy + count
/// verification both directions, BLOB and UTC <c>DateTime</c> fidelity onto MySQL's <c>LONGBLOB</c>/
/// <c>datetime(6)</c>, and that the no-op identity resync is sufficient (a fresh insert after the explicit-key
/// copy gets an id past the highest copied one — InnoDB advanced AUTO_INCREMENT on its own). Run on both engines
/// via the subclasses at the bottom.
/// </summary>
public abstract class MySqlMigrationRoundTripTests : IDisposable
{
    private readonly MySqlTestDbFixture _mysql;
    private readonly string _sqliteSourcePath;
    private readonly string _sqliteTargetPath;

    protected MySqlMigrationRoundTripTests(MySqlTestDbFixture mysql)
    {
        _mysql = mysql;
        _sqliteSourcePath = Path.Combine(Path.GetTempPath(), $"bookdb_mig_src_{Guid.NewGuid():N}.db");
        _sqliteTargetPath = Path.Combine(Path.GetTempPath(), $"bookdb_mig_dst_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_sqliteSourcePath); } catch { /* best effort */ }
        try { File.Delete(_sqliteTargetPath); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Migrate_SqliteToMySqlToSqlite_RoundTrips_AndResyncIsNoOp()
    {
        Assert.SkipUnless(_mysql.IsAvailable, _mysql.SkipReason);
        var ct = TestContext.Current.CancellationToken;

        var sqliteSource = await MigrationTestData.BuildSqliteAsync(_sqliteSourcePath, ct);
        var mysql = await MigrationTestData.BuildMySqlAsync(_mysql.ConnectionString, ct);
        var sqliteTarget = await MigrationTestData.BuildSqliteAsync(_sqliteTargetPath, ct);
        await MigrationTestData.SeedAsync(sqliteSource, ct);

        var service = new LibraryMigrationService();

        // SQLite -> MySQL (the engine clears the target first, so the shared container is self-isolating).
        var toMySql = await service.MigrateAsync(sqliteSource, mysql, new MySqlIdentitySequenceResync(), progress: null, ct);
        Assert.True(toMySql.Outcome == MigrationOutcome.Completed, $"failed at {toMySql.FailedTable}: {toMySql.ErrorMessage}");
        Assert.True(toMySql.AllCountsMatch);

        await using (var my = await mysql.CreateDbContextAsync(ct))
        {
            var book = await my.Books.SingleAsync(ct);
            Assert.Equal(MigrationTestData.BookAdded, book.Added);
            var image = await my.BookImages.SingleAsync(ct);
            Assert.Equal(MigrationTestData.CoverBytes, image.ImageData);

            // No-op resync is sufficient: a new insert must get an id past the highest copied one, not collide.
            var fresh = new Book { Title = "Fresh After Migration", Added = MigrationTestData.BookAdded, Updated = MigrationTestData.BookAdded };
            my.Books.Add(fresh);
            await my.SaveChangesAsync(ct);
            Assert.True(fresh.BookId > book.BookId, $"expected fresh id past {book.BookId}, got {fresh.BookId}");

            // Remove it so the reverse copy mirrors the original catalog.
            my.Books.Remove(fresh);
            await my.SaveChangesAsync(ct);
        }

        // MySQL -> SQLite: round-trips back to an empty SQLite database.
        var toSqlite = await service.MigrateAsync(mysql, sqliteTarget, new SqliteIdentitySequenceResync(), progress: null, ct);
        Assert.True(toSqlite.Outcome == MigrationOutcome.Completed, $"failed at {toSqlite.FailedTable}: {toSqlite.ErrorMessage}");
        Assert.True(toSqlite.AllCountsMatch);

        await using (var sq = await sqliteTarget.CreateDbContextAsync(ct))
        {
            Assert.Equal(1, await sq.Books.CountAsync(ct));
            Assert.Equal(1, await sq.Loans.CountAsync(ct));
            var image = await sq.BookImages.SingleAsync(ct);
            Assert.Equal(MigrationTestData.CoverBytes, image.ImageData);
            Assert.Equal(0, await sq.ClientSessions.CountAsync(ct));
        }
    }
}

public sealed class MySqlServerMigrationRoundTripTests : MySqlMigrationRoundTripTests, IClassFixture<MySqlServerFixture>
{
    public MySqlServerMigrationRoundTripTests(MySqlServerFixture fixture) : base(fixture) { }
}

public sealed class MariaDbMigrationRoundTripTests : MySqlMigrationRoundTripTests, IClassFixture<MariaDbFixture>
{
    public MariaDbMigrationRoundTripTests(MariaDbFixture fixture) : base(fixture) { }
}
