using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Serilog;

namespace BookDB.Desktop;

internal sealed class Program
{
    /// <summary>The single-instance gate for this process; consumed by <see cref="App"/> to wire window activation.</summary>
    internal static SingleInstanceGate? InstanceGate { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        // A relaunch waits for the outgoing instance to release the lock, rather than deferring to it and exiting.
        var isRelaunch = Array.Exists(args, a => string.Equals(a, SingleInstanceGate.RelaunchArgument, StringComparison.OrdinalIgnoreCase));
        var gate = SingleInstanceGate.TryAcquire(
            AppHost.GetAppDataPath(),
            isRelaunch ? TimeSpan.FromSeconds(10) : default);
        if (!gate.IsPrimary)
        {
            LogInstanceGateDeferred(isRelaunch);
            gate.Dispose();
            return;
        }

        InstanceGate = gate;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppHost.HandleUnhandledException(e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppHost.HandleUnobservedTaskException(e.Exception);
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            gate.Dispose();
        }

        // On Linux, Avalonia's DBus teardown races the stopped dispatcher once the main loop ends
        // (AvaloniaUI/Avalonia#19523): a TaskCanceledException escapes an async void on a worker thread and
        // the runtime prints an unhandled-exception banner no handler can suppress. Every shutdown task has
        // already run (AppHost.ShutdownAsync, host stop, instance-lock release) — exit now instead of letting
        // the runtime teardown open the race window.
        Log.CloseAndFlush();
        Environment.Exit(0);
    }

    // Log.Logger isn't configured until AppHost.Build runs, which this exit path never reaches, so a
    // throwaway file logger captures the outcome. A relaunch landing here means the restart failed: the
    // outgoing instance never released the lock within the wait window.
    private static void LogInstanceGateDeferred(bool isRelaunch)
    {
        try
        {
            using var logger = new LoggerConfiguration()
                .WriteTo.File(
                    Path.Combine(AppHost.GetAppDataPath(), "logs", "bookdb-.log"),
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                    shared: true)
                .CreateLogger();

            if (isRelaunch)
                logger.Warning("Relaunch could not acquire the single-instance lock in time; the outgoing instance is still running and the restart did not complete.");
            else
                logger.Information("Another instance is already running; deferring to it and exiting.");
        }
        catch
        {
            // Diagnostics must never block exit.
        }
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        GC.KeepAlive(typeof(Avalonia.Svg.Skia.SvgImageExtension).Assembly);
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
