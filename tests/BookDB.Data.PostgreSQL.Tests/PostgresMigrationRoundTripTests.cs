using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using BookDB.Data.PostgreSQL;
using BookDB.Data.Sqlite;
using BookDB.Logic.Services;
using BookDB.Models;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BookDB.Data.PostgreSQL.Tests;

/// <summary>
/// Cross-engine migration against a live container: SQLite → Postgres → SQLite. Proves FK-safe copy, count
/// verification, DateTime Kind normalization onto Postgres' <c>timestamp without time zone</c>, the Postgres
/// identity-sequence resync (a fresh insert after the explicit-key copy must not collide), and round-trip fidelity.
/// </summary>
public sealed class PostgresMigrationRoundTripTests : IClassFixture<PostgresTestDbFixture>, IDisposable
{
    private readonly PostgresTestDbFixture _fixture;
    private readonly string _sqliteSourcePath;
    private readonly string _sqliteTargetPath;

    public PostgresMigrationRoundTripTests(PostgresTestDbFixture fixture)
    {
        _fixture = fixture;
        _sqliteSourcePath = Path.Combine(Path.GetTempPath(), $"bookdb_mig_src_{Guid.NewGuid():N}.db");
        _sqliteTargetPath = Path.Combine(Path.GetTempPath(), $"bookdb_mig_dst_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_sqliteSourcePath); } catch { /* best effort */ }
        try { File.Delete(_sqliteTargetPath); } catch { /* best effort */ }
    }

    private static readonly byte[] CoverBytes = [0x89, 0x50, 0x4E, 0x47, 0x10, 0x20];
    private static readonly DateTime BookAdded = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

    private static async Task<IDbContextFactory<BookDbContext>> BuildSqliteAsync(string path, CancellationToken ct)
    {
        var cs = $"Data Source={path}";
        var runner = new SqliteDbUpRunner(cs,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseStartupService>.Instance);
        await runner.RunAsync(new Progress<(int, int)>(), ct);

        var services = new ServiceCollection();
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();
        services.AddSqliteProvider(cs);
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<BookDbContext>>();
    }

    private async Task<IDbContextFactory<BookDbContext>> BuildPostgresAsync(CancellationToken ct)
    {
        var runner = new PostgresDbUpRunner(_fixture.ConnectionString,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseStartupService>.Instance);
        await runner.RunAsync(new Progress<(int, int)>(), ct);

        var services = new ServiceCollection();
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();
        services.AddPostgresProvider(_fixture.ConnectionString);
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<BookDbContext>>();
    }

    private static async Task SeedAsync(IDbContextFactory<BookDbContext> factory, CancellationToken ct)
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

    [Fact]
    public async Task Migrate_SqliteToPostgresToSqlite_RoundTrips_AndResyncsSequences()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;

        var sqliteSource = await BuildSqliteAsync(_sqliteSourcePath, ct);
        var postgres = await BuildPostgresAsync(ct);
        var sqliteTarget = await BuildSqliteAsync(_sqliteTargetPath, ct);
        await SeedAsync(sqliteSource, ct);

        var service = new LibraryMigrationService();

        // SQLite -> Postgres (clears the shared container first, so it is self-isolating).
        var toPg = await service.MigrateAsync(sqliteSource, postgres, new PostgresIdentitySequenceResync(), progress: null, ct);
        Assert.True(toPg.Outcome == MigrationOutcome.Completed, $"failed at {toPg.FailedTable}: {toPg.ErrorMessage}");
        Assert.True(toPg.AllCountsMatch);

        // The DateTime survived onto Postgres' timestamp-without-time-zone, and the BLOB is intact.
        await using (var pg = await postgres.CreateDbContextAsync(ct))
        {
            var book = await pg.Books.SingleAsync(ct);
            Assert.Equal(BookAdded, book.Added);
            var image = await pg.BookImages.SingleAsync(ct);
            Assert.Equal(CoverBytes, image.ImageData);

            // Sequence resync: a new insert must get an id past the highest copied one, not collide.
            var fresh = new Book { Title = "Fresh After Migration" };
            pg.Books.Add(fresh);
            await pg.SaveChangesAsync(ct);
            Assert.True(fresh.BookId > book.BookId, $"expected fresh id past {book.BookId}, got {fresh.BookId}");

            // Remove it so the reverse copy mirrors the original catalog.
            pg.Books.Remove(fresh);
            await pg.SaveChangesAsync(ct);
        }

        // Postgres -> SQLite: round-trips back to an empty SQLite database.
        var toSqlite = await service.MigrateAsync(postgres, sqliteTarget, new SqliteIdentitySequenceResync(), progress: null, ct);
        Assert.True(toSqlite.Outcome == MigrationOutcome.Completed, $"failed at {toSqlite.FailedTable}: {toSqlite.ErrorMessage}");
        Assert.True(toSqlite.AllCountsMatch);

        await using (var sq = await sqliteTarget.CreateDbContextAsync(ct))
        {
            Assert.Equal(1, await sq.Books.CountAsync(ct));
            Assert.Equal(1, await sq.Loans.CountAsync(ct));
            var image = await sq.BookImages.SingleAsync(ct);
            Assert.Equal(CoverBytes, image.ImageData);
            Assert.Equal(0, await sq.ClientSessions.CountAsync(ct));
        }
    }
}
