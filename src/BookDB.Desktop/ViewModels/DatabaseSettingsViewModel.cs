using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Data.MySql;
using BookDB.Data.PostgreSQL;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using BookDB.Help;
using BookDB.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BookDB.Desktop.ViewModels;

/// <summary>A selectable TLS/SSL mode; <see cref="Value"/> is the driver's <c>SslMode</c> token stored in config.json.</summary>
public sealed record SslModeOption(string Value, string DisplayName);

/// <summary>
/// The Settings → Database tab: choose the backend (local SQLite, a PostgreSQL server, or a MySQL/MariaDB server)
/// and edit the server connection parameters. The shared Save button commits a backend/connection change through
/// <see cref="SaveAsync"/>; <see cref="ValidateForSave"/> gates it and <see cref="DbChanged"/> tells the window
/// a restart is needed — the same restart-on-Save path that language, theme, and log level already use.
/// </summary>
public sealed partial class DatabaseSettingsViewModel : ObservableObject
{
    private readonly IBootstrapConfigService _bootstrapConfig;
    private readonly IPostgresConnectionProber _postgresProber;
    private readonly IMySqlConnectionProber _mySqlProber;
    private readonly ISecretStore _secretStore;
    private readonly IWindowService _windowService;

    // The selected backend is the single source of truth; the three Is*Selected flags are computed views of it, so
    // selecting one engine necessarily deselects the others (no separate mutually-exclusive booleans to keep in sync).
    private DatabaseBackend _selectedBackend = DatabaseBackend.Sqlite;

    // The TLS token sets differ per engine: PostgreSQL uses Npgsql's SslMode names, MySQL/MariaDB use the
    // MySQL driver's. Per-instance so the display names follow the culture active when the tab is opened.
    private readonly IReadOnlyList<SslModeOption> _postgresSslModes;
    private readonly IReadOnlyList<SslModeOption> _mySqlSslModes;

    // Baseline captured at load, so IsDirty reflects edits the user actually made this session.
    private DatabaseBackend _baseBackend;
    private string _baseHost = string.Empty;
    private string _basePort = string.Empty;
    private string _baseDatabase = string.Empty;
    private string _baseUsername = string.Empty;
    private string _basePassword = string.Empty;
    private string _baseSslMode = string.Empty;

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

    public bool IsSqliteSelected
    {
        get => _selectedBackend == DatabaseBackend.Sqlite;
        set { if (value) TrySelectBackend(DatabaseBackend.Sqlite); }
    }

    public bool IsPostgreSqlSelected
    {
        get => _selectedBackend == DatabaseBackend.PostgreSql;
        set { if (value) TrySelectBackend(DatabaseBackend.PostgreSql); }
    }

    public bool IsMySqlSelected
    {
        get => _selectedBackend == DatabaseBackend.MySql;
        set { if (value) TrySelectBackend(DatabaseBackend.MySql); }
    }

    /// <summary>True when a server backend (PostgreSQL or MySQL/MariaDB) is selected — drives the shared connection panel.</summary>
    public bool IsRemoteSelected => _selectedBackend != DatabaseBackend.Sqlite;

    /// <summary>The TLS modes valid for the selected engine; the ComboBox rebinds when the backend changes.</summary>
    public IReadOnlyList<SslModeOption> AvailableSslModes => IsMySqlSelected ? _mySqlSslModes : _postgresSslModes;

    /// <summary>Whether an OS credential store exists; a server backend is unavailable without one (no plaintext fallback).</summary>
    public bool IsKeyringAvailable { get; }

    public string KeyringUnavailableMessage => Resources.Settings_Database_KeyringUnavailable;

    /// <summary>True when TLS is set to a plaintext mode (Postgres "Disable" / MySQL "None"), surfacing the warning.</summary>
    public bool ShowTlsDisableWarning => SelectedSslMode?.Value is "Disable" or "None";

    /// <summary>True once a test-connection result is available, gating its inline display.</summary>
    public bool HasTestResult => !string.IsNullOrEmpty(TestResultMessage);

    /// <summary>True when the Apply flow has a blocking validation message to show inline.</summary>
    public bool HasApplyError => !string.IsNullOrEmpty(ApplyErrorMessage);

    /// <summary>True when the credential store already holds a password for the current connection, so Save can
    /// proceed without re-entering it (the field stays blank by design). Drives the saved-password hint.</summary>
    public bool HasSavedPassword =>
        IsRemoteSelected && !string.IsNullOrEmpty(_secretStore.Get(CurrentAccountKey()));

