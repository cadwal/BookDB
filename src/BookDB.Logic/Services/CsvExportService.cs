using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Models;
using BookDB.Models.Entities;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace BookDB.Logic.Services;

public sealed class CsvExportService : ICsvExportService
{
    private readonly IDbContextFactory<BookDbContext> _factory;

    public IReadOnlyList<string> AllColumnNames { get; } =
    [
        "Title", "Subtitle", "AltTitle", "Authors", "Series", "SeriesNumber",
        "ISBN", "Publisher", "PubDate", "PubPlace", "Edition", "Format",
        "Collection", "Language", "Pages", "Copies", "Rating", "Status", "Condition",
        "Location", "Owner", "ReadCount", "Signed", "OutOfPrint", "Favorite",
        "Keywords", "Comments", "PurchasePrice", "PurchaseCurrency", "Added", "Updated"
    ];

    public IReadOnlyList<string> DefaultColumnNames { get; } =
    [
        "Title", "Authors", "Series", "SeriesNumber", "ISBN",
        "Publisher", "PubDate", "Format", "Collection", "Language",
        "Rating", "Status"
    ];

    public CsvExportService(IDbContextFactory<BookDbContext> factory)
    {
        _factory = factory;
    }

    public async Task ExportAsync(CsvExportParameters parameters, CancellationToken ct = default, IProgress<ProgressUpdate<CsvExportProgressStep>>? progress = null)
    {
        try
        {
            progress?.Report(new ProgressUpdate<CsvExportProgressStep>(CsvExportProgressStep.Querying));
            await using var dbContext = await _factory.CreateDbContextAsync(ct);

            IQueryable<Book> query = dbContext.Books.AsNoTracking()
                .Include(b => b.Publisher)
                .Include(b => b.Format)
                .Include(b => b.Edition)
                .Include(b => b.Language)
                .Include(b => b.Collection)
                .Include(b => b.Series)
                .Include(b => b.Rating)
                .Include(b => b.Status)
                .Include(b => b.Condition)
                .Include(b => b.Location)
                .Include(b => b.Owner)
                .Include(b => b.Contributors)
                    .ThenInclude(c => c.Person)
                .Include(b => b.Contributors)
                    .ThenInclude(c => c.ContributorRole);

            // Apply collection filter
            if (parameters.CollectionIds is { Count: > 0 })
                query = query.Where(CollectionFilter.Predicate(parameters.CollectionIds));

            // Apply search book IDs filter (empty but non-null = search matched nothing → export zero rows)
            if (parameters.SearchBookIds != null)
                query = query.Where(b => parameters.SearchBookIds.Contains(b.BookId));

            // Apply facet filters
            if (parameters.FacetFilters is { Count: > 0 })
            {
                foreach (var (key, ids) in parameters.FacetFilters)
                {
                    if (ids.Count == 0) continue;
                    query = key switch
                    {
                        "Format"    => query.Where(b => b.FormatId != null && ids.Contains(b.FormatId.Value)),
                        "Series"    => query.Where(b => b.SeriesId != null && ids.Contains(b.SeriesId.Value)),
                        "Publisher" => query.Where(b => b.PublisherId != null && ids.Contains(b.PublisherId.Value)),
                        "Language"  => query.Where(b => b.LanguageId != null && ids.Contains(b.LanguageId.Value)),
                        "Rating"    => query.Where(b => b.RatingId != null && ids.Contains(b.RatingId.Value)),
                        "Status"    => query.Where(b => b.StatusId != null && ids.Contains(b.StatusId.Value)),
                        "Location"  => query.Where(b => b.LocationId != null && ids.Contains(b.LocationId.Value)),
                        "Owner"     => query.Where(b => b.OwnerId != null && ids.Contains(b.OwnerId.Value)),
                        "Author"    => query.Where(b => b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author" && ids.Contains(c.PersonId))),
                        "Category"  => query.Where(b => b.Categories.Any(c => ids.Contains(c.CategoryId))),
                        _           => query
                    };
                }
            }

            // Apply sort — unbounded, no Take() cap
            query = (parameters.SortColumn, parameters.SortAscending) switch
            {
                ("Title", true)          => query.OrderBy(b => b.Title),
                ("Title", false)         => query.OrderByDescending(b => b.Title),
                ("SeriesNumber", true)   => query.OrderBy(b => b.SeriesNumber),
                ("SeriesNumber", false)  => query.OrderByDescending(b => b.SeriesNumber),
                ("Year", true)           => query.OrderBy(b => b.PubDate),
                ("Year", false)          => query.OrderByDescending(b => b.PubDate),
                _                        => query.OrderBy(b => b.Title)
            };

            var books = await query.ToListAsync(ct);
            progress?.Report(new ProgressUpdate<CsvExportProgressStep>(CsvExportProgressStep.WritingBooks, books.Count));

            var selectedColumns = parameters.SelectedColumns.Count > 0
                ? parameters.SelectedColumns
                : AllColumnNames;

            await WriteExportAsync(parameters.OutputPath, books, selectedColumns, progress, ct);

            Log.Information("CsvExportService: exported {Count} books to {OutputPath}", books.Count, parameters.OutputPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CsvExportService: ExportAsync failed");
            throw;
        }
    }

    private async Task WriteExportAsync(
        string outputPath,
        IReadOnlyList<Book> books,
        IReadOnlyList<string> selectedColumns,
        IProgress<ProgressUpdate<CsvExportProgressStep>>? progress,
        CancellationToken ct)
    {
        await using var writer = new StreamWriter(outputPath, false, System.Text.Encoding.UTF8);
        await using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));

        foreach (var col in selectedColumns)
            csv.WriteField(col);
        await csv.NextRecordAsync();

        for (var i = 0; i < books.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (i % 100 == 0)
                progress?.Report(new ProgressUpdate<CsvExportProgressStep>(CsvExportProgressStep.WritingRow, i + 1, books.Count));

            var book = books[i];
            var authors = string.Join("; ", book.Contributors
                .Where(c => c.ContributorRole?.Code == "Author" && c.Person != null)
                .Select(c => c.Person!.DisplayName));

            var row = BuildRow(book, authors);
            foreach (var col in selectedColumns)
                csv.WriteField(row.TryGetValue(col, out var val) ? val : string.Empty);
            await csv.NextRecordAsync();
        }
    }

