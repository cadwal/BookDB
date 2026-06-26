using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Data.PostgreSQL;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using BookDB.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BookDB.Desktop.ViewModels;

/// <summary>A selectable TLS/SSL mode; <see cref="Value"/> is the Npgsql <c>SslMode</c> token stored in config.json.</summary>
public sealed record SslModeOption(string Value, string DisplayName);

/// <summary>
/// The Settings → Database tab: choose the backend (local SQLite or a PostgreSQL server) and edit the server
/// connection parameters. The shared Save button commits a backend/connection change through
/// <see cref="SaveAsync"/>; <see cref="ValidateForSave"/> gates it and <see cref="DbChanged"/> tells the window
/// a restart is needed — the same restart-on-Save path that language, theme, and log level already use.
/// </summary>
public sealed partial class DatabaseSettingsViewModel : ObservableObject
{
    private readonly IBootstrapConfigService _bootstrapConfig;
    private readonly IPostgresConnectionProber _prober;
    private readonly ISecretStore _secretStore;

    // Baseline captured at load, so IsDirty reflects edits the user actually made this session.
    private bool _baseIsPostgres;
    private string _baseHost = string.Empty;
    private string _basePort = string.Empty;
    private string _baseDatabase = string.Empty;
    private string _baseUsername = string.Empty;
    private string _basePassword = string.Empty;
    private string _baseSslMode = string.Empty;

    [ObservableProperty]
    private bool _isSqliteSelected = true;

    [ObservableProperty]
    private bool _isPostgreSqlSelected;

    [ObservableProperty]
    private string _host = string.Empty;

    [ObservableProperty]
    private string _port = "5432";

    [ObservableProperty]
    private string _databaseName = "bookdb";

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private SslModeOption? _selectedSslMode;

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private string? _testResultMessage;

    [ObservableProperty]
    private bool _testResultIsError;

    [ObservableProperty]
    private string? _applyErrorMessage;

    public IReadOnlyList<SslModeOption> AvailableSslModes { get; } =
    [
        new SslModeOption("Disable",    Resources.Settings_Database_Tls_Disable),
        new SslModeOption("Prefer",     Resources.Settings_Database_Tls_Prefer),
        new SslModeOption("Require",    Resources.Settings_Database_Tls_Require),
        new SslModeOption("VerifyFull", Resources.Settings_Database_Tls_VerifyFull),
    ];

    /// <summary>Whether an OS credential store exists; PostgreSQL is unavailable without one (no plaintext fallback).</summary>
    public bool IsKeyringAvailable { get; }

    public string KeyringUnavailableMessage => Resources.Settings_Database_KeyringUnavailable;

    /// <summary>True when TLS is set to Disable, surfacing the plaintext-network warning in the view.</summary>
    public bool ShowTlsDisableWarning => SelectedSslMode?.Value == "Disable";

    /// <summary>True once a test-connection result is available, gating its inline display.</summary>
    public bool HasTestResult => !string.IsNullOrEmpty(TestResultMessage);

    /// <summary>True when the Apply flow has a blocking validation message to show inline.</summary>
    public bool HasApplyError => !string.IsNullOrEmpty(ApplyErrorMessage);

    /// <summary>True when the credential store already holds a password for the current connection, so Save can
    /// proceed without re-entering it (the field stays blank by design). Drives the saved-password hint.</summary>
    public bool HasSavedPassword =>
        IsPostgreSqlSelected && !string.IsNullOrEmpty(_secretStore.Get(BuildOptions().AccountKey));

    /// <summary>True when the user changed any field from its loaded value — drives the Apply enablement.</summary>
    public bool IsDirty =>
        IsPostgreSqlSelected != _baseIsPostgres ||
        Host != _baseHost ||
        Port != _basePort ||
        DatabaseName != _baseDatabase ||
        Username != _baseUsername ||
        Password != _basePassword ||
        (SelectedSslMode?.Value ?? string.Empty) != _baseSslMode;

    /// <summary>True once <see cref="SaveAsync"/> has written a backend/connection change this session, so the
    /// window includes the Database tab in its restart-needed decision.</summary>
    public bool DbChanged { get; private set; }

