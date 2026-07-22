using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace BookDB.Desktop.Services.UpdateCheck;

/// <summary>Per-machine update-check state: when the network check last ran and the newest version it
/// saw. Persisted outside the per-library database (the binary is shared across libraries) and never
/// bundled into DB backups.</summary>
public sealed record UpdateCheckState(DateTimeOffset? LastCheckUtc = null, string? LastSeenLatest = null);

public interface IUpdateCheckStateStore
{
    UpdateCheckState Load();
    void Save(UpdateCheckState state);
}

public sealed class UpdateCheckStateStore(string filePath, ILogger<UpdateCheckStateStore> logger)
    : IUpdateCheckStateStore
{
    public UpdateCheckState Load()
    {
        try
        {
            if (!File.Exists(filePath)) return new UpdateCheckState();
            return JsonSerializer.Deserialize<UpdateCheckState>(File.ReadAllText(filePath))
                   ?? new UpdateCheckState();
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "Could not read update-check state (ignored)");
            return new UpdateCheckState();
        }
    }

    public void Save(UpdateCheckState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, JsonSerializer.Serialize(state));
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "Could not write update-check state (ignored)");
        }
    }
}
