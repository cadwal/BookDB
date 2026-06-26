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
using BookDB.Data.PostgreSQL;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
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
    private readonly IMigrationTargetBuilder _targetBuilder;
    private readonly ILibraryMigrationService _migrationService;
    private readonly IBackupService _backupService;
    private readonly IFilePickerService _filePicker;
    private readonly ISecretStore _secretStore;
    private readonly IApplicationRestartService _restartService;

    public MoveLibraryViewModel(
        IDbContextFactory<BookDbContext> source,
        AppSettings appSettings,
        IBootstrapConfigService bootstrapConfig,
        IPostgresConnectionProber prober,
        IMigrationTargetBuilder targetBuilder,
        ILibraryMigrationService migrationService,
        IBackupService backupService,
        IFilePickerService filePicker,
        ISecretStore secretStore,
        IApplicationRestartService restartService)
    {
        _source = source;
        _appSettings = appSettings;
        _bootstrapConfig = bootstrapConfig;
        _prober = prober;
        _targetBuilder = targetBuilder;
        _migrationService = migrationService;
        _backupService = backupService;
        _filePicker = filePicker;
        _secretStore = secretStore;
        _restartService = restartService;

        // For v2 there is one alternative backend: the target is always whichever the source is not.
        TargetBackend = appSettings.Backend == DatabaseBackend.Sqlite
            ? DatabaseBackend.PostgreSql
            : DatabaseBackend.Sqlite;
        SelectedSslMode = AvailableSslModes.First(s => s.Value == "Require");
        SourceDescription = DescribeSource();
    }

    public bool TargetIsPostgres => TargetBackend == DatabaseBackend.PostgreSql;
    public bool TargetIsSqlite => !TargetIsPostgres;
    private DatabaseBackend TargetBackend { get; }

    public string SourceDescription { get; }

    public string TargetBackendName => TargetIsPostgres
        ? Resources.MoveLibrary_Target_Postgres
        : Resources.MoveLibrary_Target_Sqlite;

    // --- Postgres target fields (reuse the Settings → Database editor's shape) ---
    [ObservableProperty] private string _host = string.Empty;
    [ObservableProperty] private string _port = "5432";
    [ObservableProperty] private string _databaseName = "bookdb";
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private SslModeOption? _selectedSslMode;

    public IReadOnlyList<SslModeOption> AvailableSslModes { get; } =
    [
        new SslModeOption("Disable",    Resources.Settings_Database_Tls_Disable),
        new SslModeOption("Prefer",     Resources.Settings_Database_Tls_Prefer),
        new SslModeOption("Require",    Resources.Settings_Database_Tls_Require),
        new SslModeOption("VerifyFull", Resources.Settings_Database_Tls_VerifyFull),
    ];

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

    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestConnectionAsync()
    {
        IsTesting = true;
        TestResultMessage = null;
        try
        {
            var result = await _prober.ProbeAsync(BuildOptions(), string.IsNullOrEmpty(Password) ? null : Password);
            TestResultIsError = !result.IsSuccess;
            TestResultMessage = result.Status == ConnectionProbeStatus.Success
                ? (result.BookCount.HasValue
                    ? string.Format(CultureInfo.CurrentCulture, Resources.Settings_Database_TestSuccess, result.ServerVersion, result.BookCount.Value)
                    : string.Format(CultureInfo.CurrentCulture, Resources.Settings_Database_TestSuccessUninitialized, result.ServerVersion))
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

    private bool CanTest() => TargetIsPostgres && !IsTesting && !IsRunning && !string.IsNullOrWhiteSpace(Host);

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckTargetAsync()
    {
        CheckErrorMessage = null;
        TargetChecked = false;
        try
        {
            TargetRecordCount = TargetIsPostgres
                ? await CheckPostgresAsync()
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
        (!TargetIsPostgres || (!string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Username)));

    // A reachable server with no BookDB schema yet (BookCount null) counts as empty, not an error.
    private async Task<long> CheckPostgresAsync()
    {
        var result = await _prober.ProbeAsync(BuildOptions(), string.IsNullOrEmpty(Password) ? null : Password);
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
        // Safety backup first — never write to the target before the source is captured (R29).
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

            var connectionString = TargetIsPostgres
                ? PostgresConnectionStringFactory.Build(BuildOptions(), string.IsNullOrEmpty(Password) ? null : Password)
                : $"Data Source={DefaultSqlitePath}";

            await using var target = await _targetBuilder.BuildAsync(TargetBackend, connectionString);

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
                return; // counts must match before the active database may be switched (R31)
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
        (!TargetIsPostgres || (!string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrEmpty(Password)));

    private async Task SwitchActiveDatabaseAsync()
    {
        var backendName = TargetIsPostgres
            ? Resources.Settings_Database_Backend_Postgres
            : Resources.Settings_Database_Backend_Sqlite;
        var confirm = string.Format(CultureInfo.CurrentCulture, Resources.Settings_Database_RestartConfirm_Body, backendName);
        if (!await _restartService.ConfirmRestartAsync(confirm))
            return;

        if (TargetIsPostgres)
        {
            var options = BuildOptions();
            _secretStore.Set(options.AccountKey, Password);
            _bootstrapConfig.Update(c =>
            {
                c.Backend = DatabaseBackend.PostgreSql.ToString();
                c.Postgres = options;
            });
        }
        else
        {
            _bootstrapConfig.Update(c =>
            {
                c.Backend = DatabaseBackend.Sqlite.ToString();
            });
        }

        _restartService.Restart();
    }

    private PostgresOptions BuildOptions() => new()
    {
        Host = Host.Trim(),
        Port = int.TryParse(Port, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ? port : 5432,
        Database = DatabaseName.Trim(),
        Username = Username.Trim(),
        SslMode = SelectedSslMode?.Value ?? "Require",
    };

    private string DescribeSource()
    {
        if (_appSettings.Backend == DatabaseBackend.PostgreSql)
        {
            var pg = _bootstrapConfig.Load().Postgres;
            return string.Format(CultureInfo.CurrentCulture, Resources.MoveLibrary_Source_Postgres, $"{pg.Host}/{pg.Database}");
        }
        var file = string.IsNullOrEmpty(_appSettings.SqliteLibraryPath)
            ? "library.db"
            : Path.GetFileName(_appSettings.SqliteLibraryPath);
        return string.Format(CultureInfo.CurrentCulture, Resources.MoveLibrary_Source_Sqlite, file);
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
