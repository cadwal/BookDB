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
/// Restores a SQLite-produced CSV archive into a live Postgres container: proves the engine-neutral restore on
/// the remote backend, the identity-sequence resync running inside the restore transaction (a fresh insert must
/// not collide), and BLOB/UTC fidelity.
/// </summary>
public sealed class PostgresRestoreRoundTripTests : IClassFixture<PostgresTestDbFixture>, IDisposable
{
    private readonly PostgresTestDbFixture _fixture;
    private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), $"bookdb_restore_src_{Guid.NewGuid():N}.db");
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), $"bookdb_restore_pg_{Guid.NewGuid():N}");

    public PostgresRestoreRoundTripTests(PostgresTestDbFixture fixture)
    {
        _fixture = fixture;
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_sqlitePath); } catch { /* best effort */ }
        try { Directory.Delete(_workDir, recursive: true); } catch { /* best effort */ }
    }

    private sealed class KeyResources : IResourceProvider
    {
        public string? GetString(string key) => key;
    }

    private static readonly byte[] CoverBytes = [0x89, 0x50, 0x4E, 0x47, 0x55, 0x66];
    private static readonly DateTime BookAdded = new(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc);

    private static IDbContextFactory<BookDbContext> BuildSqlite(string path, CancellationToken ct)
    {
        var cs = $"Data Source={path}";
        new SqliteDbUpRunner(cs, Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseStartupService>.Instance)
            .RunAsync(new Progress<(int, int)>(), ct).GetAwaiter().GetResult();
        var services = new ServiceCollection();
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();
        services.AddSqliteProvider(cs);
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<BookDbContext>>();
    }

    private IDbContextFactory<BookDbContext> BuildPostgres(CancellationToken ct)
    {
        new PostgresDbUpRunner(_fixture.ConnectionString, Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseStartupService>.Instance)
            .RunAsync(new Progress<(int, int)>(), ct).GetAwaiter().GetResult();
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
        await db.SaveChangesAsync(ct);
    }

    private BackupService BackupServiceFor(IDbContextFactory<BookDbContext> factory, AppSettings settings, IBackupStrategy strategy)
        => new(factory, settings, new LookupService(factory, new KeyResources()), new KeyResources(), new DataChangeTracker(), strategy);

    [Fact]
    public async Task Restore_SqliteArchive_IntoPostgres_RoundTrips_AndResyncsInsideTransaction()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.SkipReason);
        var ct = TestContext.Current.CancellationToken;

        var sqlite = BuildSqlite(_sqlitePath, ct);
        await SeedAsync(sqlite, ct);
        var sqliteSettings = new AppSettings { SqliteLibraryPath = _sqlitePath };
        var archive = await BackupServiceFor(sqlite, sqliteSettings, new SqliteBackupStrategy(sqlite, sqliteSettings))
            .BackupCsvArchiveAsync(_workDir, ct, explicitFileName: "archive.zip");

        var postgres = BuildPostgres(ct);
        var pgBackup = BackupServiceFor(postgres, new AppSettings { Backend = DatabaseBackend.PostgreSql }, new PostgresBackupStrategy());
        var restore = new CsvArchiveRestoreService(postgres, new PostgresIdentitySequenceResync(), pgBackup);

        var result = await restore.RestoreAsync(archive, _workDir, progress: null, ct: ct);

        Assert.True(result.Data.Outcome == MigrationOutcome.Completed, $"failed at {result.Data.FailedTable}: {result.Data.ErrorMessage}");
        Assert.True(result.Data.AllCountsMatch);

        await using var db = await postgres.CreateDbContextAsync(ct);
        var book = await db.Books.SingleAsync(ct);
        Assert.Equal(BookAdded, book.Added);
        var image = await db.BookImages.SingleAsync(ct);
        Assert.Equal(CoverBytes, image.ImageData);

        // Sequence resync ran inside the committed restore transaction: a new insert gets a fresh id.
        var fresh = new Book { Title = "Fresh" };
        db.Books.Add(fresh);
        await db.SaveChangesAsync(ct);
        Assert.True(fresh.BookId > book.BookId, $"expected id past {book.BookId}, got {fresh.BookId}");
    }
}
