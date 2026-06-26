using System;
using BookDB.Models;

namespace BookDB.Desktop.Services;

/// <summary>
/// Read/write access to the local bootstrap config file (<c>config.json</c>) for the settings the
/// file owns rather than the in-database Settings table — the active backend, server connection
/// parameters, and the three pre-DI settings (Language, UiTheme, LogLevel).
/// </summary>
public interface IBootstrapConfigService
{
    /// <summary>Reads the current config from disk; returns defaults when the file is absent or unreadable.</summary>
    BootstrapConfig Load();

    /// <summary>Loads the current config, applies <paramref name="mutate"/>, and saves it back atomically.</summary>
    void Update(Action<BootstrapConfig> mutate);
}

public sealed class BootstrapConfigService : IBootstrapConfigService
{
    private readonly string _configPath;

    public BootstrapConfigService(string configPath) => _configPath = configPath;

    public BootstrapConfig Load() => BootstrapConfig.Load(_configPath) ?? new BootstrapConfig();

    public void Update(Action<BootstrapConfig> mutate)
    {
        BootstrapConfig config = Load();
        mutate(config);
        config.Save(_configPath);
    }
}
