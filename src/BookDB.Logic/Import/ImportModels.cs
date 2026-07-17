using System;
using System.Collections.Generic;
using BookDB.Models.Entities;

namespace BookDB.Logic.Import;

/// <summary>A single parsed record from the READERWARE file, before DB resolution.</summary>
public class ParsedBook
{
    public int RowKey { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? AltTitle { get; set; }
    public string? Isbn { get; set; }
    public string? AmazonAsin { get; set; }
    public int PublisherRowKey { get; set; }
    public string? PubPlace { get; set; }
    public string? PubDate { get; set; }
    public string? CopyrightDate { get; set; }
    public int? Pages { get; set; }
    public int Copies { get; set; } = 1;
    public int FormatRowKey { get; set; }
    public int EditionRowKey { get; set; }
    public int LanguageRowKey { get; set; }
    public int SeriesRowKey { get; set; }
    public string? SeriesNumber { get; set; }
    public int RatingRowKey { get; set; }
    public int ConditionRowKey { get; set; }
    public int LocationRowKey { get; set; }
    public int OwnerRowKey { get; set; }
    public int StatusRowKey { get; set; }
    public int SourceRowKey { get; set; }
    public int PurchasePlaceRowKey { get; set; }
    public int ReadingLevelRowKey { get; set; }
    public bool Signed { get; set; }
    public bool OutOfPrint { get; set; }
    public bool Favorite { get; set; }
    public string? Keywords { get; set; }
    public string? Comments { get; set; }
    public string? BookInfo { get; set; }
    public string? ExternalId { get; set; }
    public string? MediaLink { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? ListPrice { get; set; }
    public string? PurchaseCurrency { get; set; }
    public string? ListPriceCurrency { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public int ReadCount { get; set; }
    public DateTime? DateEntered { get; set; }
    public DateTime? DateLastRead { get; set; }

    public string? Issn { get; set; }
    public string? Lccn { get; set; }
    public string? DeweyDecimal { get; set; }
    public string? CallNumber { get; set; }

    public string? Dimensions { get; set; }
    public decimal? Weight { get; set; }

    public decimal? ItemValue { get; set; }
    public DateTime? ValuationDate { get; set; }

    public decimal? AmazonNewValue { get; set; }
    public decimal? AmazonUsedValue { get; set; }
    public decimal? AmazonCollectibleValue { get; set; }
    public int? AmazonNewCount { get; set; }
    public int? AmazonUsedCount { get; set; }
    public int? AmazonCollectibleCount { get; set; }
    public int? SalesRank { get; set; }

    public int? LexileLevel { get; set; }

    /// <summary>Up to 3 category row keys (CATEGORY1, CATEGORY2, CATEGORY3). -1 = absent.</summary>
    public int[] CategoryRowKeys { get; set; } = Array.Empty<int>();
    /// <summary>Contributor row keys for each role column. Key = role code, Value = list of CONTRIBUTOR ROWKEYs (-1 = absent).</summary>
    public Dictionary<string, List<int>> ContributorsByRole { get; set; } = [];
    public int FullImageCount { get; set; }

    // Resolved name strings (populated at parse time from backup list files)
    public string? PublisherName { get; set; }
    public string? SeriesName { get; set; }
    public string? FormatName { get; set; }
    public string? EditionName { get; set; }
    public string? LanguageName { get; set; }
    public string? RatingName { get; set; }
    public string? ConditionName { get; set; }
    public string? LocationName { get; set; }
    public string? OwnerName { get; set; }
    public string? StatusName { get; set; }
    public string? SourceName { get; set; }
    public string? PurchasePlaceName { get; set; }
    public string? ReadingLevelName { get; set; }

    /// <summary>Resolved contributor names with roles. Populated at parse time from CONTRIBUTOR file.</summary>
    public List<(string Role, string DisplayName, string SortName)> ResolvedContributors { get; set; } = [];

    /// <summary>Resolved category names from CATEGORY_LIST. Populated at parse time.</summary>
    public List<string> ResolvedCategoryNames { get; set; } = [];
}

/// <summary>A single row shown in the dry-run preview sample table.</summary>
public record ImportSampleRow(
    int RowNumber,
    string Title,
    string? Isbn,
    string? AuthorDisplay,
    string? PublisherName,
    bool HasCover,
    string? DuplicateNote);

/// <summary>Result of the dry-run preview pass (no DB writes).</summary>
public record ImportPreview(
    int TotalRecords,
    int RecordsWithIsbn,
    int RecordsWithoutIsbn,
    int DuplicateIsbnCount,
    int RecordsWithCovers,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ImportSampleRow> SampleRows);

/// <summary>Progress update for the import progress step.</summary>
public record ImportProgress(int Processed, int Total, string CurrentTitle);

/// <summary>Final result after a completed or cancelled import.</summary>
public record ImportResult(
    int Imported,
    int Updated,
    int Skipped,
    int FlaggedNoIsbn,
    bool WasCancelled,
    IReadOnlyList<string> Errors);

/// <summary>A parsed volume from READERWARE_VOLUMES.</summary>
public record ParsedVolume(int VolumeRowKey, int BookRowKey, int VolumeNumber);

/// <summary>A parsed chapter from READERWARE_CHAPTERS.</summary>
public record ParsedChapter(int ChapterRowKey, int VolumeRowKey, int ChapterNumber);

/// <summary>A parsed borrower from BORROWER file.</summary>
public record ParsedBorrower(int RowKey, string? FirstName, string? LastName,
    string? BorrowerExternalId, string? Organization, string? Address1, string? Address2,
    string? City, string? State, string? PostalCode, string? Country,
    string? Phone1, string? Phone2, string? Email, string? Fax);

/// <summary>A parsed loan from LOANS file.</summary>
public record ParsedLoan(int RowKey, int BookRowKey, int BorrowerRowKey,
    DateTime? LoanedDate, DateTime? DueDate, DateTime? ReturnedDate, string? LoanExternalId);

/// <summary>The fully parsed backup: all books plus raw image data keyed by book RowKey.</summary>
public class ParsedBackup
{
    public IReadOnlyList<ParsedBook> Books { get; set; } = Array.Empty<ParsedBook>();
    /// <summary>Map from book RowKey to list of (ImageIndex, Data) tuples for all FULL_IMAGES entries.
    /// Decoded bytes, not hex — a hex string holds the same image at 4x the size, and a full
    /// catalog's covers must fit here simultaneously.</summary>
    public Dictionary<int, List<(int ImageIndex, byte[] Data)>> FullImagesByRowKey { get; set; } = [];
    /// <summary>Map from book RowKey to list of (ImageIndex, Data) tuples for all THUMB_IMAGES entries.</summary>
    public Dictionary<int, List<(int ImageIndex, byte[] Data)>> ThumbImagesByRowKey { get; set; } = [];
    public IReadOnlyList<ParsedVolume> Volumes { get; set; } = Array.Empty<ParsedVolume>();
    public IReadOnlyList<ParsedChapter> Chapters { get; set; } = Array.Empty<ParsedChapter>();
    public IReadOnlyList<ParsedBorrower> Borrowers { get; set; } = Array.Empty<ParsedBorrower>();
    public IReadOnlyList<ParsedLoan> Loans { get; set; } = Array.Empty<ParsedLoan>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}
