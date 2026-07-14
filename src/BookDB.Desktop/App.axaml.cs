using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using BookDB.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace BookDB.Desktop;

public partial class App : Application
{
    private AppHost? _appHost;

    /// <summary>Set by the headless UI-test harness so app init loads resources/themes (via <see cref="Initialize"/>)
    /// but skips the production startup (AppHost.Build, single-instance gate, splash, connectivity gate, MainWindow).
    /// Tests build their own DI host and create their own windows.</summary>
    public static bool HeadlessTestMode { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (HeadlessTestMode)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        BindingPlugins.DataValidators.RemoveAt(0);

        _appHost = AppHost.Build();
        DataTemplates.Add(new ViewLocator(_appHost.Services));

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Keep the app alive while only the splash is visible (closing the splash before the
            // main window is shown would otherwise trigger an OnLastWindowClose shutdown).
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            // Exit is raised on the UI thread with the dispatcher still pumping, on every exit path —
            // the one safe moment to tear down the DBus connection (see DBusShutdown).
            desktop.Exit += (_, _) => Helpers.DBusShutdown.DisposeDefaultConnection();

            // Wire Avalonia UI-thread unhandled exception handler via Dispatcher.
            // IClassicDesktopStyleApplicationLifetime.UnhandledException does not exist in
            // Avalonia 11.3.x — the correct hook is Dispatcher.UIThread.UnhandledException.
            // Wired before StartAsync so the handler is active during startup.
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                // A dropped remote DB connection from an unguarded interactive call is not fatal: surface it on
                // the status indicator and keep running, rather than opening the crash log.
                if (!AppHost.TryReportConnectionLoss(e.Exception))
                    AppHost.HandleUnhandledException(e.Exception);
                e.Handled = true;   // prevent Avalonia from also crashing the process
            };

            // Show the splash immediately so the user sees feedback while migrations and other
            // heavy startup jobs run. Its ViewModel is already subscribed to the progress reporter.
            var splash = _appHost.Services.GetRequiredService<SplashWindow>();
            splash.Show();

            var windowService = _appHost.Services.GetRequiredService<Services.IWindowService>();

            // Remote backend: verify the server is reachable before the host runs DbUp against it.
            if (_appHost.RequiresStartupConnectivityCheck)
            {
                // The splash is Topmost so it floats over other apps during a normal launch; drop that while we
                // show interactive recovery dialogs, or they would render behind it.
                splash.Topmost = false;
                var probe = await _appHost.ProbeConnectionAsync();
                while (!probe.IsSuccess)
                {
                    var outcome = await windowService.ShowStartupFailureDialogAsync(
                        probe, _appHost.ProbeConnectionAsync, splash);
                    if (outcome == ViewModels.StartupFailureOutcome.Proceed)
                        break;
                    if (outcome == ViewModels.StartupFailureOutcome.Quit)
                    {
                        splash.Close();
                        desktop.Shutdown();
                        return;
                    }

                    // Open settings: the user can fix the connection (Apply restarts the app) — otherwise re-probe.
                    // Guard it: if opening settings throws, an unhandled exception here escapes this async-void
                    // startup path and is swallowed as a connection loss, stranding the app with no window. Log
                    // and loop back to the failure dialog instead, so recovery always stays reachable.
                    try
                    {
                        await windowService.ShowSettingsAsync(splash);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Opening settings from the startup-failure recovery flow failed");
                    }
                    probe = await _appHost.ProbeConnectionAsync();
                }
            }

            var sw = Stopwatch.StartNew();
            Views.MainWindow mainWindow;
            try
            {
                mainWindow = await _appHost.StartAsync();
            }
            catch
            {
                splash.Close();   // never leave an orphaned splash behind; let the fatal-error flow run
                throw;
            }

            // Minimum display time so the splash never flickers on a fast launch.
            var remaining = 500 - (int)sw.ElapsedMilliseconds;
            if (remaining > 0)
                await Task.Delay(remaining);

            // Remote backend only: block if another live client already holds the database.
            if (!await windowService.ShowConnectDialogAsync(splash))
            {
                splash.Close();
                await _appHost.ShutdownAsync();   // removes this client's heartbeat row; no backup (nothing changed)
                desktop.Shutdown();
                return;
            }

            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            splash.Close();

            // A second launch signals this instance over the gate's pipe (off the UI thread); marshal the
            // window-raise back onto the UI thread.
            Program.InstanceGate?.SetActivationHandler(() =>
                Dispatcher.UIThread.Post(() => Helpers.WindowActivator.BringToFront(mainWindow)));

            var shutdownInProgress = false;
            desktop.ShutdownRequested += async (_, args) =>
            {
                if (shutdownInProgress) return;
                // Cancel so we can await async shutdown work (auto-backup etc.) before the process exits.
                args.Cancel = true;
                shutdownInProgress = true;
                // Shutdown work must never strand the app open: if it throws (e.g. a remote DB is down while
                // persisting settings), still tear down so the window can close instead of hanging forever.
                try { await _appHost.ShutdownAsync(); }
                catch (Exception ex) { Log.Error(ex, "Shutdown work failed — exiting anyway"); }
                finally { desktop.Shutdown(); }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
