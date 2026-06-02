using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Models;
using BookDB.Models.Metadata;

namespace BookDB.MetadataSources.Sources;

public class IsbnSearchOrgClient : IMetadataSource
{
    private readonly HttpClient _http;

    public IsbnSearchOrgClient(HttpClient http)
    {
        _http = http;
    }

    public string SourceName => "IsbnSearchOrg";

    public async Task<BookMetadata?> FetchAsync(string isbn, CancellationToken ct = default)
    {
        var normalized = IsbnNormalizer.Normalize(isbn);
        
        try
        {
            var html = await _http.GetStringAsync($"isbn/{normalized}", ct);

            // If the book details container isn't present, the book wasn't found
            if (!html.Contains("id=\"book\"") && !html.Contains("class=\"bookinfo\""))
            {
                return null;
            }

            var titleMatch = Regex.Match(html, @"<h1>\s*(.*?)\s*</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var authorMatch = Regex.Match(html, @"<strong>Author:</strong>\s*(.*?)\s*</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var publisherMatch = Regex.Match(html, @"<strong>Publisher:</strong>\s*(.*?)\s*</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var publishedMatch = Regex.Match(html, @"<strong>Published:</strong>\s*(.*?)\s*</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var coverMatch = Regex.Match(html, @"<div class=""image"">\s*<img src=""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!titleMatch.Success)
            {
                return null;
            }

            var title = HtmlEntityDecode(titleMatch.Groups[1].Value.Trim());
            
            var authors = new List<string>();
            if (authorMatch.Success)
            {
                var rawAuthor = HtmlEntityDecode(authorMatch.Groups[1].Value.Trim());
                if (!string.IsNullOrEmpty(rawAuthor))
                {
                    foreach (var auth in rawAuthor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        authors.Add(auth);
                    }
                }
            }

            string? publisher = publisherMatch.Success ? HtmlEntityDecode(publisherMatch.Groups[1].Value.Trim()) : null;
            string? pubDate = publishedMatch.Success ? HtmlEntityDecode(publishedMatch.Groups[1].Value.Trim()) : null;
            string? coverUrl = coverMatch.Success ? coverMatch.Groups[1].Value.Trim() : null;

            if (coverUrl is not null && coverUrl.StartsWith('/'))
            {
                coverUrl = $"https://isbnsearch.org{coverUrl}";
            }

            return new BookMetadata(
                Title: title,
                Subtitle: null,
                Authors: authors,
                Publisher: publisher,
                PubDate: pubDate,
                Language: null,
                Isbn: normalized,
                Pages: null,
                Description: null,
                CoverImageUrl: coverUrl,
                Series: null,
                SeriesNumber: null,
                SourceName: SourceName
            );
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static string HtmlEntityDecode(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return System.Net.WebUtility.HtmlDecode(input);
    }
}
