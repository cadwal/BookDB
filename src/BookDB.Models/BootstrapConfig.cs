using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BookDB.Models;

/// <summary>
/// Local bootstrap configuration (<c>%APPDATA%/BookDB/config.json</c>): the active backend
/// choice, server connection parameters, and the three pre-DI settings (language, theme, log
/// level). It resolves the chicken-and-egg where the backend choice cannot live in the database
/// it points at, and lets the app read these before any DI or network round-trip.
/// </summary>
/// <remarks>
/// <see cref="Backend"/> is intentionally an open string, not a closed enum: additional backends
/// (e.g. MySQL) can be added in later versions without breaking older readers. Unknown backend
/// values and unknown sibling blocks are tolerated on read rather than rejected.
/// </remarks>
public sealed class BootstrapConfig
{
    public int Version { get; set; } = 1;
    public string Backend { get; set; } = "Sqlite";
    public PostgresOptions Postgres { get; set; } = new();
    public MySqlOptions MySql { get; set; } = new();
    public string? Language { get; set; }
    public string? UiTheme { get; set; }
    public string? LogLevel { get; set; }

    /// <summary>
    /// Mobile-scanner companion settings. Machine-scoped like the backend choice above — the companion
    /// belongs to this desktop install, not to whichever library it happens to have open.
    /// </summary>
    public CompanionOptions Companion { get; set; } = new();

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// Reads the configuration from <paramref name="path"/>. Returns <c>null</c> when the file
    /// does not exist; returns a defaults instance (never throwing) when the file exists but
    /// cannot be parsed. Unknown properties are ignored, so a config written by a newer build
    /// loads without error.
    /// </summary>
    public static BootstrapConfig? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<BootstrapConfig>(json, SerializerOptions) ?? new BootstrapConfig();
        }
        catch (JsonException)
        {
            return new BootstrapConfig();
        }
    }

    /// <summary>
    /// Writes the configuration to <paramref name="path"/> atomically (write to a temporary file,
    /// then replace), creating the containing directory if it does not exist.
    /// </summary>
    public void Save(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(this, SerializerOptions));
        File.Move(tempPath, path, overwrite: true);
    }
}

public sealed class CompanionOptions
{
    /// <summary>Off by default: the listener never opens, and there is no firewall prompt, until asked for.</summary>
    public bool Enabled { get; set; }

    /// <summary>High port so binding never needs elevation. See <c>docs</c>/the companion help topic.</summary>
    public int Port { get; set; } = 7443;

    /// <summary>Longest edge, in pixels, the phone downscales every captured photo to before sending.</summary>
    public int MaxLongEdgePx { get; set; } = 1600;

    /// <summary>JPEG quality (1–100) the phone encodes at.</summary>
    public int JpegQuality { get; set; } = 80;

    /// <summary>
    /// Identifies this desktop install across LAN-address changes, so a paired phone can tell "same
    /// desktop, new IP" from "a different desktop". Minted once, the first time the companion is enabled.
    /// </summary>
    public string? InstanceId { get; set; }
}

public sealed class PostgresOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "bookdb";
    public string Username { get; set; } = string.Empty;
    public string SslMode { get; set; } = "Require";

    /// <summary>
    /// The OS credential-store account key for this server's password, so multiple server configurations do
    /// not collide on one key. The password itself is never stored in config.json.
    /// </summary>
    [JsonIgnore]
    public string AccountKey => $"{Username}@{Host}:{Port}/{Database}";
}
