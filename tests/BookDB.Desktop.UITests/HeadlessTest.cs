using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
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
/// but skip the production startup.
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
/// Base class for headless UI tests. One process-wide Avalonia session (one app per process) is shared; each test
/// body runs on the Avalonia UI thread via <see cref="RunUi"/>, which fails the test if any binding error is raised
/// while it runs. Give each test its own <see cref="TestHost"/> (fresh temp DB) for isolation.
/// </summary>
public abstract class HeadlessTest
{
    private static readonly BindingErrorSink Sink = Install();

    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

    private static BindingErrorSink Install()
    {
        // Determinism on a Swedish dev OS: pin the UI culture before the session thread is created (threads inherit
        // it), and assert on control identity / VM state rather than localized strings.
        var culture = CultureInfo.GetCultureInfo("en");
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;

        var sink = new BindingErrorSink();
        Logger.Sink = sink;
        return sink;
    }

    /// <summary>Runs the test body on the Avalonia UI thread; fails the test if a binding error was raised.</summary>
    protected static async Task RunUi(Func<Task> body)
    {
        var errors = await RunCore(body);
        Assert.True(errors.Count == 0,
            "Binding error(s) raised during the test:\n  " + string.Join("\n  ", errors));
    }

    /// <summary>Runs the body and returns the binding errors raised, without asserting — for the harness self-test.</summary>
    protected static Task<IReadOnlyList<string>> CaptureBindingErrors(Func<Task> body) => RunCore(body);

    private static Task<IReadOnlyList<string>> RunCore(Func<Task> body) =>
        Session.Dispatch(async () =>
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
            return (IReadOnlyList<string>)errors;
        }, CancellationToken.None);
}
