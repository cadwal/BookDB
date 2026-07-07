using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BookDB.Data.DbContexts;
using BookDB.Data.Interfaces;
using BookDB.Data.MySql;
using BookDB.Data.PostgreSQL;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using BookDB.Help;
using BookDB.Logic.Services;
using BookDB.Models;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace BookDB.Desktop.ViewModels;

/// <summary>
/// The Maintenance dialog's "Move library" section: copies the whole library from the current (source) backend
/// to the other backend. The source is fixed and read-only; the target connection is entered here, content-checked
/// before any write, gated by an acknowledgement when it already holds data, and migrated after an automatic CSV
/// safety backup. Optionally switches the active database to the target (forced restart) once counts verify.
/// </summary>
public sealed partial class MoveLibraryViewModel : ObservableObject
{
    private readonly IDbContextFactory<BookDbContext> _source;
    private readonly AppSettings _appSettings;
    private readonly IBootstrapConfigService _bootstrapConfig;
    private readonly IPostgresConnectionProber _prober;
    private readonly IMySqlConnectionProber _mySqlProber;
    private readonly IMigrationTargetBuilder _targetBuilder;
    private readonly ILibraryMigrationService _migrationService;
    private readonly IBackupService _backupService;
    private readonly IFilePickerService _filePicker;
    private readonly ISecretStore _secretStore;
    private readonly IApplicationRestartService _restartService;
    private readonly IWindowService _windowService;

    public MoveLibraryViewModel(
        IDbContextFactory<BookDbContext> source,
        AppSettings appSettings,
        IBootstrapConfigService bootstrapConfig,
        IPostgresConnectionProber prober,
        IMySqlConnectionProber mySqlProber,
        IMigrationTargetBuilder targetBuilder,
        ILibraryMigrationService migrationService,
        IBackupService backupService,
        IFilePickerService filePicker,
        ISecretStore secretStore,
        IApplicationRestartService restartService,
        IWindowService windowService)
    {
        _source = source;
        _appSettings = appSettings;
        _bootstrapConfig = bootstrapConfig;
        _prober = prober;
        _mySqlProber = mySqlProber;
        _targetBuilder = targetBuilder;
        _migrationService = migrationService;
        _backupService = backupService;
        _filePicker = filePicker;
        _secretStore = secretStore;
        _restartService = restartService;
        _windowService = windowService;

        _postgresSslModes = RemoteConnectionEditor.PostgresSslModes();
        _mySqlSslModes = RemoteConnectionEditor.MySqlSslModes();

        // The source is fixed; default the target to a sensible alternative the user can change.
        _targetBackend = appSettings.Backend == DatabaseBackend.Sqlite
            ? DatabaseBackend.PostgreSql
            : DatabaseBackend.Sqlite;
        SelectedSslMode = AvailableSslModes.First(s => s.Value == RemoteConnectionEditor.DefaultSslMode(_targetBackend));
        SourceDescription = DescribeSource();
    }

    private DatabaseBackend _targetBackend;

    public bool TargetIsSqlite
    {
        get => _targetBackend == DatabaseBackend.Sqlite;
        set { if (value) SelectTarget(DatabaseBackend.Sqlite); }
    }

    public bool TargetIsPostgres
    {
        get => _targetBackend == DatabaseBackend.PostgreSql;
        set { if (value) SelectTarget(DatabaseBackend.PostgreSql); }
    }

    public bool TargetIsMySql
    {
        get => _targetBackend == DatabaseBackend.MySql;
        set { if (value) SelectTarget(DatabaseBackend.MySql); }
    }

    /// <summary>True for any server target (PostgreSQL or MySQL/MariaDB) — drives the shared connection panel and the credential gating.</summary>
    public bool IsServerTarget => _targetBackend != DatabaseBackend.Sqlite;

    // A backend is offered as a target only when it isn't the fixed source.
    public bool ShowSqliteTargetOption => _appSettings.Backend != DatabaseBackend.Sqlite;
    public bool ShowPostgresTargetOption => _appSettings.Backend != DatabaseBackend.PostgreSql;
    public bool ShowMySqlTargetOption => _appSettings.Backend != DatabaseBackend.MySql;

    public string SourceDescription { get; }

    public string TargetBackendName => _targetBackend switch
    {
        DatabaseBackend.PostgreSql => Resources.MoveLibrary_Target_Postgres,
        DatabaseBackend.MySql => Resources.MoveLibrary_Target_MySql,
        _ => Resources.MoveLibrary_Target_Sqlite,
    };

    // --- Server target fields (reuse the Settings → Database editor's shape) ---
    [ObservableProperty] private string _host = string.Empty;
    [ObservableProperty] private string _port = "5432";
    [ObservableProperty] private string _databaseName = "bookdb";
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private SslModeOption? _selectedSslMode;

