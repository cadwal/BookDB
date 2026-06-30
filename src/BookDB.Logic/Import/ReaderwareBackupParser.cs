using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;

namespace BookDB.Logic.Import;

/// <summary>
/// Parses a Readerware native backup — either a pre-extracted folder or a zip archive.
/// All backup files are UTF-16 Big Endian encoded, no BOM.
/// Format version: DBCATALOG40 file must be present.
/// </summary>
public sealed class ReaderwareBackupParser : IBackupParser
{
    private const string MainFile      = "READERWARE";
    private const string ContribFile   = "CONTRIBUTOR";
    private const string FullImgFile   = "FULL_IMAGES";
    private const string VersionFile   = "DBCATALOG40";
    private const string ThumbImgFile  = "THUMB_IMAGES";
    private const string VolumesFile   = "READERWARE_VOLUMES";
    private const string ChaptersFile  = "READERWARE_CHAPTERS";
    private const string LoansFile     = "LOANS";
    private const string BorrowerFile  = "BORROWER";

    /// <summary>
    /// Parse a backup. Path may be a .zip file or a folder.
    /// Returns the parsed backup without writing anything to the database.
    /// </summary>
    public async Task<ParsedBackup> ParseAsync(string path, CancellationToken ct = default)
    {
        string backupFolder;
        string? tempDir = null;

        if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            tempDir = Path.Combine(Path.GetTempPath(), $"bookdb_import_{Guid.NewGuid():N}");
            await Task.Run(() => ZipFile.ExtractToDirectory(path, tempDir), ct);
            backupFolder = tempDir;
        }
        else if (Directory.Exists(path))
        {
            backupFolder = path;
        }
        else
        {
            throw new ArgumentException($"Path is not a valid backup zip or folder: {path}");
        }

