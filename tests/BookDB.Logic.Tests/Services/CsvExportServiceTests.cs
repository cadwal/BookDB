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

public sealed class CsvExportServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TestBookDbContextFactory _factory;
    private readonly CsvExportService _sut;
    private readonly string _tempWorkDir;

    public CsvExportServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_csvexport_test_{Guid.NewGuid():N}.db");
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
        _sut = new CsvExportService(_factory);

        _tempWorkDir = Path.Combine(Path.GetTempPath(), $"bookdb_csvexport_workdir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempWorkDir);
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
        try { Directory.Delete(_tempWorkDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task ExportAsync_ExportsAllRows_NoCap()
    {
        var ct = TestContext.Current.CancellationToken;

        // Insert 1002 books — more than the 1000-row cap in BookService.GetBooksAsync
        using (var db = _factory.CreateDbContext())
        {
            var now = DateTime.UtcNow;
            var books = Enumerable.Range(1, 1002)
                .Select(i => new Book { Title = $"Book {i:D4}", Added = now, Updated = now })
                .ToList();
            db.Books.AddRange(books);
            db.SaveChanges();
        }

        var outputPath = Path.Combine(_tempWorkDir, "export.csv");
        var parameters = new CsvExportParameters(
            OutputPath: outputPath,
            SelectedColumns: new[] { "Title" },
            CollectionIds: null,
            SearchBookIds: null,
            FacetFilters: null,
            SortColumn: "Title",
            SortAscending: true);

        await _sut.ExportAsync(parameters, ct);

        Assert.True(File.Exists(outputPath), "Output CSV file should exist");

        // Count data rows (file has 1 header row + 1002 data rows)
        var lines = await File.ReadAllLinesAsync(outputPath, ct);
        var dataRowCount = lines.Length - 1; // subtract header
        Assert.Equal(1002, dataRowCount);
    }

    [Fact]
    public async Task ExportAsync_ReportsTypedProgressSteps()
    {
        var ct = TestContext.Current.CancellationToken;

        // 150 books → row reports fire at i=0 and i=100 (every 100th row).
        using (var db = _factory.CreateDbContext())
        {
            var now = DateTime.UtcNow;
            db.Books.AddRange(Enumerable.Range(1, 150)
                .Select(i => new Book { Title = $"Book {i:D4}", Added = now, Updated = now }));
            db.SaveChanges();
        }

        var parameters = new CsvExportParameters(
            OutputPath: Path.Combine(_tempWorkDir, "export.csv"),
            SelectedColumns: new[] { "Title" },
            CollectionIds: null,
            SearchBookIds: null,
            FacetFilters: null,
            SortColumn: "Title",
            SortAscending: true);

        var reports = new List<ProgressUpdate<CsvExportProgressStep>>();
        await _sut.ExportAsync(parameters, ct, new CollectingProgress(reports));

        Assert.Equal(CsvExportProgressStep.Querying, reports[0].Step);

        var writingBooks = Assert.Single(reports, r => r.Step == CsvExportProgressStep.WritingBooks);
        Assert.Equal(150, writingBooks.Current);

        var rowReports = reports.Where(r => r.Step == CsvExportProgressStep.WritingRow).ToList();
        Assert.Equal(2, rowReports.Count);
        Assert.All(rowReports, r => Assert.Equal(150, r.Total));
        Assert.Equal(new[] { 1, 101 }, rowReports.Select(r => r.Current));
    }

    private sealed class CollectingProgress : IProgress<ProgressUpdate<CsvExportProgressStep>>
    {
        private readonly List<ProgressUpdate<CsvExportProgressStep>> _reports;
        public CollectingProgress(List<ProgressUpdate<CsvExportProgressStep>> reports) => _reports = reports;
        public void Report(ProgressUpdate<CsvExportProgressStep> value) => _reports.Add(value);
    }
}
