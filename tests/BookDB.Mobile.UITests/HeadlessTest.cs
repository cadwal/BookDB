using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Logging;
using Avalonia.Threading;
using BookDB.Mobile;
using Xunit;

namespace BookDB.Mobile.UITests;

/// <summary>
/// Builds the headless phone app for the test session: the real <see cref="App"/>, so the smokes render against
/// the same theme, resources and <c>ViewLocator</c> the phone runs with. <see cref="App.HeadlessTestMode"/>
/// keeps app init to that and skips composing the live shell, which needs a platform head to exist.
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
/// Base class for the phone's headless UI tests. Under the assembly's <c>[AvaloniaTestFramework]</c> the body
/// already runs on the Avalonia UI thread, so <see cref="RunUi"/> installs the binding-error gate around it and
/// fails the test if any binding error was raised.
/// </summary>
public abstract class HeadlessTest
{
    internal static readonly BindingErrorSink Sink = new();

    [ModuleInitializer]
    internal static void Init()
    {
        // Determinism on a Swedish dev OS: pin the UI culture at assembly load (before the dispatcher thread is
        // created) and assert on control identity / resource keys rather than on words.
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
