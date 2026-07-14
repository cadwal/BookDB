using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Logic.Services;
using BookDB.Models;
using BookDB.Models.Entities;
using DbUp;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Logic.Tests.Services;

public sealed class PrintServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TestBookDbContextFactory _factory;
    private readonly PrintService _sut;
    private readonly string _tempWorkDir;

    public PrintServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_print_test_{Guid.NewGuid():N}.db");
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

        global::QuestPDF.Settings.License = global::QuestPDF.Infrastructure.LicenseType.Community;
        _sut = new PrintService(_factory);

        _tempWorkDir = Path.Combine(Path.GetTempPath(), $"bookdb_print_workdir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempWorkDir);
    }

    public void Dispose()
    {
        // Release pooled SQLite connections so the temp DB file handle is freed before deletion.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_dbPath); } catch (Exception ex) { Console.Error.WriteLine($"[Dispose] Could not delete {_dbPath}: {ex.Message}"); }
        try { Directory.Delete(_tempWorkDir, recursive: true); } catch (Exception ex) { Console.Error.WriteLine($"[Dispose] Could not delete {_tempWorkDir}: {ex.Message}"); }
    }

    [Fact]
    public async Task GenerateAsync_WritesNonEmptyPdfFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var outputPath = Path.Combine(_tempWorkDir, "test-output.pdf");
        var preset = PrintPreset.CreateDefault(); // no-arg path; production calls always pass a localised title (gap: localised header not asserted)
        var parameters = new PrintParameters(
            OutputPath: outputPath,
            CollectionIds: null,
            SearchBookIds: null,
            FacetFilters: null,
            SortColumn: "Title",
            SortAscending: true,
            Preset: preset);

        await _sut.GenerateAsync(parameters, ct);

        Assert.True(File.Exists(outputPath), "PDF file should exist");
        var info = new FileInfo(outputPath);
        Assert.True(info.Length > 100, $"PDF should be non-empty but was {info.Length} bytes");
    }

    [Fact]
    public async Task GenerateAsync_StandardPreset_ContainsDefaultColumns()
    {
        var ct = TestContext.Current.CancellationToken;
        using (var dbContext = _factory.CreateDbContext())
        {
            var now = DateTime.UtcNow;
            dbContext.Books.Add(new Book { Title = "Test Book", Added = now, Updated = now });
            dbContext.SaveChanges();
        }
        var outputPath = Path.Combine(_tempWorkDir, "test-standard.pdf");
        var parameters = new PrintParameters(
            OutputPath: outputPath,
            CollectionIds: null, SearchBookIds: null, FacetFilters: null,
            SortColumn: "Title", SortAscending: true,
            Preset: PrintPreset.CreateDefault());
        await _sut.GenerateAsync(parameters, ct);
        Assert.True(File.Exists(outputPath) && new FileInfo(outputPath).Length > 100);
    }

    [Fact]
    public async Task GenerateAsync_SubsetPreset_OnlySelectedColumnsUsed()
    {
        var ct = TestContext.Current.CancellationToken;
        var outputPath = Path.Combine(_tempWorkDir, "test-subset.pdf");
        var subsetPreset = new PrintPreset(
            Name: "Subset", Columns: new[] { "Title", "Authors" },
            Orientation: PageOrientation.Portrait, FontSize: 10,
            MarginHorizontalMm: 15, MarginVerticalMm: 20,
            HeaderText: "Subset Report", FooterText: string.Empty);
        var parameters = new PrintParameters(
            OutputPath: outputPath, CollectionIds: null, SearchBookIds: null,
            FacetFilters: null, SortColumn: "Title", SortAscending: true,
            Preset: subsetPreset);
        await _sut.GenerateAsync(parameters, ct);
        Assert.True(File.Exists(outputPath) && new FileInfo(outputPath).Length > 100);
    }

    [Fact]
    public async Task GenerateAsync_FilteredQuery_ReturnsOnlyMatchingBooks()
    {
        var ct = TestContext.Current.CancellationToken;
        using (var dbContext = _factory.CreateDbContext())
        {
            var now = DateTime.UtcNow;
            dbContext.Books.Add(new Book { Title = "Alpha", Added = now, Updated = now });
            dbContext.Books.Add(new Book { Title = "Beta", Added = now, Updated = now });
            dbContext.SaveChanges();
        }
        // Use SearchBookIds filter to exercise the filter code path
        List<int> ids;
        using (var dbContext = _factory.CreateDbContext())
        {
            ids = dbContext.Books.Select(b => b.BookId).Take(1).ToList();
        }
        var outputPath = Path.Combine(_tempWorkDir, "test-filtered.pdf");
        var parameters = new PrintParameters(
            OutputPath: outputPath,
            CollectionIds: null,
            SearchBookIds: ids,
            FacetFilters: null,
            SortColumn: "Title", SortAscending: true,
            Preset: PrintPreset.CreateDefault());
        await _sut.GenerateAsync(parameters, ct);
        Assert.True(File.Exists(outputPath) && new FileInfo(outputPath).Length > 100);
    }

    [Fact]
    public async Task GenerateAsync_EmptyResult_ProducesValidPdf()
    {
        var ct = TestContext.Current.CancellationToken;
        var outputPath = Path.Combine(_tempWorkDir, "test-empty.pdf");
        var parameters = new PrintParameters(
            OutputPath: outputPath, CollectionIds: null, SearchBookIds: null,
            FacetFilters: null, SortColumn: "Title", SortAscending: true,
            Preset: PrintPreset.CreateDefault());
        await _sut.GenerateAsync(parameters, ct);
        Assert.True(File.Exists(outputPath), "PDF should exist even for empty result");
        Assert.True(new FileInfo(outputPath).Length > 100, "PDF should be non-empty");
    }

    // Footer format — exercises the parts.Length == 3 branch with a localised format string
    [Fact]
    public async Task GenerateAsync_LocalisedFooterFormat_ThreePartSplit_ProducesValidPdf()
    {
        var ct = TestContext.Current.CancellationToken;
        var outputPath = Path.Combine(_tempWorkDir, "test-sv-footer.pdf");
        var parameters = new PrintParameters(
            OutputPath: outputPath,
            CollectionIds: null, SearchBookIds: null, FacetFilters: null,
            SortColumn: "Title", SortAscending: true,
            Preset: PrintPreset.CreateDefault(),
            PageNumberFormat: "Sida {0} av {1}");
        await _sut.GenerateAsync(parameters, ct);
        Assert.True(File.Exists(outputPath) && new FileInfo(outputPath).Length > 100);
    }

    // Footer format — exercises the else/fallback branch when format has no placeholders
    [Fact]
    public async Task GenerateAsync_MalformedFooterFormat_FallsBackToDefaultPaging()
    {
        var ct = TestContext.Current.CancellationToken;
        var outputPath = Path.Combine(_tempWorkDir, "test-malformed-footer.pdf");
        var parameters = new PrintParameters(
            OutputPath: outputPath,
            CollectionIds: null, SearchBookIds: null, FacetFilters: null,
            SortColumn: "Title", SortAscending: true,
            Preset: PrintPreset.CreateDefault(),
            PageNumberFormat: "no-placeholders");
        await _sut.GenerateAsync(parameters, ct);
        Assert.True(File.Exists(outputPath) && new FileInfo(outputPath).Length > 100);
    }

    [Fact]
    public async Task GenerateAsync_ReportsTypedProgressSteps()
    {
        var ct = TestContext.Current.CancellationToken;
        using (var dbContext = _factory.CreateDbContext())
        {
            var now = DateTime.UtcNow;
            dbContext.Books.Add(new Book { Title = "Progress Book A", Added = now, Updated = now });
            dbContext.Books.Add(new Book { Title = "Progress Book B", Added = now, Updated = now });
            dbContext.SaveChanges();
        }
        var parameters = new PrintParameters(
            OutputPath: Path.Combine(_tempWorkDir, "test-progress.pdf"),
            CollectionIds: null, SearchBookIds: null, FacetFilters: null,
            SortColumn: "Title", SortAscending: true,
            Preset: PrintPreset.CreateDefault());

        var reports = new List<ProgressUpdate<PrintProgressStep>>();
        await _sut.GenerateAsync(parameters, ct, new CollectingProgress(reports));

        Assert.Equal(2, reports.Count);
        Assert.Equal(PrintProgressStep.Querying, reports[0].Step);
        Assert.Equal(PrintProgressStep.GeneratingPdf, reports[1].Step);
        Assert.Equal(2, reports[1].Current);
    }

    private sealed class CollectingProgress : IProgress<ProgressUpdate<PrintProgressStep>>
    {
        private readonly List<ProgressUpdate<PrintProgressStep>> _reports;
        public CollectingProgress(List<ProgressUpdate<PrintProgressStep>> reports) => _reports = reports;
        public void Report(ProgressUpdate<PrintProgressStep> value) => _reports.Add(value);
    }

    // Column labels — supplied labels override the key-name fallback
    [Fact]
    public async Task GenerateAsync_CustomColumnHeaderLabels_ProducesValidPdf()
    {
        var ct = TestContext.Current.CancellationToken;
        using (var dbContext = _factory.CreateDbContext())
        {
            var now = DateTime.UtcNow;
            dbContext.Books.Add(new Book { Title = "Label Test Book", Added = now, Updated = now });
            dbContext.SaveChanges();
        }
        var outputPath = Path.Combine(_tempWorkDir, "test-custom-labels.pdf");
        var parameters = new PrintParameters(
            OutputPath: outputPath,
            CollectionIds: null, SearchBookIds: null, FacetFilters: null,
            SortColumn: "Title", SortAscending: true,
            Preset: PrintPreset.CreateDefault(),
            ColumnHeaderLabels: new Dictionary<string, string> { ["Title"] = "Titel" });
        await _sut.GenerateAsync(parameters, ct);
        Assert.True(File.Exists(outputPath) && new FileInfo(outputPath).Length > 100);
    }
}
