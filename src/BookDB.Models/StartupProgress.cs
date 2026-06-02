using System;

namespace BookDB.Models;

/// <summary>
/// Coarse phases of application startup, reported to the splash screen in order.
/// </summary>
public enum StartupStage
{
    Initializing,
    ApplyingMigrations,
    LoadingLibrary,
    RestoringSession,
    Finishing
}

/// <summary>
/// A single startup progress update. <see cref="Current"/> / <see cref="Total"/> are only
/// meaningful for stages that report sub-progress (currently <see cref="StartupStage.ApplyingMigrations"/>).
/// </summary>
public readonly record struct StartupProgressReport(StartupStage Stage, int Current, int Total);

/// <summary>
/// Progress channel for application startup. Implementations simply raise <see cref="ProgressChanged"/>;
/// they do not touch the UI. Subscribers are responsible for marshalling to the UI thread.
/// </summary>
public interface IStartupProgressReporter
{
    event Action<StartupProgressReport>? ProgressChanged;

    void Report(StartupStage stage, int current = 0, int total = 0);
}

/// <summary>
/// Default <see cref="IStartupProgressReporter"/> — raises the event on the calling thread.
/// Registered as a singleton so the splash ViewModel and the startup steps share one instance.
/// </summary>
public sealed class StartupProgressReporter : IStartupProgressReporter
{
    public event Action<StartupProgressReport>? ProgressChanged;

    public void Report(StartupStage stage, int current = 0, int total = 0)
    {
        ProgressChanged?.Invoke(new StartupProgressReport(stage, current, total));
    }
}
