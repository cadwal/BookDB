using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Models;
using BookDB.Models.Metadata;

namespace BookDB.MetadataSources.Sources;

public class OpenLibraryClient : IMetadataSource
{
    private readonly HttpClient _http;

    public OpenLibraryClient(HttpClient http)
    {
        _http = http;
    }

    public string SourceName => "OpenLibrary";

    public async Task<BookMetadata?> FetchAsync(string isbn, CancellationToken ct = default)
    {
        var normalized = IsbnNormalizer.Normalize(isbn);
        var response = await _http.GetFromJsonAsync<Dictionary<string, OpenLibraryBook>>(
            $"api/books?bibkeys=ISBN:{normalized}&format=json&jscmd=data", ct);

        if (response is null || response.Count == 0)
            return null;

        var book = response.Values.First();

        var language = book.Languages?.FirstOrDefault()?.Key is string langKey
            ? MapLanguageCode(langKey.Split('/')[^1])
            : null;

        return new BookMetadata(
            Title: book.Title,
            Subtitle: book.Subtitle,
            Authors: (IReadOnlyList<string>?)book.Authors?.Select(a => a.Name).ToList() ?? Array.Empty<string>(),
            Publisher: book.Publishers?.FirstOrDefault()?.Name,
            PubDate: book.PublishDate,
            Language: language,
            Isbn: normalized,
            Pages: book.NumberOfPages,
            Description: null,
            CoverImageUrl: book.Cover?.Medium ?? book.Cover?.Small,
            Series: null,
            SeriesNumber: null,
            SourceName: SourceName
        );
    }

    // Public for testability
    public static string MapLanguageCode(string code)
    {
        return code switch
        {
            "eng" => "en",
            "swe" => "sv",
            "fre" => "fr",
            "ger" => "de",
            _ => code
        };
    }

    private class OpenLibraryBook
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("subtitle")]
        public string? Subtitle { get; set; }

        [JsonPropertyName("authors")]
        public List<OpenLibraryAuthor>? Authors { get; set; }

        [JsonPropertyName("publishers")]
        public List<OpenLibraryPublisher>? Publishers { get; set; }

        [JsonPropertyName("publish_date")]
        public string? PublishDate { get; set; }

        [JsonPropertyName("languages")]
        public List<OpenLibraryLanguage>? Languages { get; set; }

        [JsonPropertyName("number_of_pages")]
        public int? NumberOfPages { get; set; }

        [JsonPropertyName("cover")]
        public OpenLibraryCover? Cover { get; set; }
    }

    private class OpenLibraryAuthor
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private class OpenLibraryPublisher
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private class OpenLibraryLanguage
    {
        [JsonPropertyName("key")]
        public string? Key { get; set; }
    }

    private class OpenLibraryCover
    {
        [JsonPropertyName("small")]
        public string? Small { get; set; }

        [JsonPropertyName("medium")]
        public string? Medium { get; set; }

        [JsonPropertyName("large")]
        public string? Large { get; set; }
    }
}
