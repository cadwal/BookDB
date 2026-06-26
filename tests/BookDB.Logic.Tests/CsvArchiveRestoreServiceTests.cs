using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BookDB.Data;
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
/// Exercises the CSV-archive restore engine SQLite→SQLite end to end: a real archive produced by the backup
/// service is restored into a separate database, proving the scalar-only CSV round-trip, FK-safe import,
/// transactional rollback on failure, and that ClientSession is never restored.
/// </summary>
public sealed class CsvArchiveRestoreServiceTests : IDisposable
{
    private readonly string _sourcePath = TempDbPath();
    private readonly string _targetPath = TempDbPath();
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), $"bookdb_restore_work_{Guid.NewGuid():N}");

    public CsvArchiveRestoreServiceTests() => Directory.CreateDirectory(_workDir);

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_sourcePath); } catch { /* best effort */ }
        try { File.Delete(_targetPath); } catch { /* best effort */ }
        try { Directory.Delete(_workDir, recursive: true); } catch { /* best effort */ }
    }

    private static string TempDbPath() => Path.Combine(Path.GetTempPath(), $"bookdb_restore_{Guid.NewGuid():N}.db");

    private static TestBookDbContextFactory BuildFactory(string path)
    {
        var cs = $"Data Source={path}";
        var upgrader = SqliteExtensions.SqliteDatabase(DeployChanges.To, cs)
            .WithScriptsEmbeddedInAssembly(Assembly.GetAssembly(typeof(SqliteDbUpRunner))!, n => n.Contains(".Migrations."))
            .LogToNowhere().Build();
        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException($"DbUp migration failed: {result.Error}");
        var options = new DbContextOptionsBuilder<BookDbContext>().UseSqlite(cs).Options;
        return new TestBookDbContextFactory(options);
    }

    private BackupService BackupServiceFor(TestBookDbContextFactory factory, string dbPath)
    {
        var settings = new AppSettings { SqliteLibraryPath = dbPath };
        return new BackupService(factory, settings, new LookupService(factory, new NullResourceProvider()),
            new NullResourceProvider(), new DataChangeTracker(), new SqliteBackupStrategy(factory, settings));
    }

    private static readonly byte[] CoverBytes = [0x89, 0x50, 0x4E, 0x47, 0xAB, 0xCD];
    private static readonly DateTime BookAdded = new(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);

    private static async Task SeedAsync(TestBookDbContextFactory factory)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = factory.CreateDbContext();
        var roleId = await db.ContributorRoles.Select(r => r.ContributorRoleId).FirstAsync(ct);
        var categoryId = await db.Categories.Select(c => c.CategoryId).FirstAsync(ct);
        var imageTypeId = await db.BookImageTypes.Select(t => t.BookImageTypeId).FirstAsync(ct);
        var statusId = await db.BorrowerStatuses.Select(s => s.BorrowerStatusId).FirstAsync(ct);

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
        var borrower = new Borrower { StatusId = statusId, FirstName = "Grace" };
        db.Borrowers.Add(borrower);
        await db.SaveChangesAsync(ct);

        db.Loans.Add(new Loan { BookId = book.BookId, BorrowerId = borrower.BorrowerId, LoanedDate = BookAdded });
        db.ClientSessions.Add(new ClientSession { SessionId = "s1", Hostname = "h", UserName = "u", AppVersion = "1", StartedAt = BookAdded, LastSeenAt = BookAdded });
        await db.SaveChangesAsync(ct);
    }

    private async Task<string> BuildArchiveAsync()
    {
        var source = BuildFactory(_sourcePath);
        await SeedAsync(source);
        return await BackupServiceFor(source, _sourcePath)
            .BackupCsvArchiveAsync(_workDir, TestContext.Current.CancellationToken, explicitFileName: "archive.zip");
    }

    private CsvArchiveRestoreService RestoreServiceFor(TestBookDbContextFactory target)
        => new(target, new SqliteIdentitySequenceResync(), BackupServiceFor(target, _targetPath));

    [Fact]
    public async Task Restore_RoundTrips_CatalogData_AndExcludesClientSession()
    {
        var ct = TestContext.Current.CancellationToken;
        var archive = await BuildArchiveAsync();
        var target = BuildFactory(_targetPath);

        var result = await RestoreServiceFor(target).RestoreAsync(archive, _workDir, progress: null, ct: ct);

        Assert.True(result.Data.Outcome == MigrationOutcome.Completed, $"failed at {result.Data.FailedTable}: {result.Data.ErrorMessage}");
        Assert.True(result.Data.AllCountsMatch);

        await using var db = target.CreateDbContext();
        var book = await db.Books.SingleAsync(ct);
        Assert.Equal("A History of Computing", book.Title);
        Assert.Equal(BookAdded, book.Added);
        Assert.Equal(1, await db.Loans.CountAsync(ct));
        Assert.Equal(1, await db.BookContributors.CountAsync(ct));
        var image = await db.BookImages.SingleAsync(ct);
        Assert.Equal(CoverBytes, image.ImageData);
        Assert.Equal(0, await db.ClientSessions.CountAsync(ct)); // never in the archive
    }

    [Fact]
    public async Task Restore_TargetRemainsUsable_NewInsertGetsFreshId()
    {
        var ct = TestContext.Current.CancellationToken;
        var archive = await BuildArchiveAsync();
        var target = BuildFactory(_targetPath);
        await RestoreServiceFor(target).RestoreAsync(archive, _workDir, progress: null, ct: ct);

        await using var db = target.CreateDbContext();
        var fresh = new Book { Title = "New" };
        db.Books.Add(fresh);
        await db.SaveChangesAsync(ct);
        Assert.True(fresh.BookId > 1);
    }

    [Fact]
    public async Task Restore_AppliesPreferenceSettings_ButKeepsMachineSpecificLiveValues()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = BuildFactory(_sourcePath);
        await SeedAsync(source);
        await using (var db = source.CreateDbContext())
        {
            db.Settings.Add(new Settings { Key = "DefaultCollectionId", Value = "7" }); // a user preference
            db.Settings.Add(new Settings { Key = "WindowWidth", Value = "999" });        // machine-specific
            await db.SaveChangesAsync(ct);
        }
        var archive = await BackupServiceFor(source, _sourcePath)
            .BackupCsvArchiveAsync(_workDir, ct, explicitFileName: "settings.zip");

        var target = BuildFactory(_targetPath);
        await using (var db = target.CreateDbContext())
        {
            db.Settings.Add(new Settings { Key = "WindowWidth", Value = "111" }); // this machine's live layout
            await db.SaveChangesAsync(ct);
        }

        await RestoreServiceFor(target).RestoreAsync(archive, _workDir, progress: null, ct: ct);

        await using var check = target.CreateDbContext();
        Assert.Equal("7", (await check.Settings.SingleAsync(s => s.Key == "DefaultCollectionId", ct)).Value);
        Assert.Equal("111", (await check.Settings.SingleAsync(s => s.Key == "WindowWidth", ct)).Value);
    }

    [Fact]
    public async Task Restore_IntoExplicitTarget_LandsThere_AndLeavesActiveBackendUntouched()
    {
        var ct = TestContext.Current.CancellationToken;
        var archive = await BuildArchiveAsync(); // _sourcePath now holds a 1-book catalog

        // The "active" backend is the source; give it an extra book that is NOT in the archive.
        var active = BuildFactory(_sourcePath);
        await using (var db = active.CreateDbContext())
        {
            db.Books.Add(new Book { Title = "Active-only" });
            await db.SaveChangesAsync(ct);
        }
        var engine = new CsvArchiveRestoreService(active, new SqliteIdentitySequenceResync(), BackupServiceFor(active, _sourcePath));

        var targetB = BuildFactory(_targetPath);
        var target = new RestoreTargetServices(targetB, new SqliteIdentitySequenceResync(), BackupServiceFor(targetB, _targetPath));

        var result = await engine.RestoreAsync(archive, _workDir, progress: null, target: target, ct: ct);

        Assert.True(result.Data.Outcome == MigrationOutcome.Completed, $"failed at {result.Data.FailedTable}: {result.Data.ErrorMessage}");

        await using (var b = targetB.CreateDbContext())
            Assert.Equal(1, await b.Books.CountAsync(ct)); // the archive landed in the explicit target
        await using (var a = active.CreateDbContext())
            Assert.Equal(2, await a.Books.CountAsync(ct)); // the active backend was never cleared
    }

    [Fact]
    public async Task Restore_MidFailure_RollsBack_AndKeepsOriginalData()
    {
        var ct = TestContext.Current.CancellationToken;
        var archive = await BuildArchiveAsync();

        // Corrupt the archive so a Loan references a borrower that won't exist (Borrowers emptied) — the FK-enforced
        // Loan import must fail and roll the whole restore back.
        var corrupted = CorruptArchiveRemovingBorrowers(archive);

        var target = BuildFactory(_targetPath);
        await using (var seed = target.CreateDbContext())
        {
            seed.Books.Add(new Book { Title = "Original" });
            await seed.SaveChangesAsync(ct);
        }

        var result = await RestoreServiceFor(target).RestoreAsync(corrupted, _workDir, progress: null, ct: ct);

        Assert.Equal(MigrationOutcome.Failed, result.Data.Outcome);
        Assert.Equal(MigrationTable.Loan, result.Data.FailedTable);

        await using var db = target.CreateDbContext();
        var book = await db.Books.SingleAsync(ct); // the clear + partial import was rolled back
        Assert.Equal("Original", book.Title);
    }

    private string CorruptArchiveRemovingBorrowers(string archive)
    {
        var extract = Path.Combine(_workDir, "corrupt");
        ZipFile.ExtractToDirectory(archive, extract);
        // Keep the header row only, so no Borrower rows are imported.
        var borrowersCsv = Path.Combine(extract, "Borrowers.csv");
        var header = File.ReadLines(borrowersCsv).First();
        File.WriteAllText(borrowersCsv, header + Environment.NewLine);
        var corrupted = Path.Combine(_workDir, "corrupted.zip");
        ZipFile.CreateFromDirectory(extract, corrupted);
        return corrupted;
    }
}
