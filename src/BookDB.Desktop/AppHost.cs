using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Desktop.Helpers;
using BookDB.Desktop.Services;
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

    public IServiceProvider Services => _host.Services;

    private AppHost(IHost host)
    {
        _host = host;
    }

    public static AppHost Build()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BookDB");

        Directory.CreateDirectory(appDataPath);

        var activeLibraryPath = Path.Combine(appDataPath, "library.db");
        var connectionString = $"Data Source={activeLibraryPath}";

        ApplyCultureBootstrap(activeLibraryPath, connectionString);

        var levelSwitch = ApplyLogLevelBootstrap(activeLibraryPath, connectionString);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .WriteTo.File(
                Path.Combine(appDataPath, "logs", "bookdb-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Console()
            .CreateLogger();

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";
        Log.Warning("Application started {Version} — DB: {DbPath} — Culture: {Culture}",
            version, activeLibraryPath, CultureInfo.CurrentUICulture.Name);

        var appSettings = new AppSettings
        {
            ActiveLibraryPath = activeLibraryPath,
            ConnectionString = connectionString
        };

        var host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(services =>
            {
                services.AddBookDbDataServices(appSettings);
                services.AddBookDbLogicServices();
                services.AddBookDbDesktopServices();
                services.AddBookDbViewModels();
                services.AddBookDbViews();
            })
            .Build();

        return new AppHost(host);
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
        await viewModel.PersistSettingsAsync();

        // Auto-backup on close — only when one is configured. 10-second timeout prevents a hang.
        using var backupCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var backupService = _host.Services.GetRequiredService<IBackupService>();
            if (await backupService.IsAutoBackupEnabledAsync(backupCts.Token))
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

    internal static void ApplyCultureBootstrap(string dbPath, string connectionString)
    {
        string? stored = null;

        if (File.Exists(dbPath))
        {
            try
            {
                using var conn = new SqliteConnection(connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Value FROM Settings WHERE Key = 'Language'";
                var scalar = cmd.ExecuteScalar();
                stored = scalar is DBNull or null ? null : (string)scalar;
            }
            catch { /* DB exists but Settings table not yet created — ignore */ }
        }

        if (stored is null)
        {
            // First run: probe OS culture, fall back to "en" if no matching satellite
            var osCode = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            var assemblyDir = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location) ?? "";
            var satellite = Path.Combine(assemblyDir, osCode, "BookDB.Desktop.resources.dll");
            stored = File.Exists(satellite) ? osCode : "en";

            // Persist best-effort (DB may not exist yet on truly first launch — DbUp hasn't run)
            if (File.Exists(dbPath))
            {
                try
                {
                    using var conn = new SqliteConnection(connectionString);
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText =
                        "INSERT OR REPLACE INTO Settings (Key, Value) VALUES ('Language', @v)";
                    cmd.Parameters.AddWithValue("@v", stored);
                    cmd.ExecuteNonQuery();
                }
                catch { /* first-run persist is best-effort; Settings table may not exist yet */ }
            }
        }

        // Guard: invalid/malicious culture code in DB must not crash startup
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
            // Stored code is invalid — fall back silently; default culture ("en") remains
        }
    }

    internal static LoggingLevelSwitch ApplyLogLevelBootstrap(string dbPath, string connectionString)
    {
        string? stored = null;

        if (File.Exists(dbPath))
        {
            try
            {
                using var conn = new SqliteConnection(connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Value FROM Settings WHERE Key = 'LogLevel'";
                var scalar = cmd.ExecuteScalar();
                stored = scalar is DBNull or null ? null : (string)scalar;
            }
            catch { /* Settings table not yet created — ignore */ }
        }

        var level = stored == "Verbose"
            ? LogEventLevel.Debug
            : LogEventLevel.Warning;   // "Normal" or key absent

        return new LoggingLevelSwitch(level);
    }

    internal static void HandleUnhandledException(Exception? ex)
    {
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