        try
        {
            return await Task.Run(() => ParseFolder(backupFolder, ct), ct);
        }
        finally
        {
            if (tempDir is not null && Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private ParsedBackup ParseFolder(string folder, CancellationToken ct)
    {
        var warnings = new List<string>();

        // Validate format version
        if (!File.Exists(Path.Combine(folder, VersionFile)))
            warnings.Add($"DBCATALOG40 file not found — backup may not be Readerware 4.x format");

        // Load lookup tables
        var cache = new ImportLookupCache();
        cache.LoadAll(folder);

        // Load CONTRIBUTOR file
        var contributors = LoadContributors(folder);

        // Load image files using shared helper
        var fullImages = LoadImageFile(folder, FullImgFile, warnings);
        var thumbImages = LoadImageFile(folder, ThumbImgFile, warnings);

        // Load volume/chapter/borrower/loan data
        var volumes = LoadVolumes(folder, warnings);
        var chapters = LoadChapters(folder, warnings);
        var borrowers = LoadBorrowers(folder);
        var loans = LoadLoans(folder);

        // Parse READERWARE main file
        var books = ParseMainFile(folder, cache, contributors, warnings, ct);

        return new ParsedBackup
        {
            Books = books,
            FullImagesByRowKey = fullImages,
            ThumbImagesByRowKey = thumbImages,
            Volumes = volumes,
            Chapters = chapters,
            Borrowers = borrowers,
            Loans = loans,
            Warnings = warnings
        };
    }

    private static List<ParsedBook> ParseMainFile(
        string folder,
        ImportLookupCache cache,
        Dictionary<int, ContributorRecord> contributors,
        List<string> warnings,
        CancellationToken ct)
    {
        var path = Path.Combine(folder, MainFile);
        if (!File.Exists(path))
            throw new FileNotFoundException($"READERWARE file not found in backup folder: {folder}");

        var books = new List<ParsedBook>();
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.BigEndianUnicode);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = context => warnings.Add($"Bad CSV data: {(context.RawRecord?.Length > 80 ? context.RawRecord[..80] : context.RawRecord)}")
        };
        using var csv = new CsvReader(reader, config);

        if (!csv.Read())
            throw new InvalidDataException("Backup file appears empty or has no header row.");
        csv.ReadHeader();

        while (csv.Read())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                books.Add(ParseRow(csv, cache, contributors));
            }
            catch (Exception ex)
            {
                warnings.Add($"Skipped row {csv.CurrentIndex}: {ex.Message}");
            }
        }

        return books;
    }

    private static ParsedBook ParseRow(
        CsvReader csv,
        ImportLookupCache cache,
        Dictionary<int, ContributorRecord> contributors)
    {
        static int GetInt(CsvReader r, string col)
        {
            var v = r.GetField(col);
            return int.TryParse(v, out var i) ? i : -1;
        }

        static bool GetBool(CsvReader r, string col)
        {
            var v = r.GetField(col);
            return string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
        }

        static decimal? GetDecimal(CsvReader r, string col)
        {
            var v = r.GetField(col);
            return decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
        }

        static int? GetNullableInt(CsvReader r, string col)
        {
            if (!r.TryGetField<string>(col, out var val) || string.IsNullOrWhiteSpace(val)) return null;
            return int.TryParse(val.Trim(), out var result) ? result : null;
        }

        var rowKey = GetInt(csv, "ROWKEY");

        var book = new ParsedBook
        {
            RowKey = rowKey,
            Title = csv.GetField("TITLE") ?? string.Empty,
            Subtitle = NullIfEmpty(csv.GetField("SUBTITLE")),
            AltTitle = NullIfEmpty(csv.GetField("ALT_TITLE")),
            Isbn = NullIfEmpty(csv.GetField("ISBN")),
            AmazonAsin = NullIfEmpty(csv.GetField("AM_ASIN")),
            PubPlace = ResolvePublicationPlace(cache, GetInt(csv, "PUB_PLACE")),
            PubDate = NullIfEmpty(csv.GetField("RELEASE_DATE")),
            CopyrightDate = NullIfEmpty(csv.GetField("COPYRIGHT_DATE")),
            Pages = GetInt(csv, "PAGES") is int p and > 0 ? p : null,
            Copies = Math.Max(1, GetInt(csv, "COPIES") is int c and > 0 ? c : 1),
            SeriesNumber = NullIfSeriesNumber(NullIfEmpty(csv.GetField("SERIES_NUMBER"))),
            Signed = GetBool(csv, "SIGNED"),
            OutOfPrint = GetBool(csv, "OUT_OF_PRINT"),
            Favorite = GetBool(csv, "FAVORITE"),
            Keywords = NullIfEmpty(csv.GetField("KEYWORDS")),
            Comments = BuildComments(csv),
            BookInfo = NullIfEmpty(csv.GetField("PRODUCT_INFO")),
            ExternalId = NullIfEmpty(csv.GetField("EXTERNAL_ID")),
            MediaLink = NullIfEmpty(csv.GetField("MEDIA_URL")),
            PurchasePrice = GetDecimal(csv, "PURCHASE_PRICE"),
            ListPrice = GetDecimal(csv, "LIST_PRICE"),
            PurchaseCurrency = MapCurrencySymbol(csv.GetField("CURRENCY_SYMBOL")),
            ListPriceCurrency = MapCurrencySymbol(csv.GetField("CURRENCY_SYMBOL")),
            PurchaseDate = ParseDate(csv.GetField("PURCHASE_DATE")),
            ReadCount = Math.Max(0, GetInt(csv, "READ_COUNT") is int rc and >= 0 ? rc : 0),
            DateLastRead = ParseDate(csv.GetField("DATE_LAST_READ")),
            PublisherRowKey = GetInt(csv, "PUBLISHER"),
            FormatRowKey = GetInt(csv, "FORMAT"),
            EditionRowKey = GetInt(csv, "EDITION"),
            LanguageRowKey = GetInt(csv, "CONTENT_LANGUAGE"),
            SeriesRowKey = GetInt(csv, "SERIES"),
            RatingRowKey = GetInt(csv, "MY_RATING"),
            ConditionRowKey = GetInt(csv, "ITEM_CONDITION"),
            LocationRowKey = GetInt(csv, "LOCATION"),
            OwnerRowKey = GetInt(csv, "OWNER"),
            StatusRowKey = GetInt(csv, "STATUS"),
            SourceRowKey = GetInt(csv, "SOURCE"),
            PurchasePlaceRowKey = GetInt(csv, "PURCHASE_PLACE"),
            ReadingLevelRowKey = GetInt(csv, "READING_LEVEL"),
            CategoryRowKeys = new[]
            {
                GetInt(csv, "CATEGORY1"),
                GetInt(csv, "CATEGORY2"),
                GetInt(csv, "CATEGORY3")
            }.Where(k => k > 0).Distinct().ToArray(),
            FullImageCount = GetInt(csv, "FULL_IMAGE_COUNT"),
        };

        // Library Classification
        book.Issn = NullIfEmpty(csv.GetField("ISSN"));
        book.Lccn = NullIfEmpty(csv.GetField("LCCN"));
        book.DeweyDecimal = NullIfEmpty(csv.GetField("DEWEY"));
        book.CallNumber = NullIfEmpty(csv.GetField("CALL_NUMBER"));

        // Physical
        book.Dimensions = NullIfEmpty(csv.GetField("DIMENSIONS"));
        book.Weight = GetDecimal(csv, "WEIGHT");

        // Valuation
        book.ItemValue = GetDecimal(csv, "ITEM_VALUE");
        book.ValuationDate = ParseDate(csv.GetField("VALUATION_DATE"));

        // Amazon Marketplace
        book.AmazonNewValue = GetDecimal(csv, "NEW_VALUE");
        book.AmazonUsedValue = GetDecimal(csv, "USED_VALUE");
        book.AmazonCollectibleValue = GetDecimal(csv, "COLLECTIBLE_VALUE");
        book.AmazonNewCount = GetNullableInt(csv, "NEW_COUNT");
        book.AmazonUsedCount = GetNullableInt(csv, "USED_COUNT");
        book.AmazonCollectibleCount = GetNullableInt(csv, "COLLECTIBLE_COUNT");
        book.SalesRank = GetNullableInt(csv, "SALES_RANK");

        // Reading
        book.LexileLevel = GetNullableInt(csv, "LEXILE_LEVEL");

        // DATE_ENTERED → preserve original Added date
        book.DateEntered = ParseDate(csv.GetField("DATE_ENTERED"));

        // Contributors: AUTHOR through AUTHOR6, ILLUSTRATOR, TRANSLATOR, EDITOR
        var contribColumns = new (string col, string role)[]
        {
            ("AUTHOR",       "Author"),
            ("AUTHOR2",      "Author"),
            ("AUTHOR3",      "Author"),
            ("AUTHOR4",      "Author"),
            ("AUTHOR5",      "Author"),
            ("AUTHOR6",      "Author"),
            ("ILLUSTRATOR",  "Illustrator"),
            ("TRANSLATOR",   "Translator"),
            ("EDITOR",       "Editor"),
        };

        foreach (var (col, role) in contribColumns)
        {
            var fk = GetInt(csv, col);
            if (fk <= 0) continue;
            if (!book.ContributorsByRole.TryGetValue(role, out var list))
            {
                list = [];
                book.ContributorsByRole[role] = list;
            }
            list.Add(fk);
        }

        // Resolve contributor names from the CONTRIBUTOR file. Messy multi-author names (a serialized
        // "[A, B, C]" list catalogued into one field, "A / B", "A and B", …) are split downstream by
        // PersonNameHelper, which every import path shares — see ImportService.
        foreach (var (role, contribKeys) in book.ContributorsByRole)
        {
            foreach (var key in contribKeys)
            {
                if (contributors.TryGetValue(key, out var record))
                    book.ResolvedContributors.Add((role, record.DisplayName, record.SortName));
            }
        }

        // Resolve FK row keys to names via the lookup cache
        book.PublisherName = cache.Resolve("PUBLISHER_LIST", book.PublisherRowKey);
        book.SeriesName = cache.Resolve("SERIES_LIST", book.SeriesRowKey);
        book.FormatName = cache.Resolve("FORMAT_LIST", book.FormatRowKey);
        book.EditionName = cache.Resolve("EDITION_LIST", book.EditionRowKey);
        book.LanguageName = cache.Resolve("LANGUAGE_LIST", book.LanguageRowKey);
        book.RatingName = cache.Resolve("MY_RATING_LIST", book.RatingRowKey);
        book.ConditionName = cache.Resolve("CONDITION_LIST", book.ConditionRowKey);
        book.LocationName = cache.Resolve("LOCATION_LIST", book.LocationRowKey);
        book.OwnerName = cache.Resolve("OWNER_LIST", book.OwnerRowKey);
        book.StatusName = cache.Resolve("STATUS_LIST", book.StatusRowKey);
        book.SourceName = cache.Resolve("SOURCE_LIST", book.SourceRowKey);
        book.PurchasePlaceName = cache.Resolve("PURCHASE_PLACE_LIST", book.PurchasePlaceRowKey);
        book.ReadingLevelName = cache.Resolve("READING_LEVEL_LIST", book.ReadingLevelRowKey);

        // Resolve category names
        foreach (var catKey in book.CategoryRowKeys)
        {
            var name = cache.Resolve("CATEGORY_LIST", catKey);
            if (name is not null)
                book.ResolvedCategoryNames.Add(name);
        }

        return book;
    }

    private static string? ResolvePublicationPlace(ImportLookupCache cache, int rowKey)
        => rowKey <= 0 ? null : cache.Resolve("PUBLICATION_PLACE_LIST", rowKey);

    private static string? BuildComments(CsvReader csv)
        => NullIfEmpty(csv.GetField("MY_COMMENTS"));

    private static string? MapCurrencySymbol(string? symbol)
    {
        if (symbol is null) return null;
        return symbol.Trim() switch
        {
            "$"   => "USD",
            "SEK" => "SEK",
            "kr"  => "SEK",
            "€"   => "EUR",
            "£"   => "GBP",
            ""    => null,
            _     => symbol  // preserve unknown symbols as-is
        };
    }

    private static string? NullIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// Parses a date string from CSV to DateTime?. Returns null for null/empty/unparseable values.
    /// Accepts common formats: ISO 8601 (yyyy-MM-dd), US (MM/dd/yyyy), and any format
    /// recognized by DateTime.TryParse with invariant culture.
    /// </summary>
    private static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var dt) ? dt : null;
    }

    private static string? NullIfSeriesNumber(string? s)
        => s is "0" or "0.0" ? null : s;

    private static Dictionary<int, ContributorRecord> LoadContributors(string folder)
    {
        var path = Path.Combine(folder, ContribFile);
        if (!File.Exists(path)) return [];

        var result = new Dictionary<int, ContributorRecord>();
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.BigEndianUnicode);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null
        };
        using var csv = new CsvReader(reader, config);

        if (!csv.Read())
            return result;
        csv.ReadHeader();

        while (csv.Read())
        {
            if (!int.TryParse(csv.GetField("ROWKEY"), out var rowKey)) continue;
            var name = csv.GetField("NAME");
            var sortName = csv.GetField("SORT_NAME");
            if (name is null) continue;
            result[rowKey] = new ContributorRecord(rowKey, name.Trim(), sortName?.Trim() ?? name.Trim());
        }

        return result;
    }

    private static Dictionary<int, List<(int ImageIndex, string HexData)>> LoadImageFile(
        string folder, string fileName, List<string> warnings)
    {
        var path = Path.Combine(folder, fileName);
        if (!File.Exists(path)) return [];

        var result = new Dictionary<int, List<(int ImageIndex, string HexData)>>();
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.BigEndianUnicode);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null
        };
        using var csv = new CsvReader(reader, config);

        if (!csv.Read())
            return result;
        csv.ReadHeader();

        while (csv.Read())
        {
            if (!int.TryParse(csv.GetField("ROW_ID"), out var rowId)) continue;
            if (!int.TryParse(csv.GetField("IMAGE_INDEX"), out var imageIndex)) continue;
            var hexData = csv.GetField("IMAGE_DATA");
            if (string.IsNullOrWhiteSpace(hexData)) continue;

            try
            {
                var trimmed = hexData.Trim();
                if (trimmed.Length < 8)
                {
                    warnings.Add($"{fileName} row {rowId}: image data too short -- skipping");
                    continue;
                }
                var prefix = Convert.FromHexString(trimmed[..8]);
                if (!IsRecognizedImage(prefix))
                {
                    warnings.Add($"{fileName} row {rowId}: unrecognized image format -- skipping");
                    continue;
                }

                if (!result.TryGetValue(rowId, out var list))
                {
                    list = [];
                    result[rowId] = list;
                }
                list.Add((imageIndex, trimmed));
            }
            catch (Exception ex)
            {
                warnings.Add($"{fileName} row {rowId}: hex validation failed ({ex.Message}) -- skipping");
            }
        }

        return result;
    }

    /// <summary>
    /// True when the leading bytes match a supported raster format: JPEG, PNG, GIF, or BMP.
    /// Readerware stores covers mostly as JPEG with a few GIFs; PNG/BMP are accepted for safety.
    /// </summary>
    private static bool IsRecognizedImage(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return true;          // JPEG
        if (b.Length >= 4 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return true; // PNG
        if (b.Length >= 3 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46) return true;          // GIF
        if (b.Length >= 2 && b[0] == 0x42 && b[1] == 0x4D) return true;                          // BMP
        return false;
    }

    private static List<ParsedVolume> LoadVolumes(string folder, List<string> warnings)
    {
        var path = Path.Combine(folder, VolumesFile);
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
            return [];

        var result = new List<ParsedVolume>();
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.BigEndianUnicode);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null
        };
        using var csv = new CsvReader(reader, config);
        if (!csv.Read()) return result;
        csv.ReadHeader();

        while (csv.Read())
        {
            if (!int.TryParse(csv.GetField("ROWKEY"), out var rowKey)) continue;
            if (!int.TryParse(csv.GetField("BOOK_ID"), out var bookId)) continue;
            if (!int.TryParse(csv.GetField("VOL_NUMBER"), out var volNumber)) continue;
            result.Add(new ParsedVolume(rowKey, bookId, volNumber));
        }
        return result;
    }

    private static List<ParsedChapter> LoadChapters(string folder, List<string> warnings)
    {
        var path = Path.Combine(folder, ChaptersFile);
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
            return [];

        var result = new List<ParsedChapter>();
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.BigEndianUnicode);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null
        };
        using var csv = new CsvReader(reader, config);
        if (!csv.Read()) return result;
        csv.ReadHeader();

        while (csv.Read())
        {
            if (!int.TryParse(csv.GetField("ROWKEY"), out var rowKey)) continue;
            if (!int.TryParse(csv.GetField("VOL_ID"), out var volId)) continue;
            if (!int.TryParse(csv.GetField("CHP_NUMBER"), out var chpNumber)) continue;
            result.Add(new ParsedChapter(rowKey, volId, chpNumber));
        }
        return result;
    }

    private static List<ParsedBorrower> LoadBorrowers(string folder)
    {
        var path = Path.Combine(folder, BorrowerFile);
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
            return [];

        // Future: parse CSV when data exists
        return [];
    }

    private static List<ParsedLoan> LoadLoans(string folder)
    {
        var path = Path.Combine(folder, LoansFile);
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
            return [];

        // Future: parse CSV when data exists
        return [];
    }

    /// <summary>Internal record for CONTRIBUTOR file entries.</summary>
    public record ContributorRecord(int RowKey, string DisplayName, string SortName);
}
