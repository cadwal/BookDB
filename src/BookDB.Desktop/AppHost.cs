using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Data.PostgreSQL;
using BookDB.Security;
using BookDB.Desktop.Helpers;
using BookDB.Desktop.Services;
using BookDB.Desktop.Theming;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic;
using BookDB.Logic.Services;
using BookDB.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Globalization;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace BookDB.Desktop;

public sealed class AppHost : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly DatabaseBackend _backend;
    private readonly PostgresOptions? _postgresOptions;
    private readonly string? _postgresPassword;

    public IServiceProvider Services => _host.Services;

    /// <summary>True when startup must verify the database is reachable before the host runs (remote backend only).</summary>
    public bool RequiresStartupConnectivityCheck => _backend == DatabaseBackend.PostgreSql;

    private AppHost(IHost host, DatabaseBackend backend, PostgresOptions? postgresOptions, string? postgresPassword)
    {
        _host = host;
        _backend = backend;
        _postgresOptions = postgresOptions;
        _postgresPassword = postgresPassword;
        CurrentServices = host.Services;
    }

    // Set once the host is built so the static unhandled-exception backstop can resolve the connection monitor.
    private static IServiceProvider? CurrentServices { get; set; }

    /// <summary>
    /// If the exception is a dropped remote-DB connection, report it to the status-bar monitor and return true so
    /// the caller can keep the app alive instead of treating it as fatal. Many interactive DB calls (backup
    /// pre-reads, print preset saves, library move, etc.) are not individually guarded; this is the backstop that
    /// turns a connection loss into the same non-fatal status-indicator UX as the explicitly guarded paths.
    /// </summary>
    internal static bool TryReportConnectionLoss(Exception? ex)
    {
        if (ex is null || CurrentServices is null)
            return false;
        var classifier = CurrentServices.GetService<IConnectionFailureClassifier>();
        var monitor = CurrentServices.GetService<IConnectionHealthMonitor>();
        if (classifier is null || monitor is null || !classifier.IsConnectionLoss(ex))
            return false;
        monitor.ReportConnectionFailure();
        return true;
    }

    /// <summary>One-shot reachability probe for the configured PostgreSQL server, using the same options and
    /// credential-store password the live connection uses. Only valid when <see cref="RequiresStartupConnectivityCheck"/>.</summary>
    public Task<ConnectionProbeResult> ProbePostgresConnectionAsync(CancellationToken ct = default)
    {
        var prober = _host.Services.GetRequiredService<IPostgresConnectionProber>();
        return prober.ProbeAsync(_postgresOptions!, _postgresPassword, ct);
    }

    /// <summary>The per-user app-data directory; shared by startup so the single-instance gate and the
    /// host agree on one location.</summary>
    internal static string GetAppDataPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BookDB");

    public static AppHost Build()
    {
        var appDataPath = GetAppDataPath();

        Directory.CreateDirectory(appDataPath);

        var configPath = Path.Combine(appDataPath, "config.json");
        var defaultSqlitePath = Path.Combine(appDataPath, "library.db");

        var config = LoadOrCreateBootstrapConfig(configPath, defaultSqlitePath);

        var backend = ParseBackend(config.Backend);

        // SQLite keeps a local file path; PostgreSQL builds its connection string from the config.json server
        // parameters. The password is not stored in config.json — the credential store supplies it (until then
        // it is omitted, which is sufficient for a passwordless/trust-auth server).
        string? sqliteLibraryPath = null;
        string connectionString;
        string dbDescriptor;
        PostgresOptions? postgresOptions = null;
        string? postgresPassword = null;
        if (backend == DatabaseBackend.PostgreSql)
        {
            postgresOptions = config.Postgres;
            var (secretStore, _) = SecretStoreFactory.Create();
            postgresPassword = secretStore.Get(config.Postgres.AccountKey);
            connectionString = PostgresConnectionStringFactory.Build(config.Postgres, postgresPassword);
            dbDescriptor = PostgresConnectionStringFactory.Sanitize(connectionString);
        }
        else
        {
            // SQLite is always the canonical library file at the default location — it is never a
            // configurable path (copies come from backups).
            sqliteLibraryPath = defaultSqlitePath;
            connectionString = $"Data Source={sqliteLibraryPath}";
            dbDescriptor = sqliteLibraryPath;
        }

        ApplyCultureBootstrap(config);

        var levelSwitch = ApplyLogLevelBootstrap(config);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .WriteTo.File(
                Path.Combine(appDataPath, "logs", "bookdb-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                shared: true)
            .WriteTo.Console()
            .CreateLogger();

        var flavour = ApplyThemeBootstrap(config);

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";
        Log.Warning("Application started {Version} — Backend: {Backend} — DB: {DbDescriptor} — Culture: {Culture} — Theme: {Theme}",
            version, backend, dbDescriptor, CultureInfo.CurrentUICulture.Name, flavour);

        var appSettings = new AppSettings
        {
            Backend = backend,
            SqliteLibraryPath = sqliteLibraryPath,
            ConfigPath = configPath,
            ConnectionString = connectionString
        };

        var host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IBootstrapConfigService>(_ => new BootstrapConfigService(configPath));
                services.AddBookDbDataServices(appSettings);
                services.AddBookDbLogicServices();
                services.AddBookDbDesktopServices();
                services.AddBookDbViewModels();
                services.AddBookDbViews();
            })
            .Build();

        return new AppHost(host, backend, postgresOptions, postgresPassword);
    }

    public async Task<MainWindow> StartAsync()
    {
        var progress = _host.Services.GetRequiredService<IStartupProgressReporter>();
        progress.Report(StartupStage.Initializing);

        // QuestPDF Community license — must be set before any Document.Create() call.
        // Routed through IPrintService.InitializeLicense() to keep QuestPDF confined to BookDB.Logic.
        var printService = _host.Services.GetRequiredService<IPrintService>();
        printService.InitializeLicense();

        // Runs DatabaseStartupService (DbUp migrations), which reports its own sub-progress
        // and offloads the blocking upgrade to a background thread.
        await _host.StartAsync();

        progress.Report(StartupStage.LoadingLibrary);
        var viewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
        await viewModel.InitializeAsync();

        // Startup: clean up old completed items (>7 days) and reload any Pending items
        // that survived a previous shutdown. Processing items from a crashed session
        // are reset to Pending inside ReloadPendingFromDatabaseAsync.
        progress.Report(StartupStage.RestoringSession);
        var batchQueueService = _host.Services.GetRequiredService<BatchQueueService>();
        var batchProcessor = _host.Services.GetRequiredService<BatchQueueProcessor>();
        await batchQueueService.CleanupOldCompletedAsync();
        var pendingItems = await batchProcessor.ReloadPendingFromDatabaseAsync();
        if (pendingItems.Count > 0)
            _ = batchProcessor.StartBatch(pendingItems);

        progress.Report(StartupStage.Finishing);
        return _host.Services.GetRequiredService<MainWindow>();
    }

    public async Task ShutdownAsync()
    {
        Log.Warning("Shutdown initiated");

        var windowService = _host.Services.GetRequiredService<IWindowService>();
        windowService.CloseAllSecondaryWindows();

        var viewModel = _host.Services.GetRequiredService<MainWindowViewModel>();

        // Persisting settings hits the active database; on a remote backend that may be down at exit, so it
        // is best-effort with a short timeout — a failed save must never block the shutdown.
        using (var settingsCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            try
            {
                await viewModel.PersistSettingsAsync(settingsCts.Token);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Persisting settings on shutdown failed — proceeding");
            }
        }

        // Auto-backup on close — only when one is configured AND actually due (data changed this session or
        // the recency window lapsed). 10-second timeout prevents a hang.
        using var backupCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var backupService = _host.Services.GetRequiredService<IBackupService>();
            if (await backupService.ShouldAutoBackupAsync(backupCts.Token))
            {
                // Show a status window and run the backup off the UI thread so the indicator keeps animating.
                var (backupWindow, backupProgress) = AppDialogs.ShowBackupProgressWindow();
                try
                {
                    await Task.Run(
                        () => backupService.AutoBackupIfEnabledAsync(backupCts.Token, backupProgress),
                        backupCts.Token);
                }
                finally
                {
                    backupWindow.Close();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Auto-backup failed on shutdown — proceeding");
        }

        var batchProcessor = _host.Services.GetRequiredService<BatchQueueProcessor>();

        using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await batchProcessor.StopAsync(shutdownCts.Token);
            await _host.StopAsync(shutdownCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Timed out — force exit so the process doesn't hang
            Environment.Exit(0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else
            _host.Dispose();
    }

    /// <summary>
    /// Loads the bootstrap config from <paramref name="configPath"/>. On first run (no file), seeds the
    /// three pre-DI settings once from the existing SQLite Settings table when a library is present (the
    /// v1→v2 upgrade), then writes the file so it is authoritative from then on.
    /// </summary>
    internal static BootstrapConfig LoadOrCreateBootstrapConfig(string configPath, string sqliteDbPath)
    {
        var existing = BootstrapConfig.Load(configPath);
        if (existing is not null)
        {
            return existing;
        }

        var config = new BootstrapConfig();
        SeedSettingsFromSqlite(config, sqliteDbPath);
        config.Save(configPath);
        return config;
    }

    /// <summary>
    /// One-time v1→v2 upgrade: copies Language/UiTheme/LogLevel from the legacy SQLite Settings key/value
    /// table into <paramref name="config"/>. The Settings rows are left in place; config.json is the store
    /// from now on. Best-effort — a missing file or Settings table leaves the defaults untouched.
    /// </summary>
    internal static void SeedSettingsFromSqlite(BootstrapConfig config, string sqliteDbPath)
    {
        if (!File.Exists(sqliteDbPath))
        {
            return;
        }

        try
        {
            using var conn = new SqliteConnection($"Data Source={sqliteDbPath}");
            conn.Open();
            config.Language = ReadSettingValue(conn, "Language") ?? config.Language;
            config.UiTheme  = ReadSettingValue(conn, "UiTheme")  ?? config.UiTheme;
            config.LogLevel = ReadSettingValue(conn, "LogLevel") ?? config.LogLevel;
        }
        catch
        {
            // Settings table may not exist yet on a partially-initialised DB — keep defaults.
        }
    }

    private static string? ReadSettingValue(SqliteConnection openConnection, string key)
    {
        using var cmd = openConnection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Settings WHERE Key = @key";
        cmd.Parameters.AddWithValue("@key", key);
        var scalar = cmd.ExecuteScalar();
        return scalar is DBNull or null ? null : (string)scalar;
    }

    /// <summary>
    /// Maps the open <see cref="BootstrapConfig.Backend"/> string to the typed enum; an unknown or absent
    /// value falls back to <see cref="DatabaseBackend.Sqlite"/> (unsupported on this build).
    /// </summary>
    internal static DatabaseBackend ParseBackend(string? backend)
        => Enum.TryParse(backend, ignoreCase: true, out DatabaseBackend parsed)
            ? parsed
            : DatabaseBackend.Sqlite;

    internal static void ApplyCultureBootstrap(BootstrapConfig config)
    {
        var stored = config.Language;

        if (stored is null)
        {
            var osCode = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            var assemblyDir = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location) ?? "";
            var satellite = Path.Combine(assemblyDir, osCode, "BookDB.Desktop.resources.dll");
            stored = File.Exists(satellite) ? osCode : "en";
        }

        // Guard: an invalid/malicious culture code must not crash startup.
        try
        {
            var culture = new CultureInfo(stored);
            Thread.CurrentThread.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture   = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture   = culture;
        }
        catch (CultureNotFoundException)
        {
            // Stored code is invalid — fall back silently; default culture ("en") remains.
        }
    }

    internal static LoggingLevelSwitch ApplyLogLevelBootstrap(BootstrapConfig config)
    {
        var level = config.LogLevel == "Verbose"
            ? LogEventLevel.Debug
            : LogEventLevel.Warning;   // "Normal", null, or unrecognised

        return new LoggingLevelSwitch(level);
    }

    internal static ThemeFlavour ApplyThemeBootstrap(BootstrapConfig config)
    {
        var flavour = ThemeSettings.Parse(config.UiTheme);
        ThemeApplier.Apply(flavour);
        return flavour;
    }

    internal static void HandleUnhandledException(Exception? ex)
    {
        // A dropped remote DB connection is recoverable — surface it on the status indicator and keep going
        // instead of treating it as a fatal crash.
        if (TryReportConnectionLoss(ex))
            return;

        try
        {
            Log.Fatal(ex, "Unhandled exception — application terminating");
            Log.CloseAndFlush();
        }
        catch { /* logger itself may have failed — ignore */ }

        ShowFatalErrorDialog(ex);
    }

    internal static void HandleUnobservedTaskException(Exception? ex)
    {
        // On Linux desktops without a global app-menu registrar, Avalonia's DBus integration raises
        // org.freedesktop.DBus.Error.ServiceUnknown for com.canonical.AppMenu.Registrar. It is harmless
        // and surfaces here as an unobserved task exception — filter it out so it does not spam the log.
        if (IsBenignAppMenuRegistrarError(ex))
            return;

        // A dropped remote DB connection on a background task drives the status indicator, not a log error.
        if (TryReportConnectionLoss(ex))
            return;

        try
        {
            Log.Error(ex, "Unobserved task exception");
        }
        catch { /* ignore */ }
        // Do NOT re-throw — in .NET 6+ re-throwing an UnobservedTaskException crashes the process
    }

    private static bool IsBenignAppMenuRegistrarError(Exception? ex)
    {
        const string marker = "com.canonical.AppMenu.Registrar";
        if (ex is AggregateException agg)
        {
            foreach (var inner in agg.Flatten().InnerExceptions)
                if (inner.Message.Contains(marker, StringComparison.Ordinal))
                    return true;
            return false;
        }
        for (var e = ex; e is not null; e = e.InnerException)
            if (e.Message.Contains(marker, StringComparison.Ordinal))
                return true;
        return false;
    }

    internal static void ShowFatalErrorDialog(Exception? ex)
    {
        // Build the log file path for the "Open log file" button
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BookDB");
        var logFileName = $"bookdb-{DateTime.Now:yyyyMMdd}.log";
        var logPath = Path.Combine(appDataPath, "logs", logFileName);
        var target = File.Exists(logPath) ? logPath : Path.Combine(appDataPath, "logs");

        // Show a synchronous crash notification by opening the log file (or a crash text file)
        // in the system default viewer. Works before and after MainWindow exists.
        // We cannot use Avalonia dialogs here because the UI thread may be in an unknown state.
        try
        {
            var msgFile = Path.Combine(Path.GetTempPath(), "bookdb-crash.txt");
            File.WriteAllText(msgFile,
                $"BookDB encountered an unexpected error and needs to close.\n\n" +
                $"Error details have been written to:\n{logPath}\n\n" +
                $"Please open that file to see the full details.\n\n" +
                $"Error: {ex?.Message ?? "(unknown)"}");

            Helpers.SystemLauncher.Open(File.Exists(logPath) ? logPath : msgFile);
        }
        catch { /* last-resort — ignore all errors in the fatal handler */ }
    }
}
