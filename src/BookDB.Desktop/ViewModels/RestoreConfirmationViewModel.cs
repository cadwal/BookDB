using System;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using BookDB.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Desktop.ViewModels;

/// <summary>
/// After a CSV restore, applies the archive's preference keys (language/theme/log level) and lets the user decide
/// whether to also adopt the archive's backend + connection settings (never auto-applied). Either choice
/// restarts so the restored database — and any applied config — takes effect. The preference keys are applied on
/// both paths; only the backend/connection requires explicit confirmation.
/// </summary>
public sealed partial class RestoreConfirmationViewModel : ObservableObject
{
    private readonly BootstrapConfig _archived;
    private readonly IBootstrapConfigService _bootstrapConfig;
    private readonly IApplicationRestartService _restartService;

    public RestoreConfirmationViewModel(
        BootstrapConfig archived, IBootstrapConfigService bootstrapConfig, IApplicationRestartService restartService)
    {
        _archived = archived;
        _bootstrapConfig = bootstrapConfig;
        _restartService = restartService;

        var current = bootstrapConfig.Load();
        var archivedBackend = ParseBackend(archived.Backend);
        HasBackendChange =
            archivedBackend != ParseBackend(current.Backend)
            || (archivedBackend == DatabaseBackend.PostgreSql && archived.Postgres.AccountKey != current.Postgres.AccountKey)
            || (archivedBackend == DatabaseBackend.MySql && archived.MySql.AccountKey != current.MySql.AccountKey);
    }

    /// <summary>True when the archive's backend or connection differs from the live config, so the choice matters.</summary>
    public bool HasBackendChange { get; }

    public string ArchivedBackendName => ParseBackend(_archived.Backend) switch
    {
        DatabaseBackend.PostgreSql => Resources.Settings_Database_Backend_Postgres,
        DatabaseBackend.MySql => Resources.Settings_Database_Backend_MySql,
        _ => Resources.Settings_Database_Backend_Sqlite,
    };

    public Action? CloseDialog { get; set; }

    /// <summary>Adopt the archive's backend/connection (plus preferences) and restart.</summary>
    [RelayCommand]
    private void Apply()
    {
        _bootstrapConfig.Update(c =>
        {
            ApplyPreferences(c);
            c.Backend = _archived.Backend;
            // Adopt only the connection block matching the archive's backend; leave the others as configured.
            switch (ParseBackend(_archived.Backend))
            {
                case DatabaseBackend.PostgreSql: c.Postgres = _archived.Postgres; break;
                case DatabaseBackend.MySql: c.MySql = _archived.MySql; break;
            }
        });
        CloseDialog?.Invoke();
        _restartService.Restart();
    }

    /// <summary>Keep the current backend/connection; still apply preferences and restart to load the restored data.</summary>
    [RelayCommand]
    private void KeepCurrent()
    {
        _bootstrapConfig.Update(ApplyPreferences);
        CloseDialog?.Invoke();
        _restartService.Restart();
    }

    private void ApplyPreferences(BootstrapConfig config)
    {
        if (_archived.Language is not null) config.Language = _archived.Language;
        if (_archived.UiTheme is not null) config.UiTheme = _archived.UiTheme;
        if (_archived.LogLevel is not null) config.LogLevel = _archived.LogLevel;
    }

    private static DatabaseBackend ParseBackend(string? backend)
        => Enum.TryParse(backend, ignoreCase: true, out DatabaseBackend parsed) ? parsed : DatabaseBackend.Sqlite;
}
