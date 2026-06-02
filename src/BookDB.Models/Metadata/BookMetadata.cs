using System.Collections.Generic;

namespace BookDB.Models.Metadata;

public record BookMetadata(
    string? Title,
    string? Subtitle,
    IReadOnlyList<string> Authors,
    string? Publisher,
    string? PubDate,
    string? Language,
    string? Isbn,
    int? Pages,
    string? Description,
    string? CoverImageUrl,
    string? Series,
    string? SeriesNumber,
    string SourceName
);