    public DatabaseSettingsViewModel(
        IBootstrapConfigService bootstrapConfig,
        SecretStoreAvailability secretStoreAvailability,
        IPostgresConnectionProber prober,
        ISecretStore secretStore)
    {
        _bootstrapConfig = bootstrapConfig;
        _prober = prober;
        _secretStore = secretStore;
        IsKeyringAvailable = secretStoreAvailability.IsAvailable;
        SelectedSslMode = AvailableSslModes.First(s => s.Value == "Require");
    }

    /// <summary>Probes the entered PostgreSQL parameters and shows an inline, classified result.</summary>
    [RelayCommand(CanExecute = nameof(CanTestConnection))]
    private async Task TestConnectionAsync()
    {
        IsTesting = true;
        TestResultMessage = null;
        try
        {
            var options = BuildOptions();
            // Use the freshly entered password if any; otherwise fall back to the one already stored for this
            // connection (the field is blank by design when a password is saved), so Test mirrors how the live
            // connection authenticates instead of probing with no password.
            var password = string.IsNullOrEmpty(Password) ? _secretStore.Get(options.AccountKey) : Password;
            var result = await _prober.ProbeAsync(options, string.IsNullOrEmpty(password) ? null : password);
            ApplyResult(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DatabaseSettingsViewModel: test connection failed unexpectedly");
            TestResultIsError = true;
            TestResultMessage = string.Format(
                CultureInfo.CurrentCulture, Resources.Settings_Database_TestError_Unknown, ex.Message);
        }
        finally
        {
            IsTesting = false;
        }
    }

    private bool CanTestConnection() => IsPostgreSqlSelected && !IsTesting && !string.IsNullOrWhiteSpace(Host);

    private void ApplyResult(ConnectionProbeResult result)
    {
        TestResultIsError = !result.IsSuccess;
        TestResultMessage = result.Status switch
        {
            ConnectionProbeStatus.Success => result.BookCount.HasValue
                ? string.Format(CultureInfo.CurrentCulture, Resources.Settings_Database_TestSuccess, result.ServerVersion, result.BookCount.Value)
                : string.Format(CultureInfo.CurrentCulture, Resources.Settings_Database_TestSuccessUninitialized, result.ServerVersion),
            ConnectionProbeStatus.AuthenticationFailed => Resources.Settings_Database_TestError_Auth,
            ConnectionProbeStatus.ConnectionRefused => Resources.Settings_Database_TestError_Refused,
            ConnectionProbeStatus.Timeout => Resources.Settings_Database_TestError_Timeout,
            ConnectionProbeStatus.TlsError => Resources.Settings_Database_TestError_Tls,
            ConnectionProbeStatus.UnsupportedServerVersion => string.Format(
                CultureInfo.CurrentCulture,
                Resources.Settings_Database_TestError_UnsupportedVersion,
                PostgresConnectionProber.MinimumServerMajorVersion,
                result.ErrorDetail ?? string.Empty),
            _ => string.Format(CultureInfo.CurrentCulture, Resources.Settings_Database_TestError_Unknown, result.ErrorDetail ?? string.Empty),
        };
    }

    /// <summary>
    /// Gate for the shared Save: a PostgreSQL backend needs a password — entered now or already stored. Returns
    /// false with an inline error when it is missing, so Save can abort instead of writing a backend that can't
    /// authenticate. Cheap and side-effect-free; call it before <see cref="SaveAsync"/>.
    /// </summary>
    public bool ValidateForSave()
    {
        ApplyErrorMessage = null;
        if (IsPostgreSqlSelected && string.IsNullOrEmpty(Password)
            && string.IsNullOrEmpty(_secretStore.Get(BuildOptions().AccountKey)))
        {
            ApplyErrorMessage = Resources.Settings_Database_Apply_PasswordRequired;
            return false;
        }
        return true;
    }

    private PostgresOptions BuildOptions() => new()
    {
        Host = Host.Trim(),
        Port = int.TryParse(Port, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ? port : 5432,
        Database = DatabaseName.Trim(),
        Username = Username.Trim(),
        SslMode = SelectedSslMode?.Value ?? "Require",
    };

    public Task LoadAsync(CancellationToken ct = default)
    {
        var config = _bootstrapConfig.Load();
        var pg = config.Postgres;

        Host = pg.Host;
        Port = pg.Port.ToString(CultureInfo.InvariantCulture);
        DatabaseName = string.IsNullOrWhiteSpace(pg.Database) ? "bookdb" : pg.Database;
        Username = pg.Username;
        Password = string.Empty; // never read back from the credential store into the UI
        SelectedSslMode = AvailableSslModes.FirstOrDefault(s =>
            string.Equals(s.Value, pg.SslMode, StringComparison.OrdinalIgnoreCase))
            ?? AvailableSslModes.First(s => s.Value == "Require");

        // Honour the stored backend only when a keyring is present; otherwise PostgreSQL cannot be selected.
        var postgresStored = ParseBackend(config.Backend) == DatabaseBackend.PostgreSql;
        IsPostgreSqlSelected = postgresStored && IsKeyringAvailable;
        IsSqliteSelected = !IsPostgreSqlSelected;

        CaptureBaseline();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Commits a backend/connection change when the user edited this tab: stores the password (when entered),
    /// writes config.json, and sets <see cref="DbChanged"/> so the window prompts the restart. A no-op when
    /// nothing changed. <see cref="ValidateForSave"/> must pass first.
    /// </summary>
    public Task SaveAsync(CancellationToken ct = default)
    {
        DbChanged = false;
        if (!IsDirty)
            return Task.CompletedTask;

        var options = BuildOptions();
        if (IsPostgreSqlSelected && !string.IsNullOrEmpty(Password))
            _secretStore.Set(options.AccountKey, Password);

        var targetBackend = (IsPostgreSqlSelected ? DatabaseBackend.PostgreSql : DatabaseBackend.Sqlite).ToString();
        _bootstrapConfig.Update(c =>
        {
            c.Backend = targetBackend;
            c.Postgres = options;
        });

        DbChanged = true;
        CaptureBaseline();
        return Task.CompletedTask;
    }

    private void CaptureBaseline()
    {
        _baseIsPostgres = IsPostgreSqlSelected;
        _baseHost = Host;
        _basePort = Port;
        _baseDatabase = DatabaseName;
        _baseUsername = Username;
        _basePassword = Password;
        _baseSslMode = SelectedSslMode?.Value ?? string.Empty;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(HasSavedPassword)); // after a Save the password is now stored
    }

    partial void OnIsSqliteSelectedChanged(bool value)
    {
        if (value)
            IsPostgreSqlSelected = false;
    }

    partial void OnIsPostgreSqlSelectedChanged(bool value)
    {
        // Guard: PostgreSQL needs a keyring; veto the selection and fall back to SQLite when none is available.
        if (value && !IsKeyringAvailable)
        {
            IsPostgreSqlSelected = false;
            return;
        }

        if (value)
            IsSqliteSelected = false;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        switch (e.PropertyName)
        {
            case nameof(IsSqliteSelected):
            case nameof(IsPostgreSqlSelected):
            case nameof(Host):
            case nameof(Port):
            case nameof(DatabaseName):
            case nameof(Username):
            case nameof(Password):
                OnPropertyChanged(nameof(IsDirty));
                break;
        }

        // The saved-password lookup keys off the backend + connection identity (AccountKey).
        switch (e.PropertyName)
        {
            case nameof(IsPostgreSqlSelected):
            case nameof(Host):
            case nameof(Port):
            case nameof(DatabaseName):
            case nameof(Username):
                OnPropertyChanged(nameof(HasSavedPassword));
                break;
            case nameof(SelectedSslMode):
                OnPropertyChanged(nameof(ShowTlsDisableWarning));
                OnPropertyChanged(nameof(IsDirty));
                break;
        }

        if (e.PropertyName == nameof(ApplyErrorMessage))
            OnPropertyChanged(nameof(HasApplyError));

        switch (e.PropertyName)
        {
            case nameof(IsPostgreSqlSelected):
                TestResultMessage = null; // a stale result from the other backend must not linger
                TestConnectionCommand.NotifyCanExecuteChanged();
                break;
            case nameof(IsTesting):
            case nameof(Host):
                TestConnectionCommand.NotifyCanExecuteChanged();
                break;
            case nameof(TestResultMessage):
                OnPropertyChanged(nameof(HasTestResult));
                break;
        }
    }

    private static DatabaseBackend ParseBackend(string? backend)
        => Enum.TryParse(backend, ignoreCase: true, out DatabaseBackend parsed)
            ? parsed
            : DatabaseBackend.Sqlite;
}