    // Per-engine TLS token sets (Npgsql vs the MySQL driver); the same split as DatabaseSettingsViewModel.
    private readonly IReadOnlyList<SslModeOption> _postgresSslModes;
    private readonly IReadOnlyList<SslModeOption> _mySqlSslModes;

    public IReadOnlyList<SslModeOption> AvailableSslModes => TargetIsMySql ? _mySqlSslModes : _postgresSslModes;

    // --- SQLite target ---
    // The SQLite backend is always the canonical local library file; it is never chosen (copies come
    // from backups), so the move target is the fixed default path rather than a picked file.
    private static string DefaultSqlitePath => Path.Combine(AppHost.GetAppDataPath(), "library.db");

    // --- Test connection (Postgres only) ---
    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private string? _testResultMessage;
    [ObservableProperty] private bool _testResultIsError;
    public bool HasTestResult => !string.IsNullOrEmpty(TestResultMessage);

    // --- Target content check ---
    [ObservableProperty] private bool _targetChecked;
    [ObservableProperty] private long _targetRecordCount;
    [ObservableProperty] private string? _checkErrorMessage;
    public bool TargetHasData => TargetChecked && TargetRecordCount > 0;
    public bool TargetIsEmpty => TargetChecked && TargetRecordCount == 0;
    public bool HasCheckError => !string.IsNullOrEmpty(CheckErrorMessage);
    public string TargetHasDataWarning =>
        string.Format(CultureInfo.CurrentCulture, Resources.MoveLibrary_Target_HasData, TargetRecordCount);

    [ObservableProperty] private bool _acknowledgeReplace;
    [ObservableProperty] private bool _switchActiveWhenDone = true;

    // --- Migration run state ---
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _logText = string.Empty;
    [ObservableProperty] private bool _hasFailure;
    [ObservableProperty] private string? _failureMessage;

    public bool CanInteract => !IsRunning;

