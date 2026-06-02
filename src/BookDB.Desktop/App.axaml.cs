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

namespace BookDB.Desktop;

public partial class App : Application
{
    private AppHost? _appHost;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        BindingPlugins.DataValidators.RemoveAt(0);

        _appHost = AppHost.Build();
        DataTemplates.Add(new ViewLocator(_appHost.Services));

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Keep the app alive while only the splash is visible (closing the splash before the
            // main window is shown would otherwise trigger an OnLastWindowClose shutdown).
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            // Wire Avalonia UI-thread unhandled exception handler via Dispatcher.
            // IClassicDesktopStyleApplicationLifetime.UnhandledException does not exist in
            // Avalonia 11.3.x — the correct hook is Dispatcher.UIThread.UnhandledException.
            // Wired before StartAsync so the handler is active during startup.
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                AppHost.HandleUnhandledException(e.Exception);
                e.Handled = true;   // prevent Avalonia from also crashing the process
            };

            // Show the splash immediately so the user sees feedback while migrations and other
            // heavy startup jobs run. Its ViewModel is already subscribed to the progress reporter.
            var splash = _appHost.Services.GetRequiredService<SplashWindow>();
            splash.Show();

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

            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            splash.Close();

            var shutdownInProgress = false;
            desktop.ShutdownRequested += async (_, args) =>
            {
                if (shutdownInProgress) return;
                // Cancel so we can await async shutdown work (auto-backup etc.) before the process exits.
                args.Cancel = true;
                shutdownInProgress = true;
                await _appHost.ShutdownAsync();
                desktop.Shutdown();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
