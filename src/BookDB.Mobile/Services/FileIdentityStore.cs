using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BookDB.Mobile.Services;

/// <summary>Stores the paired identity as a single JSON file in the app-private directory. One device is
/// paired at a time, so saving replaces whatever was there.</summary>
public sealed class FileIdentityStore : IIdentityStore
{
    private readonly string _path;

    public FileIdentityStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "identity.json");
    }

    // A present-but-corrupt file counts as "not paired": Load returns null for it, and the shell must route
    // such a device to pairing rather than to a hub it can't actually reach.
    public bool HasIdentity => Load() is not null;

    public DeviceIdentity? Load()
    {
        if (!File.Exists(_path))
            return null;

        try
        {
            var stored = JsonSerializer.Deserialize(File.ReadAllText(_path), StoredIdentityJsonContext.Default.StoredIdentity);
            if (stored is null || string.IsNullOrEmpty(stored.Endpoint) || string.IsNullOrEmpty(stored.ClientPfxBase64))
                return null;

            return new DeviceIdentity(
                stored.Endpoint,
                stored.ServerThumbprints,
                Convert.FromBase64String(stored.ClientPfxBase64),
                stored.DeviceName,
                stored.InstanceId);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            // A corrupt identity file is treated as "not paired" rather than crashing the app on launch.
            return null;
        }
    }

    public void Save(DeviceIdentity identity)
    {
        var stored = new StoredIdentity
        {
            Endpoint = identity.Endpoint,
            ServerThumbprints = [.. identity.ServerThumbprints],
            ClientPfxBase64 = Convert.ToBase64String(identity.ClientPfx),
            DeviceName = identity.DeviceName,
            InstanceId = identity.InstanceId,
        };
        File.WriteAllText(_path, JsonSerializer.Serialize(stored, StoredIdentityJsonContext.Default.StoredIdentity));
    }

    public void Clear()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }
}

internal sealed class StoredIdentity
{
    public string Endpoint { get; set; } = "";
    public string[] ServerThumbprints { get; set; } = [];
    public string ClientPfxBase64 { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string InstanceId { get; set; } = "";
}

// Source-generated so the store still works on a trimmed mobile head.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StoredIdentity))]
internal sealed partial class StoredIdentityJsonContext : JsonSerializerContext
{
}