    [RelayCommand]
    private void OpenRemoteDatabasesHelp() => _windowService.OpenHelpWindow(HelpTab.RemoteDatabases);

    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestConnectionAsync()
    {
        IsTesting = true;
        TestResultMessage = null;
        try
        {
            var result = await ProbeTargetAsync();
            TestResultIsError = !result.IsSuccess;
            TestResultMessage = result.Status == ConnectionProbeStatus.Success
                ? RemoteConnectionEditor.DescribeSuccess(result, TargetIsMySql)
                : ConnectionErrorText.Describe(result.Status, result.ErrorDetail);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "MoveLibraryViewModel: test connection failed");
            TestResultIsError = true;
            TestResultMessage = ConnectionErrorText.Describe(ConnectionProbeStatus.Unknown, ex.Message);
        }
        finally
        {
            IsTesting = false;
        }
    }

    private bool CanTest() => IsServerTarget && !IsTesting && !IsRunning && !string.IsNullOrWhiteSpace(Host);

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckTargetAsync()
    {
        CheckErrorMessage = null;
        TargetChecked = false;
        try
        {
            TargetRecordCount = IsServerTarget
                ? await CheckServerAsync()
                : CheckSqlite();
            TargetChecked = true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "MoveLibraryViewModel: target check failed");
            CheckErrorMessage = ConnectionErrorText.Describe(ConnectionProbeStatus.Unknown, ex.Message);
        }
    }

    private bool CanCheck() =>
        !IsRunning &&
        (!IsServerTarget || (!string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Username)));

    // A reachable server with no BookDB schema yet (BookCount null) counts as empty, not an error.
    private async Task<long> CheckServerAsync()
    {
        var result = await ProbeTargetAsync();
        if (!result.IsSuccess)
            throw new InvalidOperationException(ConnectionErrorText.Describe(result.Status, result.ErrorDetail));
        return result.BookCount ?? 0;
    }

    // Read-only count of the canonical library file; a missing file or absent schema means an empty target.
    private long CheckSqlite()
    {
        if (!File.Exists(DefaultSqlitePath))
            return 0;
        try
        {
            using var connection = new SqliteConnection($"Data Source={DefaultSqlitePath};Mode=ReadOnly");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM \"Book\"";
            return Convert.ToInt64(command.ExecuteScalar());
        }
        catch (SqliteException)
        {
            return 0; // no Book table yet — the migration's DbUp will create the schema
        }
    }

    [RelayCommand(CanExecute = nameof(CanMove))]
    private async Task MoveAsync()
    {
        // Safety backup first — never write to the target before the source is captured.
        var folder = await _filePicker.PickFolderAsync(Resources.FilePicker_ChooseSafetyBackupLocation);
        if (string.IsNullOrEmpty(folder))
            return;

        IsRunning = true;
        HasFailure = false;
        FailureMessage = null;
        LogText = string.Empty;
        MigrationPhase? lastPhase = null;

        var progress = new Progress<MigrationProgress>(p => Dispatcher.UIThread.Post(() =>
        {
            if (p.Phase != MigrationPhase.Copying && p.Phase != lastPhase)
            {
                lastPhase = p.Phase;
                AppendLine(MigrationText.Describe(p.Phase));
            }
            else if (p.Phase == MigrationPhase.Copying && p.Table is { } table
                && p.Copied > 0 && MigrationText.TryDescribe(table, out var label))
            {
                AppendLine(string.Format(CultureInfo.CurrentCulture, Resources.MoveLibrary_Progress_TableRunning, label, p.Copied, p.Total));
            }
        }));

        try
        {
            var safetyPath = await _backupService.BackupCsvArchiveAsync(folder);
            AppendLine(string.Format(CultureInfo.CurrentCulture, Resources.MoveLibrary_SafetyBackupSaved, safetyPath));

            var password = string.IsNullOrEmpty(Password) ? null : Password;
            var connectionString = _targetBackend switch
            {
                DatabaseBackend.PostgreSql => PostgresConnectionStringFactory.Build(BuildPostgresOptions(), password),
                DatabaseBackend.MySql => MySqlConnectionStringFactory.Build(BuildMySqlOptions(), password),
                _ => $"Data Source={DefaultSqlitePath}",
            };

            await using var target = await _targetBuilder.BuildAsync(_targetBackend, connectionString);

            // Snapshot the target too when it already holds data: the migration clears it, so capture an
            // engine-neutral backup of what is about to be replaced. A failure here aborts the move (caught
            // below, like the source backup) before anything is destroyed, so an acknowledged replace can still
            // be recovered from this archive.
            if (TargetHasData)
            {
                var targetBackupName = $"bookdb-csv-target-{DateTime.Now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture)}.zip";
                var targetBackupPath = await target.Backup.BackupCsvArchiveAsync(folder, explicitFileName: targetBackupName);
                AppendLine(string.Format(CultureInfo.CurrentCulture, Resources.MoveLibrary_TargetBackupSaved, targetBackupPath));
            }

            var result = await Task.Run(() =>
                _migrationService.MigrateAsync(_source, target.Factory, target.Resync, progress));

            if (result.Outcome == MigrationOutcome.Failed)
            {
                var where = result.FailedTable?.ToString() ?? "?";
                FailureMessage = string.Format(CultureInfo.CurrentCulture, Resources.MoveLibrary_Failed, where, safetyPath);
                AppendLine("  " + (result.ErrorMessage ?? string.Empty));
                HasFailure = true;
                return;
            }

            if (!result.AllCountsMatch)
            {
                AppendLine(Resources.MoveLibrary_CountMismatch);
                return; // counts must match before the active database may be switched
            }

            AppendLine(Resources.MoveLibrary_Complete);
            if (SwitchActiveWhenDone)
                await SwitchActiveDatabaseAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "MoveLibraryViewModel: migration failed");
            FailureMessage = ConnectionErrorText.Describe(ConnectionProbeStatus.Unknown, ex.Message);
            HasFailure = true;
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool CanMove() =>
        !IsRunning && TargetChecked && (!TargetHasData || AcknowledgeReplace) &&
        (!IsServerTarget || (!string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrEmpty(Password)));

    private async Task SwitchActiveDatabaseAsync()
    {
        var backendName = _targetBackend switch
        {
            DatabaseBackend.PostgreSql => Resources.Settings_Database_Backend_Postgres,
            DatabaseBackend.MySql => Resources.Settings_Database_Backend_MySql,
            _ => Resources.Settings_Database_Backend_Sqlite,
        };
        var confirm = string.Format(CultureInfo.CurrentCulture, Resources.Settings_Database_RestartConfirm_Body, backendName);
        if (!await _restartService.ConfirmRestartAsync(confirm))
            return;

        switch (_targetBackend)
        {
            case DatabaseBackend.PostgreSql:
            {
                var options = BuildPostgresOptions();
                _secretStore.Set(options.AccountKey, Password);
                _bootstrapConfig.Update(c =>
                {
                    c.Backend = DatabaseBackend.PostgreSql.ToString();
                    c.Postgres = options;
                });
                break;
            }
            case DatabaseBackend.MySql:
            {
                var options = BuildMySqlOptions();
                _secretStore.Set(options.AccountKey, Password);
                _bootstrapConfig.Update(c =>
                {
                    c.Backend = DatabaseBackend.MySql.ToString();
                    c.MySql = options;
                });
                break;
            }
            default:
                _bootstrapConfig.Update(c => c.Backend = DatabaseBackend.Sqlite.ToString());
                break;
        }

        _restartService.Restart();
    }

    private Task<ConnectionProbeResult> ProbeTargetAsync()
    {
        var password = string.IsNullOrEmpty(Password) ? null : Password;
        return TargetIsMySql
            ? _mySqlProber.ProbeAsync(BuildMySqlOptions(), password)
            : _prober.ProbeAsync(BuildPostgresOptions(), password);
    }

    private PostgresOptions BuildPostgresOptions() =>
        RemoteConnectionEditor.BuildPostgresOptions(Host, Port, DatabaseName, Username, SelectedSslMode?.Value);

    private MySqlOptions BuildMySqlOptions() =>
        RemoteConnectionEditor.BuildMySqlOptions(Host, Port, DatabaseName, Username, SelectedSslMode?.Value);

    // Switch the target backend: reset the validation it invalidates, move the TLS selection onto the new engine's
    // list, and offer the new engine's default port when the field still holds the previous engine's default.
    private void SelectTarget(DatabaseBackend backend)
    {
        if (_targetBackend == backend)
            return;

        var previous = _targetBackend;
        _targetBackend = backend;

        var newPortDefault = RemoteConnectionEditor.DefaultPort(backend);
        var previousPortDefault = RemoteConnectionEditor.DefaultPort(previous);
        if (string.IsNullOrWhiteSpace(Port) || Port == previousPortDefault)
            Port = newPortDefault;

        TargetChecked = false;
        TestResultMessage = null;
        CheckErrorMessage = null;

        OnPropertyChanged(nameof(TargetIsSqlite));
        OnPropertyChanged(nameof(TargetIsPostgres));
        OnPropertyChanged(nameof(TargetIsMySql));
        OnPropertyChanged(nameof(IsServerTarget));
        OnPropertyChanged(nameof(TargetBackendName));
        OnPropertyChanged(nameof(AvailableSslModes));
        if (SelectedSslMode is null || AvailableSslModes.All(m => m.Value != SelectedSslMode.Value))
            SelectedSslMode = AvailableSslModes.First(m => m.Value == RemoteConnectionEditor.DefaultSslMode(_targetBackend));

        TestConnectionCommand.NotifyCanExecuteChanged();
        CheckTargetCommand.NotifyCanExecuteChanged();
        MoveCommand.NotifyCanExecuteChanged();
    }

    private string DescribeSource()
    {
        switch (_appSettings.Backend)
        {
            case DatabaseBackend.PostgreSql:
            {
                var pg = _bootstrapConfig.Load().Postgres;
                return string.Format(CultureInfo.CurrentCulture, Resources.MoveLibrary_Source_Postgres, $"{pg.Host}/{pg.Database}");
            }
            case DatabaseBackend.MySql:
            {
                var my = _bootstrapConfig.Load().MySql;
                return string.Format(CultureInfo.CurrentCulture, Resources.MoveLibrary_Source_MySql, $"{my.Host}/{my.Database}");
            }
            default:
            {
                var file = string.IsNullOrEmpty(_appSettings.SqliteLibraryPath)
                    ? "library.db"
                    : Path.GetFileName(_appSettings.SqliteLibraryPath);
                return string.Format(CultureInfo.CurrentCulture, Resources.MoveLibrary_Source_Sqlite, file);
            }
        }
    }

    private void AppendLine(string line) => LogText += line + Environment.NewLine;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(IsRunning):
                OnPropertyChanged(nameof(CanInteract));
                TestConnectionCommand.NotifyCanExecuteChanged();
                CheckTargetCommand.NotifyCanExecuteChanged();
                MoveCommand.NotifyCanExecuteChanged();
                break;
            case nameof(IsTesting):
            case nameof(Host):
                TestConnectionCommand.NotifyCanExecuteChanged();
                CheckTargetCommand.NotifyCanExecuteChanged();
                MoveCommand.NotifyCanExecuteChanged();
                break;
            case nameof(Username):
            case nameof(Password):
                CheckTargetCommand.NotifyCanExecuteChanged();
                MoveCommand.NotifyCanExecuteChanged();
                break;
            case nameof(TargetChecked):
            case nameof(TargetRecordCount):
            case nameof(AcknowledgeReplace):
                OnPropertyChanged(nameof(TargetHasData));
                OnPropertyChanged(nameof(TargetIsEmpty));
                OnPropertyChanged(nameof(TargetHasDataWarning));
                MoveCommand.NotifyCanExecuteChanged();
                break;
            case nameof(TestResultMessage):
                OnPropertyChanged(nameof(HasTestResult));
                break;
            case nameof(CheckErrorMessage):
                OnPropertyChanged(nameof(HasCheckError));
                break;
        }
    }
}
