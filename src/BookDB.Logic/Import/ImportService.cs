using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Logic.Helpers;
using BookDB.Logic.Messages;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookDB.Logic.Import;

/// <summary>
/// Orchestrates dry-run preview and full import of Readerware backup data.
/// Processes records in batches of 100 using fresh DbContext per batch.
/// </summary>
public sealed class ImportService : IImportService
{
    private readonly IDbContextFactory<BookDbContext> _factory;
    private readonly IBackupParser _parser;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<ImportService> _logger;
    private const int BatchSize = 10;

    public ImportService(
        IDbContextFactory<BookDbContext> factory,
        IBackupParser parser,
        ISettingsService settingsService,
        ILogger<ImportService> logger)
    {
        _factory = factory;
        _parser = parser;
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <summary>
    /// Dry-run preview: parse the backup and check for duplicates but write nothing to DB.
    /// </summary>
    public async Task<ImportPreview> PreviewAsync(
        string path,
        int collectionId,
        CancellationToken ct = default)
    {
        var parsed = await _parser.ParseAsync(path, ct);

        var isbnList = parsed.Books
            .Where(b => !string.IsNullOrWhiteSpace(b.Isbn))
            .Select(b => b.Isbn!)
            .ToList();

        // Check existing ISBNs in DB
        var duplicateIsbnCount = 0;
        if (isbnList.Count > 0)
        {
            await using var dbContext = await _factory.CreateDbContextAsync(ct);
            var existingIsbns = await dbContext.Books
                .Where(b => b.Isbn != null && isbnList.Contains(b.Isbn))
                .Select(b => b.Isbn!)
                .ToListAsync(ct);
            var existingSet = new HashSet<string>(existingIsbns, StringComparer.OrdinalIgnoreCase);
            duplicateIsbnCount = isbnList.Count(isbn => existingSet.Contains(isbn));
        }

        var sampleRows = new List<ImportSampleRow>();
        for (int i = 0; i < Math.Min(10, parsed.Books.Count); i++)
        {
            var book = parsed.Books[i];
            sampleRows.Add(new ImportSampleRow(
                RowNumber: i + 1,
                Title: book.Title,
                Isbn: book.Isbn,
                AuthorDisplay: GetFirstContributorDisplay(book),
                PublisherName: null, // Not resolving publisher names in preview (perf)
                HasCover: parsed.FullImagesByRowKey.ContainsKey(book.RowKey),
                DuplicateNote: null));
        }

        return new ImportPreview(
            TotalRecords: parsed.Books.Count,
            RecordsWithIsbn: isbnList.Count,
            RecordsWithoutIsbn: parsed.Books.Count - isbnList.Count,
            DuplicateIsbnCount: duplicateIsbnCount,
            RecordsWithCovers: parsed.FullImagesByRowKey.Count,
            Warnings: parsed.Warnings,
            SampleRows: sampleRows);
    }

    private static string? GetFirstContributorDisplay(ParsedBook book)
    {
        var author = book.ResolvedContributors.FirstOrDefault(c => c.Role == "Author");
        if (string.IsNullOrWhiteSpace(author.DisplayName))
            return author.DisplayName;

        // Preview the same cleaned first author the import will create — unwrap a serialized "[A, B, C]" list and
        // strip any "(role)" suffix via the shared helper, so the preview never shows the raw bracketed blob.
        var first = PersonNameHelper.SplitSquished(author.DisplayName).FirstOrDefault();
        return first is null ? author.DisplayName : PersonNameHelper.ParseDisplayNameAndRoleHint(first).DisplayName;
    }

    /// <summary>
    /// Full import: parse backup, write all records to DB in batches.
    /// Reports progress via IProgress. Respects CancellationToken between batches.
    /// </summary>
    public async Task<ImportResult> ImportAsync(
        string path,
        int collectionId,
        IProgress<ImportProgress>? progress = null,
        Func<string, CancellationToken, Task<ImportDuplicateResolution>>? askCallback = null,
        CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogWarning("Import started — File: {FilePath}", path);

        var counters = new ImportCounters();
        var errors = new List<string>();
        var wasCancelled = false;

        // Setup phase (settings, parse, ISBN pre-load) runs before the batch loop. If the import
        // is cancelled here — before any book is written — return a cancelled, empty result rather
        // than letting the OperationCanceledException escape. This mirrors the per-batch
        // cancellation handling below, so ImportAsync never throws on cancellation.
        ParsedBackup parsed;
        string overwritePolicy;
        HashSet<string> existingIsbns;
        try
        {
            overwritePolicy = await _settingsService.GetAsync("Import.OverwritePolicy", ct) ?? "Skip";
            parsed = await _parser.ParseAsync(path, ct);

            // Pre-load existing ISBNs for duplicate detection
            await using var dbContext = await _factory.CreateDbContextAsync(ct);
            var allIsbns = await dbContext.Books
                .Where(b => b.Isbn != null)
                .Select(b => b.Isbn!)
                .ToListAsync(ct);
            existingIsbns = new HashSet<string>(allIsbns, StringComparer.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning("Import cancelled during setup — File: {FilePath}", path);
            wasCancelled = true;
            WeakReferenceMessenger.Default.Send(new ImportCompleteMessage(0, 0, wasCancelled));
            return new ImportResult(0, 0, 0, 0, wasCancelled, errors);
        }

        // One resolver for the whole run, so an "apply to all" choice persists across batches.
        var duplicateResolver = new DuplicateResolver(overwritePolicy, askCallback);

        var books = parsed.Books;
        var fullImages = parsed.FullImagesByRowKey;
        var thumbImages = parsed.ThumbImagesByRowKey;

        // RowKey -> BookId map built during import for post-pass Volume/Chapter linking
        var rowKeyToBookId = new Dictionary<int, int>();

        var batches = books
            .Select((book, index) => (book, index))
            .GroupBy(x => x.index / BatchSize)
            .Select(g => g.Select(x => x.book).ToList())
            .ToList();

        for (int batchIdx = 0; batchIdx < batches.Count; batchIdx++)
        {
            var batch = batches[batchIdx];

            try
            {
                ct.ThrowIfCancellationRequested();
                await ProcessBatchAsync(
                    batch, collectionId, fullImages, thumbImages,
                    existingIsbns, rowKeyToBookId, counters, errors,
                    duplicateResolver, ct);
            }
            catch (OperationCanceledException)
            {
                wasCancelled = true;
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Batch {BatchIndex} failed during import of {FilePath}", batchIdx + 1, path);
                errors.Add($"Batch {batchIdx + 1} error: {ex.Message}");
            }

            var processed = Math.Min((batchIdx + 1) * BatchSize, books.Count);
            progress?.Report(new ImportProgress(processed, books.Count, batch[^1].Title));

            // Send progress message for wizard step 4
            WeakReferenceMessenger.Default.Send(new ImportProgressMessage(
                processed, books.Count, batch[^1].Title));
        }

        // Must run after all book batches complete so RowKey->BookId map is fully populated

        if (!wasCancelled && (parsed.Volumes.Count > 0 || parsed.Chapters.Count > 0))
        {
            try
            {
                await ImportVolumesAndChaptersAsync(parsed, rowKeyToBookId, errors, ct);
            }
            catch (OperationCanceledException)
            {
                wasCancelled = true;
            }
            catch (Exception ex)
            {
                errors.Add($"Volume/Chapter import error: {ex.Message}");
            }
        }

        sw.Stop();
        _logger.LogWarning(
            "Import completed — Imported: {ImportedCount}, Skipped: {SkippedCount}, Errors: {ErrorCount}, Duration: {DurationMs}ms",
            counters.Imported, counters.Skipped, errors.Count, sw.ElapsedMilliseconds);

        var result = new ImportResult(counters.Imported, counters.Updated, counters.Skipped, counters.FlaggedNoIsbn, wasCancelled, errors);

        // Notify book list to refresh
        WeakReferenceMessenger.Default.Send(new ImportCompleteMessage(counters.Imported, counters.Updated, wasCancelled));

        return result;
    }

    private sealed class ImportCounters
    {
        public int Imported;
        public int Updated;
        public int Skipped;
        public int FlaggedNoIsbn;
    }

    private async Task ProcessBatchAsync(
        List<ParsedBook> batch,
        int collectionId,
        Dictionary<int, List<(int ImageIndex, byte[] Data)>> fullImages,
        Dictionary<int, List<(int ImageIndex, byte[] Data)>> thumbImages,
        HashSet<string> existingIsbnSet,
        Dictionary<int, int> rowKeyToBookId,
        ImportCounters counters,
        List<string> errors,
        DuplicateResolver duplicateResolver,
        CancellationToken ct)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);

        // We resolve lookup entities by name within each batch context
        // Use a single caches instance to avoid duplicate inserts within the batch
        var caches = new ImportLookupCaches();

        // Pre-load existing lookup entities into caches
        await PreloadCachesAsync(dbContext, caches, ct);

        foreach (var parsedBook in batch)
        {
            ct.ThrowIfCancellationRequested();

            // Take the book's images out of the shared dictionaries up front so the memory is
            // reclaimable as soon as this iteration ends — the dictionaries otherwise hold the
            // whole catalog's covers until the last batch. No later pass reads them again.
            fullImages.Remove(parsedBook.RowKey, out var fullImageList);
            thumbImages.Remove(parsedBook.RowKey, out var thumbImageList);

            try
            {
                if (string.IsNullOrWhiteSpace(parsedBook.Isbn))
                    counters.FlaggedNoIsbn++;

                if (!string.IsNullOrWhiteSpace(parsedBook.Isbn) &&
                    existingIsbnSet.Contains(parsedBook.Isbn))
                {
                    // Existing book — merge empty fields only.
                    // Use the shared batch dbContext so GetOrCreateLookupAsync updates the in-batch caches;
                    // a separate mergeDb would leave the caches stale for subsequent books in the same batch.
                    var existing = await dbContext.Books
                        .AsTracking()
                        .FirstOrDefaultAsync(b => b.Isbn == parsedBook.Isbn, ct);

                    if (existing is not null)
                    {
                        var shouldOverwrite = await duplicateResolver.ShouldOverwriteAsync(parsedBook.Title, ct);

                        if (shouldOverwrite)
                        {
                            // BuildBookEntityAsync before MergeAll: GetOrCreateLookupAsync may call
                            // SaveChangesAsync internally; running it first avoids premature flush.
                            var importedBook = await BuildBookEntityAsync(parsedBook, collectionId, dbContext, caches, ct);
                            MergeAll(existing, importedBook);
                            await dbContext.SaveChangesAsync(ct);
                            counters.Updated++;
                        }
                        else
                        {
                            var importedBook = await BuildBookEntityAsync(parsedBook, collectionId, dbContext, caches, ct);
                            if (MergeEmptyOnly(existing, importedBook))
                            {
                                await dbContext.SaveChangesAsync(ct);
                                counters.Updated++;
                            }
                            else
                            {
                                counters.Skipped++;
                            }
                        }
                        continue;
                    }
                }

                // New book
                var book = await BuildBookEntityAsync(parsedBook, collectionId, dbContext, caches, ct);

                book.Added = parsedBook.DateEntered ?? DateTime.UtcNow;
                book.Updated = DateTime.UtcNow;

                dbContext.Books.Add(book);
                await dbContext.SaveChangesAsync(ct);

                // Track RowKey -> BookId for post-pass volume/chapter linking
                rowKeyToBookId[parsedBook.RowKey] = book.BookId;

                // Add contributors
                await AddContributorsAsync(dbContext, book.BookId, parsedBook, caches, ct);

                // Add categories
                await AddCategoriesAsync(dbContext, book.BookId, parsedBook, collectionId, caches, ct);

                // Write FULL_IMAGES as BookImage BLOB rows
                if (fullImageList is not null)
                {
                    try
                    {
                        foreach (var (imageIndex, bytes) in fullImageList)
                        {
                            var mimeType = ImageHelpers.DetectMimeType(bytes);
                            var (typeId, isPrimary) = MapFullImageIndex(imageIndex);
                            dbContext.BookImages.Add(new BookImage
                            {
                                BookId = book.BookId,
                                ImageData = bytes,
                                MimeType = mimeType,
                                IsPrimary = isPrimary,
                                DisplayOrder = imageIndex,
                                BookImageTypeId = typeId,
                                Added = DateTime.UtcNow
                            });
                        }
                        await dbContext.SaveChangesAsync(ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        errors.Add($"Cover BLOB insert failed for '{parsedBook.Title}': {ex.Message}");
                    }
                }

                // Write THUMB_IMAGES as BookImage BLOB rows
                if (thumbImageList is not null)
                {
                    try
                    {
                        foreach (var (imageIndex, bytes) in thumbImageList)
                        {
                            var mimeType = ImageHelpers.DetectMimeType(bytes);
                            dbContext.BookImages.Add(new BookImage
                            {
                                BookId = book.BookId,
                                ImageData = bytes,
                                MimeType = mimeType,
                                IsPrimary = false,
                                DisplayOrder = 1000 + imageIndex,
                                BookImageTypeId = 1,
                                Added = DateTime.UtcNow
                            });
                        }
                        await dbContext.SaveChangesAsync(ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        errors.Add($"Thumbnail BLOB insert failed for '{parsedBook.Title}': {ex.Message}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(parsedBook.Isbn))
                    existingIsbnSet.Add(parsedBook.Isbn);

                counters.Imported++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"Failed to import '{parsedBook.Title}': {ex.Message}");
                dbContext.ChangeTracker.Clear();
            }
        }
    }

    private async Task ImportVolumesAndChaptersAsync(
        ParsedBackup parsed,
        Dictionary<int, int> rowKeyToBookId,
        List<string> errors,
        CancellationToken ct)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);

        // Import Volumes — build VolumeRowKey -> BookVolumeId map
        var volumeRowKeyToId = new Dictionary<int, int>();
        foreach (var vol in parsed.Volumes)
        {
            if (!rowKeyToBookId.TryGetValue(vol.BookRowKey, out var bookId))
                continue; // Book not imported — skip volume

            var bookVolume = new BookVolume
            {
                BookId = bookId,
                VolumeNumber = vol.VolumeNumber
            };
            dbContext.BookVolumes.Add(bookVolume);
            await dbContext.SaveChangesAsync(ct);
            volumeRowKeyToId[vol.VolumeRowKey] = bookVolume.BookVolumeId;
        }

        // Import Chapters — resolve VolumeRowKey -> BookVolumeId
        foreach (var chp in parsed.Chapters)
        {
            if (!volumeRowKeyToId.TryGetValue(chp.VolumeRowKey, out var bookVolumeId))
                continue; // Volume not imported — skip chapter

            dbContext.BookChapters.Add(new BookChapter
            {
                BookVolumeId = bookVolumeId,
                ChapterNumber = chp.ChapterNumber
            });
        }
        await dbContext.SaveChangesAsync(ct);
    }

    // PersonCache is intentionally not populated here — people may be numerous and are
    // not needed for every batch. AddContributorsAsync lazy-loads it on first call.
    private static async Task PreloadCachesAsync(
        BookDbContext db,
        ImportLookupCaches caches,
        CancellationToken ct)
    {
        foreach (var p in await db.Publishers.ToListAsync(ct))
            caches.PublisherCache[p.Name] = p.PublisherId;
        foreach (var s in await db.Series.ToListAsync(ct))
            caches.SeriesCache[s.Name] = s.SeriesId;
        foreach (var f in await db.Formats.ToListAsync(ct))
            caches.FormatCache[f.Name] = f.FormatId;
        foreach (var e in await db.Editions.ToListAsync(ct))
            caches.EditionCache[e.Name] = e.EditionId;
        foreach (var l in await db.Languages.ToListAsync(ct))
            caches.LanguageCache[l.Name] = l.LanguageId;
        foreach (var c in await db.Conditions.ToListAsync(ct))
            caches.ConditionCache[c.Name] = c.ConditionId;
        foreach (var loc in await db.Locations.ToListAsync(ct))
            caches.LocationCache[loc.Name] = loc.LocationId;
        foreach (var o in await db.Owners.ToListAsync(ct))
            caches.OwnerCache[o.Name] = o.OwnerId;
        foreach (var st in await db.Statuses.ToListAsync(ct))
            caches.StatusCache[st.Name] = st.StatusId;
        foreach (var src in await db.Sources.ToListAsync(ct))
            caches.SourceCache[src.Name] = src.SourceId;
        foreach (var pp in await db.PurchasePlaces.ToListAsync(ct))
            caches.PurchasePlaceCache[pp.Name] = pp.PurchasePlaceId;
        foreach (var r in await db.Ratings.ToListAsync(ct))
            caches.RatingCache[r.Name] = r.RatingId;
        foreach (var rl in await db.ReadingLevels.ToListAsync(ct))
            caches.ReadingLevelCache[rl.Name] = rl.ReadingLevelId;
        foreach (var role in await db.ContributorRoles.ToListAsync(ct))
            caches.RoleCache[role.Code] = role.ContributorRoleId;
        foreach (var cat in await db.Categories.ToListAsync(ct))
            caches.CategoryCache[cat.Name] = cat.CategoryId;
    }

    private static async Task<Book> BuildBookEntityAsync(
        ParsedBook parsed,
        int collectionId,
        BookDbContext db,
        ImportLookupCaches caches,
        CancellationToken ct)
    {
        var book = new Book
        {
            CollectionId = collectionId,
            Title = parsed.Title,
            Subtitle = parsed.Subtitle,
            AltTitle = parsed.AltTitle,
            Isbn = parsed.Isbn,
            AmazonAsin = parsed.AmazonAsin,
            PubPlace = parsed.PubPlace,
            PubDate = parsed.PubDate,
            CopyrightDate = parsed.CopyrightDate,
            Pages = parsed.Pages,
            Copies = parsed.Copies,
            SeriesNumber = parsed.SeriesNumber,
            Signed = parsed.Signed,
            OutOfPrint = parsed.OutOfPrint,
            Favorite = parsed.Favorite,
            Keywords = parsed.Keywords,
            Comments = parsed.Comments,
            BookInfo = parsed.BookInfo,
            ExternalId = parsed.ExternalId,
            MediaLink = parsed.MediaLink,
            PurchasePrice = parsed.PurchasePrice,
            PurchaseCurrency = parsed.PurchaseCurrency,
            ListPrice = parsed.ListPrice,
            ListPriceCurrency = parsed.ListPriceCurrency,
            PurchaseDate = parsed.PurchaseDate,
            ReadCount = parsed.ReadCount,
            DateLastRead = parsed.DateLastRead,

            // Library Classification
            Issn = parsed.Issn,
            Lccn = parsed.Lccn,
            DeweyDecimal = parsed.DeweyDecimal,
            CallNumber = parsed.CallNumber,

            // Physical
            Dimensions = parsed.Dimensions,
            Weight = parsed.Weight,

            // Valuation
            ItemValue = parsed.ItemValue,
            ValuationDate = parsed.ValuationDate,

            // Amazon Marketplace
            AmazonNewValue = parsed.AmazonNewValue,
            AmazonUsedValue = parsed.AmazonUsedValue,
            AmazonCollectibleValue = parsed.AmazonCollectibleValue,
            AmazonNewCount = parsed.AmazonNewCount,
            AmazonUsedCount = parsed.AmazonUsedCount,
            AmazonCollectibleCount = parsed.AmazonCollectibleCount,
            SalesRank = parsed.SalesRank,

            // Reading
            LexileLevel = parsed.LexileLevel,
            // Resolve FK names to DB entity IDs via get-or-create
            PublisherId = await GetOrCreateLookupAsync(db, caches.PublisherCache, parsed.PublisherName,
                n => new Publisher { Name = n }, p => p.PublisherId, db.Publishers, ct),
            SeriesId = await GetOrCreateLookupAsync(db, caches.SeriesCache, parsed.SeriesName,
                n => new Series { Name = n }, s => s.SeriesId, db.Series, ct),
            FormatId = await GetOrCreateLookupAsync(db, caches.FormatCache, parsed.FormatName,
                n => new Format { Name = n }, f => f.FormatId, db.Formats, ct),
            EditionId = await GetOrCreateLookupAsync(db, caches.EditionCache, parsed.EditionName,
                n => new Edition { Name = n }, e => e.EditionId, db.Editions, ct),
            LanguageId = await GetOrCreateLookupAsync(db, caches.LanguageCache, parsed.LanguageName,
                n => new Language { Name = n }, l => l.LanguageId, db.Languages, ct),
            RatingId = await GetOrCreateLookupAsync(db, caches.RatingCache, parsed.RatingName,
                n => new Rating { Name = n }, r => r.RatingId, db.Ratings, ct),
            ConditionId = await GetOrCreateLookupAsync(db, caches.ConditionCache, parsed.ConditionName,
                n => new Condition { Name = n }, c => c.ConditionId, db.Conditions, ct),
            LocationId = await GetOrCreateLookupAsync(db, caches.LocationCache, parsed.LocationName,
                n => new Location { Name = n }, l => l.LocationId, db.Locations, ct),
            OwnerId = await GetOrCreateLookupAsync(db, caches.OwnerCache, parsed.OwnerName,
                n => new Owner { Name = n }, o => o.OwnerId, db.Owners, ct),
            StatusId = await GetOrCreateLookupAsync(db, caches.StatusCache, parsed.StatusName,
                n => new Status { Name = n }, s => s.StatusId, db.Statuses, ct),
            SourceId = await GetOrCreateLookupAsync(db, caches.SourceCache, parsed.SourceName,
                n => new Source { Name = n }, s => s.SourceId, db.Sources, ct),
            PurchasePlaceId = await GetOrCreateLookupAsync(db, caches.PurchasePlaceCache, parsed.PurchasePlaceName,
                n => new PurchasePlace { Name = n }, p => p.PurchasePlaceId, db.PurchasePlaces, ct),
            ReadingLevelId = await GetOrCreateLookupAsync(db, caches.ReadingLevelCache, parsed.ReadingLevelName,
                n => new ReadingLevel { Name = n }, r => r.ReadingLevelId, db.ReadingLevels, ct)
        };

        return book;
    }

    private static async Task<int?> GetOrCreateLookupAsync<TEntity>(
        BookDbContext db,
        Dictionary<string, int> cache,
        string? name,
        Func<string, TEntity> factory,
        Func<TEntity, int> idSelector,
        DbSet<TEntity> dbSet,
        CancellationToken ct) where TEntity : class
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (cache.TryGetValue(name, out var id)) return id;

        var entity = factory(name);
        dbSet.Add(entity);
        await db.SaveChangesAsync(ct);
        var newId = idSelector(entity);
        cache[name] = newId;
        return newId;
    }

    private static async Task AddContributorsAsync(
        BookDbContext db,
        int bookId,
        ParsedBook parsed,
        ImportLookupCaches caches,
        CancellationToken ct)
    {
        if (parsed.ResolvedContributors.Count == 0) return;

        // Pre-load people into cache if not already there.
        // Use PersonCacheLoaded rather than Count == 0: an empty DB has Count == 0
        // after loading, so the count-based guard would re-run on every book.
        if (!caches.PersonCacheLoaded)
        {
            foreach (var person in await db.People.ToListAsync(ct))
                caches.PersonCache[person.DisplayName] = person.PersonId;
            caches.PersonCacheLoaded = true;
        }

        var contributorRoles = await db.ContributorRoles
            .AsNoTracking()
            .ToListAsync(ct);

        var roleCodeCache = contributorRoles
            .ToDictionary(r => r.Code, r => r.ContributorRoleId, StringComparer.OrdinalIgnoreCase);

        var roleDisplayCache = contributorRoles
            .ToDictionary(r => r.DisplayName, r => r.ContributorRoleId, StringComparer.OrdinalIgnoreCase);

        var sortOrder = 0;
        foreach (var (roleName, displayName, _) in parsed.ResolvedContributors)
        {
            foreach (var fragment in PersonNameHelper.SplitSquished(displayName))
            {
                var (cleanDisplayName, roleHint) = PersonNameHelper.ParseDisplayNameAndRoleHint(fragment);
                if (string.IsNullOrWhiteSpace(cleanDisplayName))
                    continue;

                int roleId = default;
                var roleHintResolved = !string.IsNullOrWhiteSpace(roleHint)
                    && (roleCodeCache.TryGetValue(roleHint, out roleId)
                        || roleDisplayCache.TryGetValue(roleHint, out roleId));

                if (!roleHintResolved
                    && !(roleCodeCache.TryGetValue(roleName, out roleId)
                        || roleDisplayCache.TryGetValue(roleName, out roleId)))
                {
                    continue;
                }

                if (!caches.PersonCache.TryGetValue(cleanDisplayName, out var personId))
                {
                    var person = new Person
                    {
                        DisplayName = cleanDisplayName,
                        SortName = PersonNameHelper.DeriveSortName(cleanDisplayName)
                    };
                    db.People.Add(person);
                    await db.SaveChangesAsync(ct);
                    personId = person.PersonId;
                    caches.PersonCache[cleanDisplayName] = personId;
                }

                sortOrder++;
                db.BookContributors.Add(new BookContributor
                {
                    BookId = bookId,
                    PersonId = personId,
                    ContributorRoleId = roleId,
                    SortOrder = sortOrder
                });
            }
        }

        if (sortOrder > 0)
            await db.SaveChangesAsync(ct);
    }

    private static async Task AddCategoriesAsync(
        BookDbContext db,
        int bookId,
        ParsedBook parsed,
        int collectionId,
        ImportLookupCaches caches,
        CancellationToken ct)
    {
        if (parsed.ResolvedCategoryNames.Count == 0) return;

        foreach (var categoryName in parsed.ResolvedCategoryNames)
        {
            if (!caches.CategoryCache.TryGetValue(categoryName, out var categoryId))
            {
                var category = new Category { Name = categoryName };
                db.Categories.Add(category);
                await db.SaveChangesAsync(ct);
                categoryId = category.CategoryId;
                caches.CategoryCache[categoryName] = categoryId;
            }

            var exists = await db.BookCategories.AnyAsync(
                bc => bc.BookId == bookId && bc.CategoryId == categoryId, ct);
            if (!exists)
            {
                db.BookCategories.Add(new BookCategory
                {
                    BookId = bookId,
                    CategoryId = categoryId
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Fills null/empty fields on <paramref name="existing"/> from <paramref name="imported"/>.
    /// Never overwrites populated fields.
    /// Returns true if at least one field was updated.
    /// </summary>
    public static bool MergeEmptyOnly(Book existing, Book imported)
    {
        bool changed = false;

        if (existing.Subtitle is null && imported.Subtitle is not null) { existing.Subtitle = imported.Subtitle; changed = true; }
        if (existing.AltTitle is null && imported.AltTitle is not null) { existing.AltTitle = imported.AltTitle; changed = true; }
        if (existing.PublisherId is null && imported.PublisherId is not null) { existing.PublisherId = imported.PublisherId; changed = true; }
        if (existing.PubPlace is null && imported.PubPlace is not null) { existing.PubPlace = imported.PubPlace; changed = true; }
        if (existing.PubDate is null && imported.PubDate is not null) { existing.PubDate = imported.PubDate; changed = true; }
        if (existing.CopyrightDate is null && imported.CopyrightDate is not null) { existing.CopyrightDate = imported.CopyrightDate; changed = true; }
        if (existing.FormatId is null && imported.FormatId is not null) { existing.FormatId = imported.FormatId; changed = true; }
        if (existing.EditionId is null && imported.EditionId is not null) { existing.EditionId = imported.EditionId; changed = true; }
        if (existing.Pages is null && imported.Pages is not null) { existing.Pages = imported.Pages; changed = true; }
        if (existing.Isbn is null && imported.Isbn is not null) { existing.Isbn = imported.Isbn; changed = true; }
        if (existing.LanguageId is null && imported.LanguageId is not null) { existing.LanguageId = imported.LanguageId; changed = true; }
        if (existing.SeriesId is null && imported.SeriesId is not null) { existing.SeriesId = imported.SeriesId; changed = true; }
        if (existing.SeriesNumber is null && imported.SeriesNumber is not null) { existing.SeriesNumber = imported.SeriesNumber; changed = true; }
        if (existing.RatingId is null && imported.RatingId is not null) { existing.RatingId = imported.RatingId; changed = true; }
        if (existing.ConditionId is null && imported.ConditionId is not null) { existing.ConditionId = imported.ConditionId; changed = true; }
        if (existing.LocationId is null && imported.LocationId is not null) { existing.LocationId = imported.LocationId; changed = true; }
        if (existing.OwnerId is null && imported.OwnerId is not null) { existing.OwnerId = imported.OwnerId; changed = true; }
        if (existing.StatusId is null && imported.StatusId is not null) { existing.StatusId = imported.StatusId; changed = true; }
        if (existing.Keywords is null && imported.Keywords is not null) { existing.Keywords = imported.Keywords; changed = true; }
        if (existing.Comments is null && imported.Comments is not null) { existing.Comments = imported.Comments; changed = true; }
        if (existing.BookInfo is null && imported.BookInfo is not null) { existing.BookInfo = imported.BookInfo; changed = true; }
        if (existing.PurchasePrice is null && imported.PurchasePrice is not null) { existing.PurchasePrice = imported.PurchasePrice; changed = true; }
        if (existing.PurchaseCurrency is null && imported.PurchaseCurrency is not null) { existing.PurchaseCurrency = imported.PurchaseCurrency; changed = true; }
        if (existing.PurchasePlaceId is null && imported.PurchasePlaceId is not null) { existing.PurchasePlaceId = imported.PurchasePlaceId; changed = true; }
        if (existing.ListPrice is null && imported.ListPrice is not null) { existing.ListPrice = imported.ListPrice; changed = true; }
        if (existing.ListPriceCurrency is null && imported.ListPriceCurrency is not null) { existing.ListPriceCurrency = imported.ListPriceCurrency; changed = true; }
        if (existing.SourceId is null && imported.SourceId is not null) { existing.SourceId = imported.SourceId; changed = true; }
        if (existing.ExternalId is null && imported.ExternalId is not null) { existing.ExternalId = imported.ExternalId; changed = true; }
        if (existing.MediaLink is null && imported.MediaLink is not null) { existing.MediaLink = imported.MediaLink; changed = true; }
        if (existing.AmazonAsin is null && imported.AmazonAsin is not null) { existing.AmazonAsin = imported.AmazonAsin; changed = true; }
        if (existing.PurchaseDate is null && imported.PurchaseDate is not null) { existing.PurchaseDate = imported.PurchaseDate; changed = true; }
        if (existing.DateLastRead is null && imported.DateLastRead is not null) { existing.DateLastRead = imported.DateLastRead; changed = true; }

        // Library Classification
        if (existing.Issn is null && imported.Issn is not null) { existing.Issn = imported.Issn; changed = true; }
        if (existing.Lccn is null && imported.Lccn is not null) { existing.Lccn = imported.Lccn; changed = true; }
        if (existing.DeweyDecimal is null && imported.DeweyDecimal is not null) { existing.DeweyDecimal = imported.DeweyDecimal; changed = true; }
        if (existing.CallNumber is null && imported.CallNumber is not null) { existing.CallNumber = imported.CallNumber; changed = true; }

        // Physical
        if (existing.Dimensions is null && imported.Dimensions is not null) { existing.Dimensions = imported.Dimensions; changed = true; }
        if (existing.Weight is null && imported.Weight is not null) { existing.Weight = imported.Weight; changed = true; }

        // Valuation
        if (existing.ItemValue is null && imported.ItemValue is not null) { existing.ItemValue = imported.ItemValue; changed = true; }
        if (existing.ValuationDate is null && imported.ValuationDate is not null) { existing.ValuationDate = imported.ValuationDate; changed = true; }

        // Amazon Marketplace
        if (existing.AmazonNewValue is null && imported.AmazonNewValue is not null) { existing.AmazonNewValue = imported.AmazonNewValue; changed = true; }
        if (existing.AmazonUsedValue is null && imported.AmazonUsedValue is not null) { existing.AmazonUsedValue = imported.AmazonUsedValue; changed = true; }
        if (existing.AmazonCollectibleValue is null && imported.AmazonCollectibleValue is not null) { existing.AmazonCollectibleValue = imported.AmazonCollectibleValue; changed = true; }
        if (existing.AmazonNewCount is null && imported.AmazonNewCount is not null) { existing.AmazonNewCount = imported.AmazonNewCount; changed = true; }
        if (existing.AmazonUsedCount is null && imported.AmazonUsedCount is not null) { existing.AmazonUsedCount = imported.AmazonUsedCount; changed = true; }
        if (existing.AmazonCollectibleCount is null && imported.AmazonCollectibleCount is not null) { existing.AmazonCollectibleCount = imported.AmazonCollectibleCount; changed = true; }
        if (existing.SalesRank is null && imported.SalesRank is not null) { existing.SalesRank = imported.SalesRank; changed = true; }

        // Reading
        if (existing.LexileLevel is null && imported.LexileLevel is not null) { existing.LexileLevel = imported.LexileLevel; changed = true; }

        if (changed) existing.Updated = DateTime.UtcNow;
        return changed;
    }

    /// <summary>
    /// Overwrites all non-null fields on <paramref name="existing"/> from <paramref name="imported"/>.
    /// Used by OverwritePolicy=Overwrite.
    /// </summary>
    private static void MergeAll(Book existing, Book imported)
    {
        if (imported.Subtitle is not null)      existing.Subtitle      = imported.Subtitle;
        if (imported.AltTitle is not null)      existing.AltTitle      = imported.AltTitle;
        if (imported.PublisherId is not null)   existing.PublisherId   = imported.PublisherId;
        if (imported.PubPlace is not null)      existing.PubPlace      = imported.PubPlace;
        if (imported.PubDate is not null)       existing.PubDate       = imported.PubDate;
        if (imported.CopyrightDate is not null) existing.CopyrightDate = imported.CopyrightDate;
        if (imported.FormatId is not null)      existing.FormatId      = imported.FormatId;
        if (imported.EditionId is not null)     existing.EditionId     = imported.EditionId;
        if (imported.Pages is not null)         existing.Pages         = imported.Pages;
        if (imported.LanguageId is not null)    existing.LanguageId    = imported.LanguageId;
        if (imported.SeriesId is not null)      existing.SeriesId      = imported.SeriesId;
        if (imported.SeriesNumber is not null)  existing.SeriesNumber  = imported.SeriesNumber;
        if (imported.RatingId is not null)      existing.RatingId      = imported.RatingId;
        if (imported.ConditionId is not null)   existing.ConditionId   = imported.ConditionId;
        if (imported.LocationId is not null)    existing.LocationId    = imported.LocationId;
        if (imported.OwnerId is not null)       existing.OwnerId       = imported.OwnerId;
        if (imported.StatusId is not null)      existing.StatusId      = imported.StatusId;
        if (imported.Keywords is not null)      existing.Keywords      = imported.Keywords;
        if (imported.Comments is not null)      existing.Comments      = imported.Comments;
        if (imported.BookInfo is not null)      existing.BookInfo      = imported.BookInfo;
        if (imported.PurchasePrice is not null) existing.PurchasePrice = imported.PurchasePrice;
        if (imported.PurchaseCurrency is not null) existing.PurchaseCurrency = imported.PurchaseCurrency;
        if (imported.PurchasePlaceId is not null)  existing.PurchasePlaceId  = imported.PurchasePlaceId;
        if (imported.ListPrice is not null)     existing.ListPrice     = imported.ListPrice;
        if (imported.ListPriceCurrency is not null) existing.ListPriceCurrency = imported.ListPriceCurrency;
        if (imported.SourceId is not null)      existing.SourceId      = imported.SourceId;
        if (imported.ExternalId is not null)    existing.ExternalId    = imported.ExternalId;
        if (imported.MediaLink is not null)     existing.MediaLink     = imported.MediaLink;
        if (imported.AmazonAsin is not null)    existing.AmazonAsin    = imported.AmazonAsin;
        if (imported.PurchaseDate is not null)  existing.PurchaseDate  = imported.PurchaseDate;
        if (imported.DateLastRead is not null)  existing.DateLastRead  = imported.DateLastRead;
        if (imported.Issn is not null)          existing.Issn          = imported.Issn;
        if (imported.Lccn is not null)          existing.Lccn          = imported.Lccn;
        if (imported.DeweyDecimal is not null)  existing.DeweyDecimal  = imported.DeweyDecimal;
        if (imported.CallNumber is not null)    existing.CallNumber    = imported.CallNumber;
        if (imported.Dimensions is not null)    existing.Dimensions    = imported.Dimensions;
        if (imported.Weight is not null)        existing.Weight        = imported.Weight;
        if (imported.ItemValue is not null)     existing.ItemValue     = imported.ItemValue;
        if (imported.ValuationDate is not null) existing.ValuationDate = imported.ValuationDate;
        if (imported.AmazonNewValue is not null)         existing.AmazonNewValue         = imported.AmazonNewValue;
        if (imported.AmazonUsedValue is not null)        existing.AmazonUsedValue        = imported.AmazonUsedValue;
        if (imported.AmazonCollectibleValue is not null) existing.AmazonCollectibleValue = imported.AmazonCollectibleValue;
        if (imported.AmazonNewCount is not null)         existing.AmazonNewCount         = imported.AmazonNewCount;
        if (imported.AmazonUsedCount is not null)        existing.AmazonUsedCount        = imported.AmazonUsedCount;
        if (imported.AmazonCollectibleCount is not null) existing.AmazonCollectibleCount = imported.AmazonCollectibleCount;
        if (imported.SalesRank is not null)     existing.SalesRank     = imported.SalesRank;
        if (imported.LexileLevel is not null)   existing.LexileLevel   = imported.LexileLevel;
        existing.Updated = DateTime.UtcNow;
    }

    /// <summary>
    /// Maps a Readerware FULL_IMAGES IMAGE_INDEX to a BookImageTypeId and IsPrimary flag.
    /// Index-to-type heuristic: 0=Cover, 1=BackCover, 2=Spine, 3=DustJacket, 4+=Cover overflow.
    /// IsPrimary means "first image of this BookImageType for the book" (per-type primary, not book-level unique).
    /// Queries that expect a single primary image per book should filter by TypeId as well.
    /// </summary>
    private static (int TypeId, bool IsPrimary) MapFullImageIndex(int imageIndex) =>
        imageIndex switch
        {
            0 => (0, true),   // Cover — primary
            _ => (0, false),  // Additional images treated as cover overflow and not primary
        };

    /// <summary>
    /// Decides whether to overwrite an existing book on an ISBN duplicate, honouring the configured
    /// policy and — for "Ask" — the user's per-item or "apply to all" choice, persisted for the run.
    /// Throws <see cref="OperationCanceledException"/> when the user chooses to cancel the import.
    /// </summary>
    private sealed class DuplicateResolver(
        string policy,
        Func<string, CancellationToken, Task<ImportDuplicateResolution>>? askCallback)
    {
        private bool? _stickyOverwrite; // null = keep asking; true/false = apply to all remaining

        public async Task<bool> ShouldOverwriteAsync(string title, CancellationToken ct)
        {
            switch (policy)
            {
                case "Overwrite":
                    return true;
                case "Ask":
                    if (_stickyOverwrite is bool sticky) return sticky;
                    if (askCallback is null) return false;
                    switch (await askCallback(title, ct))
                    {
                        case ImportDuplicateResolution.Overwrite:    return true;
                        case ImportDuplicateResolution.OverwriteAll: _stickyOverwrite = true;  return true;
                        case ImportDuplicateResolution.Skip:         return false;
                        case ImportDuplicateResolution.SkipAll:      _stickyOverwrite = false; return false;
                        case ImportDuplicateResolution.CancelImport: throw new OperationCanceledException();
                        default:                                     return false;
                    }
                default:
                    return false; // "Skip" or unknown — leave unchanged
            }
        }
    }
}
