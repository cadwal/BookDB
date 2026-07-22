// Libris KB implementation uses the Xsearch endpoint (Dublin Core JSON format).
// The Libris XL /find endpoint was tested on 2026-03-24 and returned zero items
// for ISBN queries. Xsearch reliably returns Dublin Core records with title, creator,
// publisher (string or array), date, and language (3-letter MARC code e.g. "swe").
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Models;
using BookDB.Models.Metadata;

namespace BookDB.MetadataSources.Sources;

public class LibrisKbClient : IMetadataSource
{
    private readonly HttpClient _http;

    public LibrisKbClient(HttpClient http)
    {
        _http = http;
    }

    public string SourceName => "LibrisKB";

    public async Task<BookMetadata?> FetchAsync(string isbn, CancellationToken ct = default)
    {
        var normalized = IsbnNormalizer.Normalize(isbn);
        var response = await _http.GetFromJsonAsync<LibrisXsearchRoot>(
            $"xsearch?query=isbn:{normalized}&format=json&n=1", ct);

        var list = response?.Xsearch?.List;
        if (list is null || list.Count == 0)
            return null;

        var item = list[0];
        if (item is null) return null;

        // Creator is typically "LastName, FirstName, birth-death" — strip the dates and flip to display order
        var authors = new List<string>();
        if (item.Creator is not null)
        {
            // Remove trailing date ranges like ", 1962-" or ", 1903-1950"
            var clean = System.Text.RegularExpressions.Regex.Replace(item.Creator, @",?\s*\d{4}-\d{0,4}\s*$", string.Empty).Trim();
            clean = ToDisplayOrder(clean);
            if (clean.Length > 0)
                authors.Add(clean);
        }

        // Publisher can be "City : Publisher" — extract publisher name after colon if present
        var rawPublisher = item.Publisher is string s ? s
                         : item.PublisherArray?.FirstOrDefault();
        string? publisher = null;
        if (rawPublisher is not null)
        {
            var colonIndex = rawPublisher.IndexOf(':', StringComparison.Ordinal);
            publisher = colonIndex >= 0 ? rawPublisher[(colonIndex + 1)..].Trim() : rawPublisher.Trim();
        }

        // Language mapping: swe->sv, eng->en, etc.
        var language = item.Language is not null ? MapLanguageCode(item.Language) : null;

        return new BookMetadata(
            Title: item.Title,
            Subtitle: null,
            Authors: authors,
            Publisher: publisher,
            PubDate: item.Date,
            Language: language,
            Isbn: normalized,
            Pages: null,
            Description: null,
            CoverImageUrl: null,
            Series: null,
            SeriesNumber: null,
            SourceName: SourceName
        );
    }

    // Libris/MARC creators come in sort order ("Connelly, Michael"); the rest of the app expects display
    // order ("Michael Connelly"). Flip a single "Last, First" comma only — names with more than one comma
    // (a "Jr."/"Sr." suffix, or a stray fragment) are left untouched rather than risk mangling them, and a
    // corporate creator with no comma passes through unchanged.
    private static string ToDisplayOrder(string name)
    {
        var parts = name.Split(',');
        if (parts.Length != 2) return name;
        var last = parts[0].Trim();
        var first = parts[1].Trim();
        if (last.Length == 0 || first.Length == 0) return name;
        return $"{first} {last}";
    }

    private static string MapLanguageCode(string code)
    {
        return code switch
        {
            "swe" => "sv",
            "eng" => "en",
            "fre" => "fr",
            "ger" => "de",
            _ => code
        };
    }

    private class LibrisXsearchRoot
    {
        [JsonPropertyName("xsearch")]
        public LibrisXsearch? Xsearch { get; set; }
    }

    private class LibrisXsearch
    {
        [JsonPropertyName("from")]
        public int From { get; set; }

        [JsonPropertyName("to")]
        public int To { get; set; }

        [JsonPropertyName("records")]
        public int Records { get; set; }

        [JsonPropertyName("list")]
        public List<LibrisItem>? List { get; set; }
    }

    private class LibrisItem
    {
        [JsonPropertyName("identifier")]
        public string? Identifier { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("creator")]
        public string? Creator { get; set; }

        [JsonPropertyName("isbn")]
        public string? Isbn { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        // Publisher can be a string or an array in Xsearch responses
        [JsonPropertyName("publisher")]
        [JsonConverter(typeof(StringOrArrayConverter))]
        public string? Publisher { get; set; }

        [JsonIgnore]
        public List<string>? PublisherArray { get; set; }

        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }
    }

    // Xsearch returns publisher as either a string or array depending on the record.
    private class StringOrArrayConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
                return reader.GetString();

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                var values = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.String)
                        values.Add(reader.GetString() ?? string.Empty);
                }
                return values.Count > 0 ? values[0] : null;
            }

            reader.Skip();
            return null;
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (value is null)
                writer.WriteNullValue();
            else
                writer.WriteStringValue(value);
        }
    }
}
