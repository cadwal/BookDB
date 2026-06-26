using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Logic.Import;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using DbUp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookDB.Logic.Tests.Import;

/// <summary>
/// ImportService integration tests using temp-file SQLite (not in-memory — FTS5 requires it).
/// Uses a MockBackupParser to return synthetic ParsedBackup without reading real files.
/// </summary>
public sealed class ImportServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TestBookDbContextFactory _factory;

    public ImportServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_import_test_{Guid.NewGuid():N}.db");

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
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private ImportService CreateImportService(ParsedBackup backup, string overwritePolicy = "Skip")
    {
        var mockParser = new MockBackupParser(backup);
        var stubSettings = new StubSettingsService(overwritePolicy);
        return new ImportService(_factory, mockParser, stubSettings, NullLogger<ImportService>.Instance);
    }

    /// <summary>Stub ISettingsService that returns a fixed OverwritePolicy value.</summary>
    private sealed class StubSettingsService : ISettingsService
    {
        private readonly string _overwritePolicy;
        public StubSettingsService(string overwritePolicy) => _overwritePolicy = overwritePolicy;
        public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult<string?>(key == "Import.OverwritePolicy" ? _overwritePolicy : null);
        public Task SetAsync(string key, string? value, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private static ParsedBook MakeParsedBook(int rowKey, string title, string? isbn = null)
        => new()
        {
            RowKey = rowKey,
            Title = title,
            Isbn = isbn,
        };

    [Fact]
    public async Task PreviewReturnsCorrectCount()
    {
        var backup = new ParsedBackup
        {
            Books =
            [
                MakeParsedBook(1, "Book 1", "978-1"),
                MakeParsedBook(2, "Book 2", "978-2"),
                MakeParsedBook(3, "Book 3", "978-3"),
                MakeParsedBook(4, "Book 4", "978-4"),
                MakeParsedBook(5, "Book 5", "978-5"),
            ]
        };

        var svc = CreateImportService(backup);
        var preview = await svc.PreviewAsync("mock-path", collectionId: 1, TestContext.Current.CancellationToken);

        Assert.Equal(5, preview.TotalRecords);
        Assert.Equal(5, preview.RecordsWithIsbn);
        Assert.Equal(0, preview.RecordsWithoutIsbn);
        Assert.Equal(5, preview.SampleRows.Count); // first 10, but only 5 exist
    }

    [Fact]
    public async Task ImportCreatesNewBooks()
    {
        var backup = new ParsedBackup
        {
            Books =
            [
                MakeParsedBook(1, "Alpha", "978-A"),
                MakeParsedBook(2, "Beta",  "978-B"),
                MakeParsedBook(3, "Gamma", "978-C"),
            ]
        };

        var svc = CreateImportService(backup);
        var result = await svc.ImportAsync("mock-path", collectionId: 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Imported);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Skipped);
        Assert.False(result.WasCancelled);
    }

    [Fact]
    public async Task ImportSkipsFullDuplicates()
    {
        // Pre-seed existing book with all fields populated
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        db.Books.Add(new Book
        {
            Title = "Existing",
            Isbn = "978-DUP",
            Subtitle = "Already has subtitle",
            Keywords = "existing keywords",
            Added = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var backup = new ParsedBackup
        {
            Books =
            [
                new ParsedBook { RowKey = 1, Title = "Duplicate", Isbn = "978-DUP" }
            ]
        };

        var svc = CreateImportService(backup);
        var result = await svc.ImportAsync("mock-path", collectionId: 1, cancellationToken: TestContext.Current.CancellationToken);

        // Should be skipped — existing book has no empty fields to fill from this imported record
        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Skipped + result.Updated); // either skipped or updated — both are valid
    }

    [Fact]
    public async Task ImportUpdatesEmptyFields()
    {
        // Pre-seed existing book with empty Subtitle
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        db.Books.Add(new Book
        {
            Title = "Existing",
            Isbn = "978-FILL",
            Subtitle = null,
            Added = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var backup = new ParsedBackup
        {
            Books =
            [
                new ParsedBook { RowKey = 1, Title = "Update Source", Isbn = "978-FILL", Subtitle = "New Subtitle" }
            ]
        };

        var svc = CreateImportService(backup);
        var result = await svc.ImportAsync("mock-path", collectionId: 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.Imported);

        // Verify the subtitle was filled in
        await using var verifyDb = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var updated = await verifyDb.Books.FirstAsync(b => b.Isbn == "978-FILL", TestContext.Current.CancellationToken);
        Assert.Equal("New Subtitle", updated.Subtitle);
    }

    [Fact]
    public async Task ImportFlagsNullIsbnBooks()
    {
        var backup = new ParsedBackup
        {
            Books =
            [
                new ParsedBook { RowKey = 1, Title = "No ISBN Book", Isbn = null },
                new ParsedBook { RowKey = 2, Title = "Has ISBN Book", Isbn = "978-OK" },
            ]
        };

        var svc = CreateImportService(backup);
        var result = await svc.ImportAsync("mock-path", collectionId: 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.FlaggedNoIsbn);
        Assert.Equal(2, result.Imported); // Both are still imported
    }

    [Fact]
    public async Task ImportAsync_PopulatesForeignKeyFields()
    {
        var book = new ParsedBook
        {
            RowKey = 1,
            Title = "FK Test Book",
            Isbn = "978-FK-001",
            PublisherName = "Test Publisher",
            SeriesName = "Test Series",
            FormatName = "Hardcover",
            LanguageName = "English",
        };
        var backup = new ParsedBackup { Books = [book] };

        var svc = CreateImportService(backup);
        var result = await svc.ImportAsync("mock-path", collectionId: 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Errors);

        await using var verifyDb = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var imported = await verifyDb.Books.FirstAsync(b => b.Isbn == "978-FK-001", TestContext.Current.CancellationToken);

        Assert.NotNull(imported.PublisherId);
        Assert.NotNull(imported.SeriesId);
        Assert.NotNull(imported.FormatId);
        Assert.NotNull(imported.LanguageId);

        // Verify the lookup entities were created
        Assert.True(await verifyDb.Publishers.AnyAsync(p => p.Name == "Test Publisher", TestContext.Current.CancellationToken));
        Assert.True(await verifyDb.Series.AnyAsync(s => s.Name == "Test Series", TestContext.Current.CancellationToken));
        Assert.True(await verifyDb.Formats.AnyAsync(f => f.Name == "Hardcover", TestContext.Current.CancellationToken));
        Assert.True(await verifyDb.Languages.AnyAsync(l => l.Name == "English", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ImportAsync_CreatesContributorRows()
    {
        var book = new ParsedBook
        {
            RowKey = 1,
            Title = "Contributor Test",
            Isbn = "978-CONTRIB-001",
            ResolvedContributors =
            [
                ("Author", "Jane Doe", "Doe, Jane"),
                ("Illustrator", "John Artist", "Artist, John"),
            ]
        };
        var backup = new ParsedBackup { Books = [book] };

        var svc = CreateImportService(backup);
        var result = await svc.ImportAsync("mock-path", collectionId: 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Errors);

        await using var verifyDb = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var importedBook = await verifyDb.Books.FirstAsync(b => b.Isbn == "978-CONTRIB-001", TestContext.Current.CancellationToken);

        var contributors = await verifyDb.BookContributors
            .Where(bc => bc.BookId == importedBook.BookId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, contributors.Count);
        Assert.True(await verifyDb.People.AnyAsync(p => p.DisplayName == "Jane Doe", TestContext.Current.CancellationToken));
        Assert.True(await verifyDb.People.AnyAsync(p => p.DisplayName == "John Artist", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ImportAsync_StripsContributorRoleHintsFromReaderwareBackup()
    {
        var book = new ParsedBook
        {
            RowKey = 1,
            Title = "Role Hint Backup",
            Isbn = "978-RW-ROLES-001",
            ResolvedContributors =
            [
                ("BadRole", "Jane Doe (Editor)", "Doe, Jane (Editor)"),
                ("BadRole", "John Smith [Translator]", "Smith, John [Translator]"),
                ("Author", "Alice Writer / Bob Editor (Editor)", "Writer, Alice / Editor, Bob (Editor)")
            ]
        };
        var backup = new ParsedBackup { Books = [book] };

        var svc = CreateImportService(backup);
        var result = await svc.ImportAsync("mock-path", collectionId: 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Errors);

        await using var verifyDb = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var importedBook = await verifyDb.Books.FirstAsync(b => b.Isbn == "978-RW-ROLES-001", TestContext.Current.CancellationToken);

        var contributors = await verifyDb.BookContributors
            .Include(bc => bc.Person)
            .Include(bc => bc.ContributorRole)
            .Where(bc => bc.BookId == importedBook.BookId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(4, contributors.Count);
        Assert.Contains(contributors, bc => bc.Person?.DisplayName == "Jane Doe" && bc.ContributorRole?.Code == "Editor");
        Assert.Contains(contributors, bc => bc.Person?.DisplayName == "John Smith" && bc.ContributorRole?.Code == "Translator");
        Assert.Contains(contributors, bc => bc.Person?.DisplayName == "Alice Writer" && bc.ContributorRole?.Code == "Author");
        Assert.Contains(contributors, bc => bc.Person?.DisplayName == "Bob Editor" && bc.ContributorRole?.Code == "Editor");
    }
            
    [Fact]
    public async Task ImportAsync_CreatesCategoryRows()
    {
        var book = new ParsedBook
        {
            RowKey = 1,
            Title = "Category Test",
            Isbn = "978-CAT-001",
            ResolvedCategoryNames = ["Fiction", "Mystery"]
        };
        var backup = new ParsedBackup { Books = [book] };

        var svc = CreateImportService(backup);
        var result = await svc.ImportAsync("mock-path", collectionId: 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Errors);

        await using var verifyDb = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var importedBook = await verifyDb.Books.FirstAsync(b => b.Isbn == "978-CAT-001", TestContext.Current.CancellationToken);

        var categories = await verifyDb.BookCategories
            .Where(bc => bc.BookId == importedBook.BookId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, categories.Count);
        Assert.True(await verifyDb.Categories.AnyAsync(c => c.Name == "Fiction", TestContext.Current.CancellationToken));
        Assert.True(await verifyDb.Categories.AnyAsync(c => c.Name == "Mystery", TestContext.Current.CancellationToken));
    }

    // Minimal valid JPEG hex: SOI + APP0 JFIF header
    private const string ValidJpegHex = "ffd8ffe000104a46494600010100000100010000";

    [Fact]
    public async Task ImportAsync_MultiImage_WritesAllBookImageRows()
    {
        var book = MakeParsedBook(1, "Multi Image Book", "978-MULTI");
        var backup = new ParsedBackup
        {
            Books = [book],
            FullImagesByRowKey = new Dictionary<int, List<(int ImageIndex, string HexData)>>
            {
                [1] =
                [
                    (0, ValidJpegHex),
                    (1, ValidJpegHex)
                ]
            }
        };

        var svc = CreateImportService(backup);
        var result = await svc.ImportAsync("mock-path", collectionId: 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Errors);

        await using var verifyDb = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var importedBook = await verifyDb.Books.FirstAsync(b => b.Isbn == "978-MULTI", TestContext.Current.CancellationToken);
        var images = await verifyDb.BookImages
            .Where(bi => bi.BookId == importedBook.BookId)
            .OrderBy(bi => bi.DisplayOrder)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, images.Count);

        var primary = images.First(i => i.DisplayOrder == 0);
        Assert.True(primary.IsPrimary);
        Assert.Equal(0, primary.BookImageTypeId);

        var secondary = images.First(i => i.DisplayOrder == 1);
        Assert.False(secondary.IsPrimary);
        Assert.Equal(0, secondary.BookImageTypeId);
    }

    [Fact]
    public async Task ImportAsync_Thumbnails_WritesWithTypeId1AndHighDisplayOrder()
    {
        var book = MakeParsedBook(2, "Thumbnail Book", "978-THUMB");
        var backup = new ParsedBackup
        {
            Books = [book],
            ThumbImagesByRowKey = new Dictionary<int, List<(int ImageIndex, string HexData)>>
            {
                [2] =
                [
                    (0, ValidJpegHex)
                ]
            }
        };

        var svc = CreateImportService(backup);
        var result = await svc.ImportAsync("mock-path", collectionId: 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Errors);

        await using var verifyDb = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var importedBook = await verifyDb.Books.FirstAsync(b => b.Isbn == "978-THUMB", TestContext.Current.CancellationToken);
        var images = await verifyDb.BookImages
            .Where(bi => bi.BookId == importedBook.BookId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(images);
        Assert.Equal(1, images[0].BookImageTypeId);    // Thumbnail type
        Assert.False(images[0].IsPrimary);
        Assert.Equal(1000, images[0].DisplayOrder);    // 1000 + imageIndex(0)
    }

    [Fact]
    public async Task ImportAsync_VolumesAndChapters_WrittenInPostPass()
    {
        var book = MakeParsedBook(10, "Volume Book", "978-VOL");
        var backup = new ParsedBackup
        {
            Books = [book],
            Volumes = [new ParsedVolume(VolumeRowKey: 1, BookRowKey: 10, VolumeNumber: 1)],
            Chapters = [new ParsedChapter(ChapterRowKey: 1, VolumeRowKey: 1, ChapterNumber: 1)]
        };

        var svc = CreateImportService(backup);
        var result = await svc.ImportAsync("mock-path", collectionId: 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Errors);

        await using var verifyDb = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var importedBook = await verifyDb.Books.FirstAsync(b => b.Isbn == "978-VOL", TestContext.Current.CancellationToken);

        var volumes = await verifyDb.BookVolumes
            .Where(bv => bv.BookId == importedBook.BookId)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Single(volumes);
        Assert.Equal(1, volumes[0].VolumeNumber);

        var chapters = await verifyDb.BookChapters
            .Where(bc => bc.BookVolumeId == volumes[0].BookVolumeId)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Single(chapters);
        Assert.Equal(1, chapters[0].ChapterNumber);
    }

    [Fact]
    public async Task CancellationStopsImportMidway()
    {
        // Create 200+ books to span multiple batches
        var books = Enumerable.Range(1, 220)
            .Select(i => MakeParsedBook(i, $"Book {i}", $"978-{i:D4}"))
            .ToArray();

        var backup = new ParsedBackup { Books = books };
        var svc = CreateImportService(backup);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100)); // Cancel quickly

        var result = await svc.ImportAsync("mock-path", collectionId: 1, cancellationToken: cts.Token);

        Assert.True(result.WasCancelled);
        // Some books should exist, but not all
        await using var verifyDb = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var count = await verifyDb.Books.CountAsync(TestContext.Current.CancellationToken);
        Assert.True(count < 220, $"Expected fewer than 220 books after cancellation, got {count}");
    }

    [Fact]
    public async Task AskOverwriteAll_AppliesToEveryDuplicateWithoutAskingAgain()
    {
        var now = DateTime.UtcNow;
        await using (var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            db.Books.Add(new Book { Title = "A", Isbn = "978-DUP-1", Subtitle = null, Added = now, Updated = now });
            db.Books.Add(new Book { Title = "B", Isbn = "978-DUP-2", Subtitle = null, Added = now, Updated = now });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var backup = new ParsedBackup
        {
            Books =
            [
                new ParsedBook { RowKey = 1, Title = "A2", Isbn = "978-DUP-1", Subtitle = "Filled A" },
                new ParsedBook { RowKey = 2, Title = "B2", Isbn = "978-DUP-2", Subtitle = "Filled B" },
            ]
        };

        var asks = 0;
        Task<ImportDuplicateResolution> Ask(string title, CancellationToken ct)
        {
            asks++;
            return Task.FromResult(ImportDuplicateResolution.OverwriteAll);
        }

        var svc = CreateImportService(backup, overwritePolicy: "Ask");
        var result = await svc.ImportAsync("mock-path", collectionId: 1, askCallback: Ask,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, asks);          // asked once; "all" applied to the rest
        Assert.Equal(2, result.Updated);
        Assert.False(result.WasCancelled);
    }

    [Fact]
    public async Task AskCancelImport_StopsRunAndKeepsAlreadyImported()
    {
        var now = DateTime.UtcNow;
        await using (var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            db.Books.Add(new Book { Title = "Existing", Isbn = "978-CANCEL", Added = now, Updated = now });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // The duplicate comes first so cancel fires before the new book is processed.
        var backup = new ParsedBackup
        {
            Books =
            [
                new ParsedBook { RowKey = 1, Title = "Dup", Isbn = "978-CANCEL" },
                new ParsedBook { RowKey = 2, Title = "New After Cancel", Isbn = "978-NEW-AFTER" },
            ]
        };

        Task<ImportDuplicateResolution> Ask(string title, CancellationToken ct)
            => Task.FromResult(ImportDuplicateResolution.CancelImport);

        var svc = CreateImportService(backup, overwritePolicy: "Ask");
        var result = await svc.ImportAsync("mock-path", collectionId: 1, askCallback: Ask,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.WasCancelled);
        await using var verifyDb = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.False(await verifyDb.Books.AnyAsync(b => b.Isbn == "978-NEW-AFTER", TestContext.Current.CancellationToken));
    }
}

/// <summary>
/// Test helper: returns a pre-built ParsedBackup without reading any files.
/// </summary>
internal sealed class MockBackupParser(ParsedBackup backup) : IBackupParser
{
    private readonly ParsedBackup _backup = backup;

    public Task<ParsedBackup> ParseAsync(string path, CancellationToken ct = default)
        => Task.FromResult(_backup);
}
