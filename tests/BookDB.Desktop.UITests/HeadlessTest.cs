using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Logging;
using Avalonia.Threading;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Builds the headless Avalonia app for the test session. The session reflects <c>BuildAvaloniaApp</c>, the same
/// convention <c>Program</c> uses; setting <see cref="App.HeadlessTestMode"/> makes app init load resources/themes
/// but skip the production startup. The assembly's <c>[AvaloniaTestFramework]</c> attribute runs every test on the
/// dispatcher thread this builder creates.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        App.HeadlessTestMode = true;
        return AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
    }
}

/// <summary>
/// Base class for headless UI tests. Under the assembly's <c>[AvaloniaTestFramework]</c> the test body already runs
/// on the Avalonia UI thread, so <see cref="RunUi"/> simply installs the binding-error gate around the body and
/// fails the test if any binding error is raised. Give each test its own <see cref="TestHost"/> (fresh temp DB).
/// </summary>
public abstract class HeadlessTest
{
    internal static readonly BindingErrorSink Sink = new();

    [ModuleInitializer]
    internal static void Init()
    {
        // Determinism on a Swedish dev OS: pin the UI culture at assembly load (before the dispatcher thread is
        // created) and assert on control identity / VM state rather than localized strings.
        var culture = CultureInfo.GetCultureInfo("en");
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;

        Logger.Sink = Sink;
    }

    /// <summary>Runs the test body under the binding-error gate; fails the test if a binding error was raised.</summary>
    protected static async Task RunUi(Func<Task> body)
    {
        var errors = await RunCore(body);
        Assert.True(errors.Count == 0,
            "Binding error(s) raised during the test:\n  " + string.Join("\n  ", errors));
    }

    /// <summary>Runs the body and returns the binding errors raised, without asserting — for the harness self-test.</summary>
    protected static Task<IReadOnlyList<string>> CaptureBindingErrors(Func<Task> body) => RunCore(body);

    private static async Task<IReadOnlyList<string>> RunCore(Func<Task> body)
    {
        var errors = new List<string>();
        Sink.Collector = errors;
        try
        {
            await body();
            Dispatcher.UIThread.RunJobs(); // flush any queued binding evaluations before we read the result
        }
        finally
        {
            Sink.Collector = null;
        }
        return errors;
    }
}
