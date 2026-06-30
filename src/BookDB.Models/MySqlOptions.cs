using System.Text.Json.Serialization;

namespace BookDB.Models;

public sealed class MySqlOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 3306;
    public string Database { get; set; } = "bookdb";
    public string Username { get; set; } = string.Empty;
    public string SslMode { get; set; } = "Preferred";

    /// <summary>
    /// The OS credential-store account key for this server's password, so multiple server configurations do
    /// not collide on one key. The password itself is never stored in config.json.
    /// </summary>
    [JsonIgnore]
    public string AccountKey => $"{Username}@{Host}:{Port}/{Database}";
}
