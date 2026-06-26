using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Data.Sqlite;
using BookDB.Logic.Services;
using BookDB.Models;
using BookDB.Models.Entities;
using DbUp;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Logic.Tests;

/// <summary>
/// Exercises the migration engine SQLite→SQLite (the cross-provider SQLite↔Postgres round-trip and the
/// sequence-resync fresh-id behaviour are covered by the container-backed tests). Both stores use the real
/// DbUp schema, so FK order, the empty-target clear, count verification, and DateTime normalization are real.
/// </summary>
public sealed class LibraryMigrationServiceTests : IDisposable
{
    private readonly string _sourcePath;
    private readonly string _targetPath;
    private readonly TestBookDbContextFactory _source;
    private readonly TestBookDbContextFactory _target;

    public LibraryMigrationServiceTests()
    {
        _sourcePath = TempDbPath();
        _targetPath = TempDbPath();
        _source = BuildFactory(_sourcePath);
        _target = BuildFactory(_targetPath);
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_sourcePath); } catch { /* best effort */ }
        try { File.Delete(_targetPath); } catch { /* best effort */ }
    }

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"bookdb_migrate_{Guid.NewGuid():N}.db");

    private static TestBookDbContextFactory BuildFactory(string path)
    {
        var cs = $"Data Source={path}";
        var upgrader = SqliteExtensions.SqliteDatabase(DeployChanges.To, cs)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(SqliteDbUpRunner))!,
                name => name.Contains(".Migrations."))
            .LogToNowhere()
            .Build();
        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException($"DbUp migration failed: {result.Error}");

        var options = new DbContextOptionsBuilder<BookDbContext>().UseSqlite(cs).Options;
        return new TestBookDbContextFactory(options);
    }

    private static readonly byte[] CoverBytes = [0xFF, 0xD8, 0xFF, 0x01, 0x02];
    private static readonly DateTime BookAdded = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    // The DbUp schema already seeds the lookup tables (Collection, Category, ContributorRole, BorrowerStatus,
    // BookImageType, …), so the seed only adds user data and references existing lookup rows by their ids.
    private async Task SeedSourceAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = _source.CreateDbContext();

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
        db.BookImages.Add(new BookImage { BookId = book.BookId, ImageData = CoverBytes, MimeType = "image/jpeg", BookImageTypeId = imageTypeId, IsPrimary = true, Added = BookAdded });

        var borrower = new Borrower { StatusId = borrowerStatusId, FirstName = "Grace" };
        db.Borrowers.Add(borrower);
        await db.SaveChangesAsync(ct);

        db.Loans.Add(new Loan { BookId = book.BookId, BorrowerId = borrower.BorrowerId, LoanedDate = BookAdded });
        db.Settings.Add(new Settings { Key = "FilterPanelWidth", Value = "210" });
        db.SavedSearches.Add(new SavedSearch { Name = "Sci-fi", QueryJson = "{}", CreatedAt = BookAdded });

        // Live presence — must NOT be copied.
        db.ClientSessions.Add(new ClientSession
        {
            SessionId = "abc", Hostname = "host", UserName = "user", AppVersion = "1.0", StartedAt = BookAdded, LastSeenAt = BookAdded,
        });

        await db.SaveChangesAsync(ct);
    }

    private async Task<MigrationResult> MigrateAsync()
    {
        var service = new LibraryMigrationService();
        return await service.MigrateAsync(
            _source, _target, new SqliteIdentitySequenceResync(), progress: null, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Migrate_CopiesEveryTable_CountsMatch_AndAllCountsMatchIsTrue()
    {
        await SeedSourceAsync();

        var result = await MigrateAsync();

        Assert.True(result.Outcome == MigrationOutcome.Completed, $"failed at {result.FailedTable}: {result.ErrorMessage}");
        Assert.True(result.AllCountsMatch);
        Assert.All(result.Tables, t => Assert.Equal(t.SourceCount, t.TargetCount));

        await using var db = _target.CreateDbContext();
        Assert.Equal(1, await db.Books.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.BookImages.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.Loans.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.BookContributors.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Migrate_ExcludesClientSession()
    {
        await SeedSourceAsync();
        await using (var srcDb = _source.CreateDbContext())
            Assert.Equal(1, await srcDb.ClientSessions.CountAsync(TestContext.Current.CancellationToken));

        await MigrateAsync();

        await using var db = _target.CreateDbContext();
        Assert.Equal(0, await db.ClientSessions.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Migrate_PreservesPrimaryKeys_ImageBytes_AndUtcDates()
    {
        await SeedSourceAsync();

        await MigrateAsync();

        await using var db = _target.CreateDbContext();
        var book = await db.Books.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, book.BookId);
        Assert.Equal(BookAdded, book.Added);

        var image = await db.BookImages.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CoverBytes, image.ImageData);
    }

    [Fact]
    public async Task Migrate_TargetRemainsUsable_NewInsertGetsFreshId()
    {
        await SeedSourceAsync();

        await MigrateAsync();

        await using var db = _target.CreateDbContext();
        var added = new Book { Title = "Brand New" };
        db.Books.Add(added);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.True(added.BookId > 1, $"expected a fresh id past the copied max, got {added.BookId}");
    }

    [Fact]
    public async Task Migrate_OverwritesExistingTargetData()
    {
        await SeedSourceAsync();

        // Pre-existing junk in the target must be cleared so counts verify against the source.
        await using (var pre = _target.CreateDbContext())
        {
            pre.Publishers.Add(new Publisher { Name = "Stale" });
            await pre.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await MigrateAsync();

        Assert.True(result.AllCountsMatch, "counts should match once the target is cleared and recopied");
        await using var db = _target.CreateDbContext();
        Assert.False(await db.Publishers.AnyAsync(p => p.Name == "Stale", TestContext.Current.CancellationToken));
    }
}
