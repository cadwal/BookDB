using System;

namespace BookDB.Models.Entities;

/// <summary>
/// One running client's presence in a shared database, used to detect concurrent access to a remote backend.
/// Written on connect and refreshed on a timer; a row older than the staleness window is treated as a crashed
/// client and ignored. Never part of a backup or a library migration — it describes live processes, not data.
/// </summary>
public sealed class ClientSession
{
    public string SessionId { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
}
