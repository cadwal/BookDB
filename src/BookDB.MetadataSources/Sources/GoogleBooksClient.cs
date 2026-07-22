using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Models;
using BookDB.Models.Metadata;

namespace BookDB.MetadataSources.Sources;

public class GoogleBooksClient : IMetadataSource
{
    private readonly HttpClient _http;
    private readonly IGoogleBooksApiKeyAccessor? _apiKey;

    public GoogleBooksClient(HttpClient http, IGoogleBooksApiKeyAccessor? apiKey = null)
    {
        _http = http;
        _apiKey = apiKey;
    }

    public string SourceName => "GoogleBooks";

    public async Task<BookMetadata?> FetchAsync(string isbn, CancellationToken ct = default)
    {
        var normalized = IsbnNormalizer.Normalize(isbn);
        var query = $"volumes?q=isbn:{normalized}";
        // A personal key moves the request off the shared anonymous per-IP daily quota, which is
        // routinely exhausted (429). Absent a key, fall back to the anonymous quota.
        if (_apiKey?.ApiKey is { Length: > 0 } key)
            query += $"&key={Uri.EscapeDataString(key)}";
        var response = await _http.GetFromJsonAsync<GoogleBooksResponse>(query, ct);

        if (response?.Items is null || response.Items.Count == 0)
            return null;

        var info = response.Items[0].VolumeInfo;
        if (info is null)
            return null;

        return new BookMetadata(
            Title: info.Title,
            Subtitle: info.Subtitle,
            Authors: (IReadOnlyList<string>?)info.Authors ?? Array.Empty<string>(),
            Publisher: info.Publisher,
            PubDate: info.PublishedDate,
            Language: info.Language,
            Isbn: normalized,
            Pages: info.PageCount,
            Description: info.Description,
            CoverImageUrl: CleanCoverUrl(info.ImageLinks?.Thumbnail),
            Series: null,
            SeriesNumber: null,
            SourceName: SourceName
        );
    }

    private static string? CleanCoverUrl(string? url)
    {
        if (url is null) return null;
        url = url.Replace("http://", "https://");
        url = System.Text.RegularExpressions.Regex.Replace(url, @"&edge=curl", string.Empty);
        return url;
    }

    private class GoogleBooksResponse
    {
        [JsonPropertyName("items")]
        public List<GoogleBooksItem>? Items { get; set; }
    }

    private class GoogleBooksItem
    {
        [JsonPropertyName("volumeInfo")]
        public GoogleBooksVolumeInfo? VolumeInfo { get; set; }
    }

    private class GoogleBooksVolumeInfo
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("subtitle")]
        public string? Subtitle { get; set; }

        [JsonPropertyName("authors")]
        public List<string>? Authors { get; set; }

        [JsonPropertyName("publisher")]
        public string? Publisher { get; set; }

        [JsonPropertyName("publishedDate")]
        public string? PublishedDate { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("pageCount")]
        public int? PageCount { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("imageLinks")]
        public GoogleBooksImageLinks? ImageLinks { get; set; }
    }

    private class GoogleBooksImageLinks
    {
        [JsonPropertyName("thumbnail")]
        public string? Thumbnail { get; set; }
    }
}
