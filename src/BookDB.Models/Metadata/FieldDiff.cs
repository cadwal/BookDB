using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BookDB.Models.Metadata;

public record SourceValue(string SourceName, string? Value, bool IsSelected = false);

public record FieldDiff(string FieldName, string? CurrentValue, IReadOnlyList<SourceValue> SourceValues);

public static class FieldDiffComputer
{
    private static readonly string[] MappableFields =
    [
        "Title", "Subtitle", "Authors", "Publisher", "PubDate",
        "Language", "Pages", "Description", "Series", "SeriesNumber"
    ];

    /// <summary>
    /// Normalizes a publication date string to a canonical form:
    /// - "2005" → "2005"
    /// - "2005-03" or "2005/03" → "2005-03"
    /// - "March 2005" → "2005-03"
    /// - "2005-03-15" → "2005-03-15"
    /// - "15 March 2005" or "March 15, 2005" → "2005-03-15"
    /// Unrecognizable input is returned as-is.
    /// </summary>
    public static string? NormalizePubDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        var s = raw.Trim();

        // Already YYYY
        if (s.Length == 4 && int.TryParse(s, out _))
            return s;

        // YYYY-MM or YYYY/MM
        if (s.Length == 7)
        {
            var sep = s[4];
            if ((sep == '-' || sep == '/') && int.TryParse(s[..4], out _) && int.TryParse(s[5..], out _))
                return $"{s[..4]}-{s[5..]}";
        }

        // YYYY-MM-DD or YYYY/MM/DD
        if (s.Length == 10)
        {
            var sep = s[4];
            if (sep == '-' || sep == '/')
            {
                if (DateTime.TryParseExact(s.Replace('/', '-'), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    return d.ToString("yyyy-MM-dd");
            }
        }

        // Try parsing with common culture-aware formats
        string[] formats =
        [
            "MMMM yyyy",       // "March 2005"
            "MMM yyyy",        // "Mar 2005"
            "d MMMM yyyy",     // "15 March 2005"
            "MMMM d, yyyy",    // "March 15, 2005"
            "MMM d, yyyy",     // "Mar 15, 2005"
            "d MMM yyyy",      // "15 Mar 2005"
            "MM/dd/yyyy",
            "dd/MM/yyyy",
            "yyyy-MM-dd",
            "yyyy/MM/dd",
        ];

        foreach (var fmt in formats)
        {
            if (DateTime.TryParseExact(s, fmt,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                // Month-year only formats
                if (fmt is "MMMM yyyy" or "MMM yyyy")
                    return parsed.ToString("yyyy-MM");
                return parsed.ToString("yyyy-MM-dd");
            }
        }

        // Return as-is if unrecognized
        return s;
    }

    public static IReadOnlyList<FieldDiff> ComputeDiffs(
        IReadOnlyList<BookMetadata> sources, BookMetadata? currentBook = null)
    {
        var result = new List<FieldDiff>();

        foreach (var fieldName in MappableFields)
        {
            string? currentValue = currentBook is null ? null : GetField(currentBook, fieldName);
            var sourceValues = sources
                .Select(s => new SourceValue(s.SourceName, GetField(s, fieldName)))
                .Where(sv => sv.Value is not null)
                .ToList();

            if (sourceValues.Count == 0)
                continue;

            // Normalize PubDate values before comparison to avoid spurious diffs
            // (e.g. "2005-03-15" and "March 15, 2005" should be treated as equal)
            Func<string?, string?> normalize = fieldName == "PubDate"
                ? v => NormalizePubDate(v)
                : v => v;

            // Collect non-null source values for uniqueness check
            // Null entries (source returned nothing for this field) are excluded — they
            // don't represent a real value choice so they must not create a spurious diff.
            var nonNullSourceValues = sourceValues
                .Where(sv => sv.Value is not null)
                .Select(sv => normalize(sv.Value!.Trim()))
                .Where(v => v is not null)
                .Select(v => v!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // If current book has a value, include it in the comparison
            var allDistinctValues = nonNullSourceValues.ToList();
            if (currentBook is not null && currentValue is not null)
            {
                var trimmedCurrent = normalize(currentValue.Trim())!;
                if (!allDistinctValues.Contains(trimmedCurrent, StringComparer.OrdinalIgnoreCase))
                    allDistinctValues.Add(trimmedCurrent);
            }

            // If all non-null values agree (or only one source has data), skip
            if (allDistinctValues.Count <= 1)
                continue;

            result.Add(new FieldDiff(fieldName, currentValue, sourceValues));
        }

        return result;
    }

    private static string? GetField(BookMetadata m, string fieldName)
    {
        return fieldName switch
        {
            "Title" => m.Title,
            "Subtitle" => m.Subtitle,
            "Authors" => m.Authors.Count > 0 ? string.Join("; ", m.Authors) : null,
            "Publisher" => m.Publisher,
            "PubDate" => m.PubDate,
            "Language" => m.Language,
            "Pages" => m.Pages?.ToString(),
            "Description" => m.Description,
            "Series" => m.Series,
            "SeriesNumber" => m.SeriesNumber,
            _ => null
        };
    }
}
