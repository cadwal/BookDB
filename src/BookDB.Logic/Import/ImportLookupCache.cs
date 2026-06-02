using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace BookDB.Logic.Import;

/// <summary>
/// Loads all Readerware lookup list files from a backup folder into in-memory dictionaries.
/// All list files share the same format: two columns (ROWKEY, LISTITEM), UTF-16 BE, no BOM, no extension.
/// FK value -1 = null (never look up).
/// </summary>
public sealed class ImportLookupCache
{
    private readonly Dictionary<string, Dictionary<int, string>> _tables = new(StringComparer.Ordinal);

    public static readonly string[] ListFileNames =
    {
        "FORMAT_LIST", "CATEGORY_LIST", "SERIES_LIST", "MY_RATING_LIST",
        "CONDITION_LIST", "LOCATION_LIST", "PUBLICATION_PLACE_LIST", "EDITION_LIST",
        "LANGUAGE_LIST", "OWNER_LIST", "SOURCE_LIST", "STATUS_LIST",
        "PUBLISHER_LIST", "PURCHASE_PLACE_LIST", "READING_LEVEL_LIST"
    };

    /// <summary>Load all available list files from the backup folder. Missing files are silently skipped.</summary>
    public void LoadAll(string backupFolder)
    {
        foreach (var name in ListFileNames)
        {
            var path = Path.Combine(backupFolder, name);
            if (File.Exists(path))
                _tables[name] = LoadListFile(path);
        }
    }

    /// <summary>
    /// Resolve an integer FK to its string value.
    /// Returns null if rowKey is -1, file is missing, or rowKey not found.
    /// </summary>
    public string? Resolve(string fileName, int rowKey)
    {
        if (rowKey <= 0) return null;
        return _tables.TryGetValue(fileName, out var table)
            ? table.TryGetValue(rowKey, out var value) ? value : null
            : null;
    }

    private static Dictionary<int, string> LoadListFile(string path)
    {
        var result = new Dictionary<int, string>();
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
            var rowKeyStr = csv.GetField(0);
            var listItem  = csv.GetField(1);
            if (int.TryParse(rowKeyStr, out var rowKey) && listItem is not null)
                result[rowKey] = listItem;
        }

        return result;
    }
}
