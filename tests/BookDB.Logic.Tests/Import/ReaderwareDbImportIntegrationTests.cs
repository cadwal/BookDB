using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Logic.Import;
using BookDB.Logic.Services;
using DbUp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookDB.Logic.Tests.Import;

/// <summary>
/// End-to-end test of the Readerware live-DB import path: synthetic DSV (as SqlTool's <c>\x</c> emits)
/// → the real <see cref="ReaderwareDbExportService.ConvertDsvToBackupFile"/> → the real
/// <see cref="ReaderwareBackupParser"/> → the real <see cref="ImportService"/> → a temp SQLite database.
/// This proves the converter's output format flows through the importer unchanged, with lookups and
/// images resolved.
/// </summary>
public sealed class ReaderwareDbImportIntegrationTests : IDisposable
{
    private const string Col = ReaderwareDbExportService.ColumnDelimiter;
    private const string Row = ReaderwareDbExportService.RowDelimiter;

    // Minimal valid JPEG hex (SOI + APP0 JFIF header) — the form VARBINARY exports as.
    private const string JpegHex = "ffd8ffe000104a46494600010100000100010000";

    private readonly string _dbPath;
    private readonly string _backupDir;
    private readonly TestBookDbContextFactory _factory;

    public ReaderwareDbImportIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_rwint_{Guid.NewGuid():N}.db");
        _backupDir = Path.Combine(Path.GetTempPath(), $"bookdb_rwint_backup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_backupDir);

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

        WriteConvertedBackup();
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
        try { Directory.Delete(_backupDir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Build a backup folder exactly as the export service would: DSV in, UTF-16BE CSV out.</summary>
    private void WriteConvertedBackup()
    {
        void Convert(string table, string dsv)
            => ReaderwareDbExportService.ConvertDsvToBackupFile(dsv, Path.Combine(_backupDir, table));

        Convert("DBCATALOG40", $"ROWKEY{Col}EV{Row}");
        Convert("PUBLISHER_LIST", $"ROWKEY{Col}LISTITEM{Row}5{Col}Penguin{Row}");
        Convert("FORMAT_LIST", $"ROWKEY{Col}LISTITEM{Row}3{Col}Hardcover{Row}");
        Convert("CATEGORY_LIST", $"ROWKEY{Col}LISTITEM{Row}7{Col}Fiction{Row}");
        Convert("CONTRIBUTOR", $"ROWKEY{Col}NAME{Col}SORT_NAME{Row}2{Col}Jane Doe{Col}Doe, Jane{Row}");
        Convert("FULL_IMAGES", $"ROW_ID{Col}IMAGE_INDEX{Col}IMAGE_DATA{Row}1{Col}0{Col}{JpegHex}{Row}");

        // The book table: only the columns we populate; the parser tolerates the rest being absent.
        var reader =
            $"ROWKEY{Col}TITLE{Col}ISBN{Col}AUTHOR{Col}PUBLISHER{Col}FORMAT{Col}CATEGORY1{Col}FULL_IMAGE_COUNT{Row}" +
            $"1{Col}The Great Book{Col}978-INT-001{Col}2{Col}5{Col}3{Col}7{Col}1{Row}" +
            $"2{Col}Second Book{Col}978-INT-002{Col}-1{Col}-1{Col}-1{Col}-1{Col}0{Row}";
        Convert("READERWARE", reader);
    }

    private ImportService CreateImportService()
        => new(_factory, new ReaderwareBackupParser(), new SkipSettings(), NullLogger<ImportService>.Instance);

    [Fact]
    public async Task PreviewReportsConvertedBackupCountsWithoutWarnings()
    {
        var svc = CreateImportService();
        var preview = await svc.PreviewAsync(_backupDir, collectionId: 1, TestContext.Current.CancellationToken);

        Assert.Equal(2, preview.TotalRecords);
        Assert.Equal(2, preview.RecordsWithIsbn);
        Assert.Equal(1, preview.RecordsWithCovers);
        Assert.Empty(preview.Warnings);

        // The first row resolves its author from the CONTRIBUTOR file and reports a cover.
        var first = preview.SampleRows.First(r => r.Title == "The Great Book");
        Assert.Equal("Jane Doe", first.AuthorDisplay);
        Assert.True(first.HasCover);
    }

    [Fact]
    public async Task ImportPersistsBooksLookupsContributorAndCover()
    {
        var svc = CreateImportService();
        var result = await svc.ImportAsync(_backupDir, collectionId: 1,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Empty(result.Errors);
        Assert.False(result.WasCancelled);

        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);

        var book = await db.Books.FirstAsync(b => b.Isbn == "978-INT-001", TestContext.Current.CancellationToken);
        Assert.Equal("The Great Book", book.Title);
        Assert.NotNull(book.PublisherId);
        Assert.NotNull(book.FormatId);

        Assert.True(await db.Publishers.AnyAsync(p => p.Name == "Penguin", TestContext.Current.CancellationToken));
        Assert.True(await db.Formats.AnyAsync(f => f.Name == "Hardcover", TestContext.Current.CancellationToken));
        Assert.True(await db.Categories.AnyAsync(c => c.Name == "Fiction", TestContext.Current.CancellationToken));
        Assert.True(await db.People.AnyAsync(p => p.DisplayName == "Jane Doe", TestContext.Current.CancellationToken));

        // The full image (exported as hex, transcoded, parsed) became a primary BookImage.
        var images = await db.BookImages
            .Where(bi => bi.BookId == book.BookId)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Single(images);
        Assert.True(images[0].IsPrimary);
    }

    /// <summary>Fixed "Skip" overwrite policy.</summary>
    private sealed class SkipSettings : ISettingsService
    {
        public Task<string?> GetAsync(string key, CancellationToken ct = default)
            => Task.FromResult<string?>(key == "Import.OverwritePolicy" ? "Skip" : null);
        public Task SetAsync(string key, string? value, CancellationToken ct = default) => Task.CompletedTask;
    }
}
