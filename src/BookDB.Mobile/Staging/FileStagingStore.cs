using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using BookDB.Contracts;
using BookDB.Mobile.Services;

namespace BookDB.Mobile.Staging;

/// <summary>
/// Keeps staged books in app-private storage: a manifest naming them, and a folder of JPEGs per book. The
/// manifest is the truth — a folder it does not name is swept, and an image it names but that is not on disk
/// is dropped — so a kill part-way through a save leaves a smaller set, never a broken one.
/// </summary>
public sealed class FileStagingStore : IStagingStore
{
    /// <summary>Big enough for a tray row on a dense screen, small enough that a long tray costs nothing.</summary>
    private const int ThumbnailLongEdgePx = 160;

    private const string ManifestName = "manifest.json";

    private readonly string _root;
    private readonly string _manifestPath;
    private readonly List<StagedItem> _items = [];

    public FileStagingStore(string directory)
    {
        _root = Path.Combine(directory, "staging");
        Directory.CreateDirectory(_root);
        _manifestPath = Path.Combine(_root, ManifestName);
        Load();
    }

    public IReadOnlyList<StagedItem> Items => _items;

    public event EventHandler? Changed;

    public void Save(StagedBook book)
    {
        string folder = FolderFor(book.ClientItemId);

        // A replace has to start clean, or a cover removed on a re-take would survive on disk.
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);

        Directory.CreateDirectory(folder);
        foreach (var image in book.Images)
            File.WriteAllBytes(Path.Combine(folder, image.Type + ".jpg"), image.Jpeg);

        var thumbnail = Thumbnail(book);
        if (thumbnail is not null)
            File.WriteAllBytes(Path.Combine(folder, "thumb.jpg"), thumbnail);

        int existing = _items.FindIndex(item => item.ClientItemId == book.ClientItemId);
        var staged = new StagedItem
        {
            ClientItemId = book.ClientItemId,
            Isbn = book.Isbn,
            ImageTypes = [.. book.Images.Select(image => image.Type)],
            StagedAt = existing >= 0 ? _items[existing].StagedAt : DateTimeOffset.UtcNow,
            Thumbnail = thumbnail,
        };

        if (existing >= 0)
            _items[existing] = staged;
        else
            _items.Add(staged);

        WriteManifest();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string clientItemId)
    {
        if (_items.RemoveAll(item => item.ClientItemId == clientItemId) == 0)
            return;

        string folder = FolderFor(clientItemId);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);

        WriteManifest();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public byte[]? ReadImage(string clientItemId, ScanImageType type)
    {
        string path = Path.Combine(FolderFor(clientItemId), type + ".jpg");
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    private static byte[]? Thumbnail(StagedBook book)
    {
        // The front cover is what the tray wants; any cover beats none when there is no front one.
        var source = book.Images.FirstOrDefault(image => image.Type == ScanImageType.FrontCover)
            ?? book.Images.FirstOrDefault();

        return source is null
            ? null
            : ScanImageEncoder.Encode(source.Jpeg, new CaptureSettings { MaxLongEdgePx = ThumbnailLongEdgePx, JpegQuality = 70 });
    }

    /// <summary>Ids are minted by the app as hex, and this one becomes a directory name — anything else is a
    /// bug worth failing on rather than a path to follow.</summary>
    private string FolderFor(string clientItemId)
    {
        if (clientItemId.Length == 0 || !clientItemId.All(char.IsAsciiLetterOrDigit))
            throw new ArgumentException("A staged item id must be alphanumeric.", nameof(clientItemId));

        return Path.Combine(_root, clientItemId);
    }

    private void Load()
    {
        foreach (var entry in ReadManifest())
        {
            string folder = Path.Combine(_root, entry.ClientItemId);
            if (!Directory.Exists(folder))
                continue;

            var types = entry.Images
                .Select(name => Enum.TryParse<ScanImageType>(name, out var type) ? type : (ScanImageType?)null)
                .Where(type => type is not null && File.Exists(Path.Combine(folder, type + ".jpg")))
                .Select(type => type!.Value)
                .ToList();

            string thumbPath = Path.Combine(folder, "thumb.jpg");
            _items.Add(new StagedItem
            {
                ClientItemId = entry.ClientItemId,
                Isbn = entry.Isbn,
                ImageTypes = types,
                StagedAt = entry.StagedAt,
                Thumbnail = File.Exists(thumbPath) ? File.ReadAllBytes(thumbPath) : null,
            });
        }

        Sweep();
    }

    private List<StagedEntry> ReadManifest()
    {
        if (!File.Exists(_manifestPath))
            return [];

        try
        {
            var manifest = JsonSerializer.Deserialize(File.ReadAllText(_manifestPath), StagingJsonContext.Default.StagedManifest);
            return manifest?.Items
                .Where(entry => entry.ClientItemId.Length > 0 && entry.ClientItemId.All(char.IsAsciiLetterOrDigit))
                .OrderBy(entry => entry.StagedAt)
                .ToList() ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A manifest that cannot be read leaves nothing staged rather than stopping the app from starting;
            // the sweep below then clears the orphaned photos.
            return [];
        }
    }

    /// <summary>Deletes folders the manifest does not name — the leavings of a save that was killed before it
    /// could record what it had written.</summary>
    private void Sweep()
    {
        var known = _items.Select(item => item.ClientItemId).ToHashSet(StringComparer.Ordinal);

        foreach (string folder in Directory.GetDirectories(_root))
        {
            if (!known.Contains(Path.GetFileName(folder)))
                Directory.Delete(folder, recursive: true);
        }
    }

    private void WriteManifest()
    {
        var manifest = new StagedManifest
        {
            Items = [.. _items.Select(item => new StagedEntry
            {
                ClientItemId = item.ClientItemId,
                Isbn = item.Isbn,
                StagedAt = item.StagedAt,
                Images = [.. item.ImageTypes.Select(type => type.ToString())],
            })],
        };

        // Written beside the real file and moved into place, so a kill mid-write cannot leave a half-manifest.
        string temporary = _manifestPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(manifest, StagingJsonContext.Default.StagedManifest));
        File.Move(temporary, _manifestPath, overwrite: true);
    }
}

internal sealed class StagedManifest
{
    public List<StagedEntry> Items { get; set; } = [];
}

internal sealed class StagedEntry
{
    public string ClientItemId { get; set; } = "";
    public string Isbn { get; set; } = "";
    public DateTimeOffset StagedAt { get; set; }

    /// <summary>Image types by name. Names rather than the wire numbers: this file outlives no contract, and a
    /// name that is no longer known is simply skipped.</summary>
    public List<string> Images { get; set; } = [];
}

// Source-generated so the store still works on a trimmed mobile head.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StagedManifest))]
internal sealed partial class StagingJsonContext : JsonSerializerContext
{
}