    private static Dictionary<string, string> BuildRow(Book book, string authors) => new()
    {
        ["Title"]            = book.Title ?? string.Empty,
        ["Subtitle"]         = book.Subtitle ?? string.Empty,
        ["AltTitle"]         = book.AltTitle ?? string.Empty,
        ["Authors"]          = authors,
        ["Series"]           = book.Series?.Name ?? string.Empty,
        ["SeriesNumber"]     = book.SeriesNumber ?? string.Empty,
        ["ISBN"]             = book.Isbn ?? string.Empty,
        ["Publisher"]        = book.Publisher?.Name ?? string.Empty,
        ["PubDate"]          = book.PubDate ?? string.Empty,
        ["PubPlace"]         = book.PubPlace ?? string.Empty,
        ["Edition"]          = book.Edition?.Name ?? string.Empty,
        ["Format"]           = book.Format?.Name ?? string.Empty,
        ["Language"]         = book.Language?.Name ?? string.Empty,
        ["Pages"]            = book.Pages?.ToString() ?? string.Empty,
        ["Copies"]           = book.Copies.ToString(),
        ["Rating"]           = book.Rating?.Name ?? string.Empty,
        ["Status"]           = book.Status?.Name ?? string.Empty,
        ["Condition"]        = book.Condition?.Name ?? string.Empty,
        ["Location"]         = book.Location?.Name ?? string.Empty,
        ["Owner"]            = book.Owner?.Name ?? string.Empty,
        ["ReadCount"]        = book.ReadCount.ToString(),
        ["Signed"]           = book.Signed.ToString(),
        ["OutOfPrint"]       = book.OutOfPrint.ToString(),
        ["Favorite"]         = book.Favorite.ToString(),
        ["Keywords"]         = book.Keywords ?? string.Empty,
        ["Comments"]         = book.Comments ?? string.Empty,
        ["PurchasePrice"]    = book.PurchasePrice?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        ["PurchaseCurrency"] = book.PurchaseCurrency ?? string.Empty,
        ["Added"]            = book.Added.ToString("yyyy-MM-dd"),
        ["Updated"]          = book.Updated.ToString("yyyy-MM-dd"),
        ["Collection"]       = book.Collection?.Name ?? string.Empty,
    };
}