    /// <summary>True when the user changed any field from its loaded value — drives the Apply enablement.</summary>
    public bool IsDirty =>
        _selectedBackend != _baseBackend ||
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
        IPostgresConnectionProber postgresProber,
        IMySqlConnectionProber mySqlProber,
        ISecretStore secretStore,
        IWindowService windowService)
    {
        _bootstrapConfig = bootstrapConfig;
        _postgresProber = postgresProber;
        _mySqlProber = mySqlProber;
        _secretStore = secretStore;
        _windowService = windowService;
        IsKeyringAvailable = secretStoreAvailability.IsAvailable;

        _postgresSslModes = RemoteConnectionEditor.PostgresSslModes();
        _mySqlSslModes = RemoteConnectionEditor.MySqlSslModes();

        SelectedSslMode = _postgresSslModes.First(s => s.Value == RemoteConnectionEditor.DefaultSslMode(_selectedBackend));
    }

    [RelayCommand]
    private void OpenRemoteDatabasesHelp() => _windowService.OpenHelpWindow(HelpTab.RemoteDatabases);

    /// <summary>Probes the entered server parameters with the engine's prober and shows an inline, classified result.</summary>
    [RelayCommand(CanExecute = nameof(CanTestConnection))]
    private async Task TestConnectionAsync()
    {
        IsTesting = true;
        TestResultMessage = null;
        try
        {
            // Use the freshly entered password if any; otherwise fall back to the one already stored for this
            // connection (the field is blank by design when a password is saved), so Test mirrors how the live
            // connection authenticates instead of probing with no password.
            var stored = string.IsNullOrEmpty(Password) ? _secretStore.Get(CurrentAccountKey()) : Password;
            var probePassword = string.IsNullOrEmpty(stored) ? null : stored;
            var result = IsMySqlSelected
                ? await _mySqlProber.ProbeAsync(BuildMySqlOptions(), probePassword)
                : await _postgresProber.ProbeAsync(BuildPostgresOptions(), probePassword);
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

    private bool CanTestConnection() => IsRemoteSelected && !IsTesting && !string.IsNullOrWhiteSpace(Host);

    private void ApplyResult(ConnectionProbeResult result)
    {
        TestResultIsError = !result.IsSuccess;
        TestResultMessage = result.Status switch
        {
            ConnectionProbeStatus.Success => RemoteConnectionEditor.DescribeSuccess(result, IsMySqlSelected),
            ConnectionProbeStatus.AuthenticationFailed => Resources.Settings_Database_TestError_Auth,
            ConnectionProbeStatus.ConnectionRefused => Resources.Settings_Database_TestError_Refused,
            ConnectionProbeStatus.Timeout => Resources.Settings_Database_TestError_Timeout,
            ConnectionProbeStatus.TlsError => Resources.Settings_Database_TestError_Tls,
            ConnectionProbeStatus.UnsupportedServerVersion => DescribeUnsupportedVersion(result),
            _ => string.Format(CultureInfo.CurrentCulture, Resources.Settings_Database_TestError_Unknown, result.ErrorDetail ?? string.Empty),
        };
    }

    private string DescribeUnsupportedVersion(ConnectionProbeResult result) => IsMySqlSelected
        ? string.Format(CultureInfo.CurrentCulture, Resources.Settings_Database_TestError_UnsupportedVersion_MySql,
            MySqlConnectionProber.MinimumMySqlVersionText, MySqlConnectionProber.MinimumMariaDbVersionText, result.ErrorDetail ?? string.Empty)
        : string.Format(CultureInfo.CurrentCulture, Resources.Settings_Database_TestError_UnsupportedVersion,
            PostgresConnectionProber.MinimumServerMajorVersion, result.ErrorDetail ?? string.Empty);

    /// <summary>
    /// Gate for the shared Save: a server backend needs a password — entered now or already stored. Returns false
    /// with an inline error when it is missing, so Save can abort instead of writing a backend that can't
    /// authenticate. Cheap and side-effect-free; call it before <see cref="SaveAsync"/>.
    /// </summary>
    public bool ValidateForSave()
    {
        ApplyErrorMessage = null;
        if (IsRemoteSelected && string.IsNullOrEmpty(Password)
            && string.IsNullOrEmpty(_secretStore.Get(CurrentAccountKey())))
        {
            ApplyErrorMessage = Resources.Settings_Database_Apply_PasswordRequired;
            return false;
        }
        return true;
    }

    private string CurrentAccountKey() =>
        IsMySqlSelected ? BuildMySqlOptions().AccountKey : BuildPostgresOptions().AccountKey;

    private PostgresOptions BuildPostgresOptions() =>
        RemoteConnectionEditor.BuildPostgresOptions(Host, Port, DatabaseName, Username, SelectedSslMode?.Value);

    private MySqlOptions BuildMySqlOptions() =>
        RemoteConnectionEditor.BuildMySqlOptions(Host, Port, DatabaseName, Username, SelectedSslMode?.Value);

    public Task LoadAsync(CancellationToken ct = default)
    {
        var config = _bootstrapConfig.Load();
        var stored = ParseBackend(config.Backend);

        // Honour the stored backend only when a keyring is present; a server backend cannot be selected without one.
        _selectedBackend = (stored != DatabaseBackend.Sqlite && !IsKeyringAvailable) ? DatabaseBackend.Sqlite : stored;

        // Populate the shared connection fields from the config block matching the effective backend; fall back to
        // the Postgres block so the panel still has sensible values when the user switches to a server from SQLite.
        if (_selectedBackend == DatabaseBackend.MySql)
        {
            var my = config.MySql;
            Host = my.Host;
            Port = my.Port.ToString(CultureInfo.InvariantCulture);
            DatabaseName = string.IsNullOrWhiteSpace(my.Database) ? "bookdb" : my.Database;
            Username = my.Username;
            SelectedSslMode = _mySqlSslModes.FirstOrDefault(s =>
                string.Equals(s.Value, my.SslMode, StringComparison.OrdinalIgnoreCase))
                ?? _mySqlSslModes.First(s => s.Value == RemoteConnectionEditor.DefaultSslMode(DatabaseBackend.MySql));
        }
        else
        {
            var pg = config.Postgres;
            Host = pg.Host;
            Port = pg.Port.ToString(CultureInfo.InvariantCulture);
            DatabaseName = string.IsNullOrWhiteSpace(pg.Database) ? "bookdb" : pg.Database;
            Username = pg.Username;
            SelectedSslMode = _postgresSslModes.FirstOrDefault(s =>
                string.Equals(s.Value, pg.SslMode, StringComparison.OrdinalIgnoreCase))
                ?? _postgresSslModes.First(s => s.Value == RemoteConnectionEditor.DefaultSslMode(DatabaseBackend.PostgreSql));
        }
        Password = string.Empty; // never read back from the credential store into the UI

        RaiseBackendFlags();
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

        if (IsMySqlSelected)
        {
            var options = BuildMySqlOptions();
            if (!string.IsNullOrEmpty(Password))
                _secretStore.Set(options.AccountKey, Password);
            _bootstrapConfig.Update(c =>
            {
                c.Backend = DatabaseBackend.MySql.ToString();
                c.MySql = options;
            });
        }
        else if (IsPostgreSqlSelected)
        {
            var options = BuildPostgresOptions();
            if (!string.IsNullOrEmpty(Password))
                _secretStore.Set(options.AccountKey, Password);
            _bootstrapConfig.Update(c =>
            {
                c.Backend = DatabaseBackend.PostgreSql.ToString();
                c.Postgres = options;
            });
        }
        else
        {
            // Switching back to the local file leaves the stored server blocks intact for an easy switch-back.
            _bootstrapConfig.Update(c => c.Backend = DatabaseBackend.Sqlite.ToString());
        }

        DbChanged = true;
        CaptureBaseline();
        return Task.CompletedTask;
    }

    private void CaptureBaseline()
    {
        _baseBackend = _selectedBackend;
        _baseHost = Host;
        _basePort = Port;
        _baseDatabase = DatabaseName;
        _baseUsername = Username;
        _basePassword = Password;
        _baseSslMode = SelectedSslMode?.Value ?? string.Empty;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(HasSavedPassword)); // after a Save the password is now stored
    }

    private void TrySelectBackend(DatabaseBackend backend)
    {
        // A server backend needs a keyring (no plaintext fallback); veto the selection and snap the radio back to
        // its current state when none is available.
        if (backend != DatabaseBackend.Sqlite && !IsKeyringAvailable)
        {
            RaiseBackendFlags();
            return;
        }
        if (_selectedBackend == backend)
            return;

        var previous = _selectedBackend;
        _selectedBackend = backend;
        ApplyPortDefault(previous);

        RaiseBackendFlags();
        OnPropertyChanged(nameof(IsRemoteSelected));
        OnPropertyChanged(nameof(AvailableSslModes)); // ItemsSource first…
        SyncSslModeToBackend();                       // …then move the selection onto the new list
        OnPropertyChanged(nameof(HasSavedPassword));
        OnPropertyChanged(nameof(IsDirty));
        TestResultMessage = null; // a stale result from the other backend must not linger
        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    private void RaiseBackendFlags()
    {
        OnPropertyChanged(nameof(IsSqliteSelected));
        OnPropertyChanged(nameof(IsPostgreSqlSelected));
        OnPropertyChanged(nameof(IsMySqlSelected));
    }

    // Offer the new engine's default port when the field still holds the previous engine's default (or is blank),
    // so a fresh switch doesn't carry over an irrelevant port. A port the user typed is left untouched.
    private void ApplyPortDefault(DatabaseBackend previous)
    {
        var newDefault = RemoteConnectionEditor.DefaultPort(_selectedBackend);
        var previousDefault = RemoteConnectionEditor.DefaultPort(previous);
        if (string.IsNullOrWhiteSpace(Port) || Port == previousDefault)
            Port = newDefault;
    }

    private void SyncSslModeToBackend()
    {
        var modes = AvailableSslModes;
        if (SelectedSslMode is null || modes.All(m => m.Value != SelectedSslMode.Value))
            SelectedSslMode = modes.First(m => m.Value == RemoteConnectionEditor.DefaultSslMode(_selectedBackend));
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        switch (e.PropertyName)
        {
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
