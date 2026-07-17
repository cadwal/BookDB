using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;
using Avalonia.Threading;
using BookDB.Desktop.Tests.Helpers;
using Xunit;

[assembly: AssemblyFixture(typeof(UiThreadAssemblyFixture))]

namespace BookDB.Desktop.Tests.Helpers;

/// <summary>
/// Runs a delegate on a single dedicated thread that owns <see cref="Dispatcher.UIThread"/>.
///
/// Avalonia 12 binds the UI-thread dispatcher to the first thread that touches it and enforces thread
/// affinity, so any test that pumps it (<c>Dispatcher.UIThread.RunJobs()</c>) must post and pump on that
/// one thread — xunit otherwise hops the test across worker threads and the pump throws a cross-thread
/// access error. Wrapping the whole body here also constructs view-models (and their DispatcherTimers, which
/// capture the current dispatcher) on the same thread. The app only ever posts on the real UI thread.
/// </summary>
internal static class UiThread
{
    private static readonly BlockingCollection<Action> Queue = new();

    static UiThread()
    {
        var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            _ = Dispatcher.UIThread; // binds the singleton dispatcher to this thread
            ready.Set();
            foreach (var work in Queue.GetConsumingEnumerable())
                work();
        })
        {
            IsBackground = true,
            Name = "test-ui-thread",
        };
        thread.Start();
        ready.Wait();
    }

    /// <summary>Forces the static constructor (and with it the dispatcher binding) to run now.</summary>
    internal static void EnsureStarted() { }

    public static void Run(Action body)
    {
        using var done = new ManualResetEventSlim();
        ExceptionDispatchInfo? error = null;
        Queue.Add(() =>
        {
            try { body(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
            finally { done.Set(); }
        });
        done.Wait();
        error?.Throw();
    }
}

/// <summary>
/// Binds the dispatcher to the dedicated thread before any test in the assembly runs: ownership goes
/// to the first thread that touches <see cref="Dispatcher.UIThread"/>, and a test constructing a
/// view-model on an xunit worker thread must not win that race — that race is why full-assembly runs
/// failed intermittently while the pumping tests passed in isolation. An assembly fixture (not a
/// module initializer) because the static constructor blocks on a spawned thread, which deadlocks
/// inside the loader-lock window a module initializer runs under.
/// </summary>
public sealed class UiThreadAssemblyFixture
{
    public UiThreadAssemblyFixture() => UiThread.EnsureStarted();
}
