using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Models.Entities;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Serilog;

namespace BookDB.Logic.Services;

public sealed class PrintService : IPrintService
{
    private readonly IDbContextFactory<BookDbContext> _factory;
    private readonly IResourceProvider _resourceProvider;

    public IReadOnlyList<string> AllColumnNames { get; } = new[]
    {
        "Title", "Subtitle", "AltTitle", "Authors", "Series", "SeriesNumber",
        "ISBN", "Publisher", "PubDate", "PubPlace", "Edition", "Format",
        "Collection", "Language", "Pages", "Copies", "Rating", "Status", "Condition",
        "Location", "Owner", "ReadCount", "Signed", "OutOfPrint", "Favorite",
        "Keywords", "Comments", "PurchasePrice", "PurchaseCurrency", "Added", "Updated"
    };

    public IReadOnlyList<string> DefaultColumnNames { get; } = new[]
    {
        "Title", "Authors", "Series", "PubDate", "Format", "Location"
    };

    public PrintService(IDbContextFactory<BookDbContext> factory, IResourceProvider resourceProvider)
    {
        _factory = factory;
        _resourceProvider = resourceProvider;
    }

    public void InitializeLicense()
    {
        // QuestPDF Community license — must be set before any Document.Create() call.
        // Called from AppHost.StartAsync() via IPrintService to keep QuestPDF confined to BookDB.Logic.
        global::QuestPDF.Settings.License = global::QuestPDF.Infrastructure.LicenseType.Community;
    }

    public async Task GenerateAsync(PrintParameters parameters, CancellationToken ct = default, IProgress<string>? progress = null)
    {
        try
        {
            // Security: validate column keys against whitelist before using in PDF
            var allColumnSet = new HashSet<string>(AllColumnNames, StringComparer.Ordinal);
            var validatedColumns = parameters.Preset.Columns
                .Where(col => allColumnSet.Contains(col))
                .ToList();

            if (validatedColumns.Count == 0)
                validatedColumns = parameters.Preset.Columns.Count == 0
                    ? new List<string>(AllColumnNames)
                    : new List<string>(DefaultColumnNames);

            progress?.Report("Querying books..."); // internal diagnostic — never rendered in UI (callers pass null)
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
                query = query.Where(b => b.CollectionId == null || parameters.CollectionIds.Contains(b.CollectionId.Value));

            // Apply search book IDs filter (empty but non-null = search matched nothing → print zero rows)
            if (parameters.SearchBookIds != null)
                query = query.Where(b => parameters.SearchBookIds.Contains(b.BookId));

            // Apply facet filters — copy verbatim from CsvExportService
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

            // Apply sort — unbounded (no Take() cap)
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
            progress?.Report($"Generating PDF for {books.Count:N0} books..."); // internal diagnostic — never rendered in UI (callers pass null)

            await Task.Run(() => GeneratePdf(parameters, validatedColumns, books), ct);

            Log.Information("PrintService: generated PDF for {Count} books at {OutputPath}", books.Count, parameters.OutputPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PrintService: GenerateAsync failed");
            throw;
        }
    }

    private void GeneratePdf(PrintParameters parameters, IReadOnlyList<string> columns, IReadOnlyList<Book> books)
    {
        var preset = parameters.Preset;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(preset.Orientation == PageOrientation.Portrait
                    ? PageSizes.A4.Portrait()
                    : PageSizes.A4.Landscape());
                page.MarginHorizontal(preset.MarginHorizontalMm, Unit.Millimetre);
                page.MarginVertical(preset.MarginVerticalMm, Unit.Millimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(style => style.FontSize(preset.FontSize));

                // Header: custom text left, generation date right
                page.Header()
                    .Row(row =>
                    {
                        row.RelativeItem()
                           .Text(preset.HeaderText)
                           .SemiBold();
                        row.ConstantItem(120)
                           .AlignRight()
                           .Text(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                    });

                // Table body with dynamic columns
                page.Content()
                    .PaddingVertical(4)
                    .Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            foreach (var _ in columns)
                                cols.RelativeColumn();
                        });

                        table.Header(header =>
                        {
                            foreach (var col in columns)
                            {
                                var label = _resourceProvider.GetString("Print_Column_" + col) ?? col;
                                header.Cell()
                                      .BorderBottom(1)
                                      .Padding(4)
                                      .Text(label)
                                      .SemiBold();
                            }
                        });

                        foreach (var book in books)
                        {
                            var row = BuildRow(book);
                            foreach (var col in columns)
                            {
                                table.Cell()
                                     .BorderBottom(0.5f)
                                     .Padding(3)
                                     .Text(row.TryGetValue(col, out var val) ? val : string.Empty);
                            }
                        }
                    });

                // Footer: optional custom text + localised page numbers
                page.Footer()
                    .AlignCenter()
                    .Text(textDescriptor =>
                    {
                        if (!string.IsNullOrWhiteSpace(preset.FooterText))
                            textDescriptor.Span(preset.FooterText + "   ");

                        // Parse localised "Page {0} of {1}" (e.g. SV: "Sida {0} av {1}") into prefix / middle / suffix
                        var fmt = _resourceProvider.GetString("Print_Footer_PageFormat") ?? "Page {0} of {1}";
                        var parts = fmt.Split(["{0}", "{1}"], StringSplitOptions.None);
                        if (parts.Length == 3)
                        {
                            if (parts[0].Length > 0) textDescriptor.Span(parts[0]);
                            textDescriptor.CurrentPageNumber();
                            if (parts[1].Length > 0) textDescriptor.Span(parts[1]);
                            textDescriptor.TotalPages();
                            if (parts[2].Length > 0) textDescriptor.Span(parts[2]);
                        }
                        else
                        {
                            textDescriptor.Span("Page ");
                            textDescriptor.CurrentPageNumber();
                            textDescriptor.Span(" of ");
                            textDescriptor.TotalPages();
                        }
                    });
            });
        }).GeneratePdf(parameters.OutputPath);
    }

    private static Dictionary<string, string> BuildRow(Book book)
    {
        var authors = string.Join("; ", book.Contributors
            .Where(c => c.ContributorRole?.Code == "Author" && c.Person != null)
            .Select(c => c.Person!.DisplayName));

        return new Dictionary<string, string>
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
}
