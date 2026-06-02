using System;
using System.Collections.Generic;

namespace BookDB.Models.Entities;

public class Book
{
    public int BookId { get; set; }

    public int? CollectionId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Subtitle { get; set; }

    public int? PublisherId { get; set; }

    public string? PubPlace { get; set; }

    public string? PubDate { get; set; }

    public string? CopyrightDate { get; set; }

    public int? FormatId { get; set; }

    public int? EditionId { get; set; }

    public int? Pages { get; set; }

    public int Copies { get; set; } = 1;

    public string? Isbn { get; set; }

    public int? LanguageId { get; set; }

    public int? SeriesId { get; set; }

    public string? SeriesNumber { get; set; }

    public int ReadCount { get; set; }

    public int? RatingId { get; set; }

    public int? ConditionId { get; set; }

    public int? LocationId { get; set; }

    public int? OwnerId { get; set; }

    public int? StatusId { get; set; }

    public bool Signed { get; set; }

    public bool OutOfPrint { get; set; }

    public bool Favorite { get; set; }

    public string? Keywords { get; set; }

    public string? Comments { get; set; }

    public string? BookInfo { get; set; }

    public decimal? PurchasePrice { get; set; }

    // ISO 4217 alphabetic code (e.g. "SEK", "USD", "EUR") — never a symbol or locale-specific string.
    public string? PurchaseCurrency { get; set; }

    public int? PurchasePlaceId { get; set; }

    public decimal? ListPrice { get; set; }

    // ISO 4217 alphabetic code (e.g. "SEK", "USD", "EUR") — never a symbol or locale-specific string.
    public string? ListPriceCurrency { get; set; }

    public int? SourceId { get; set; }

    public string? ExternalId { get; set; }

    public string? MediaLink { get; set; }

    /// <summary>Alternative or original title (e.g. translated books).</summary>
    public string? AltTitle { get; set; }

    /// <summary>Amazon ASIN identifier from Readerware AM_ASIN field.</summary>
    public string? AmazonAsin { get; set; }

    /// <summary>Date the book was purchased. Distinct from Added/DATE_ENTERED.</summary>
    public DateTime? PurchaseDate { get; set; }

    /// <summary>Date the user last read this book.</summary>
    public DateTime? DateLastRead { get; set; }

    public bool Display { get; set; } = true;

    public int? ReadingLevelId { get; set; }

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

    public DateTime Added { get; set; }

    public DateTime Updated { get; set; }

    public Collection? Collection { get; set; }

    public Publisher? Publisher { get; set; }

    public Format? Format { get; set; }

    public Edition? Edition { get; set; }

    public Language? Language { get; set; }

    public Series? Series { get; set; }

    public Rating? Rating { get; set; }

    public Condition? Condition { get; set; }

    public Location? Location { get; set; }

    public Owner? Owner { get; set; }

    public Status? Status { get; set; }

    public PurchasePlace? PurchasePlace { get; set; }

    public Source? Source { get; set; }

    public ReadingLevel? ReadingLevel { get; set; }

    public ICollection<BookContributor> Contributors { get; set; } = [];

    public ICollection<BookCategory> Categories { get; set; } = [];

    public ICollection<BookImage> Images { get; set; } = [];
}
