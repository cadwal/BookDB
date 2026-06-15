using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;

namespace BookDB.Desktop;

internal sealed class Program
{
    /// <summary>The single-instance gate for this process; consumed by <see cref="App"/> to wire window activation.</summary>
    internal static SingleInstanceGate? InstanceGate { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        var gate = SingleInstanceGate.TryAcquire(AppHost.GetAppDataPath());
        if (!gate.IsPrimary)
        {
            // Another instance is already running and has been signalled to come forward — exit quietly.
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
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        GC.KeepAlive(typeof(Avalonia.Svg.Skia.SvgImageExtension).Assembly);
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
