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
/// Progress channel for application startup. Updates are the shared <see cref="ProgressUpdate{TStep}"/> over
/// <see cref="StartupStage"/> (<c>Current</c>/<c>Total</c> only meaningful for stages that report sub-progress,
/// currently <see cref="StartupStage.ApplyingMigrations"/>). Implementations simply raise
/// <see cref="ProgressChanged"/>; they do not touch the UI. Subscribers marshal to the UI thread.
/// </summary>
public interface IStartupProgressReporter
{
    event Action<ProgressUpdate<StartupStage>>? ProgressChanged;

    void Report(StartupStage stage, int current = 0, int total = 0);
}

/// <summary>
/// Default <see cref="IStartupProgressReporter"/> — raises the event on the calling thread.
/// Registered as a singleton so the splash ViewModel and the startup steps share one instance.
/// </summary>
public sealed class StartupProgressReporter : IStartupProgressReporter
{
    public event Action<ProgressUpdate<StartupStage>>? ProgressChanged;

    public void Report(StartupStage stage, int current = 0, int total = 0)
    {
        ProgressChanged?.Invoke(new ProgressUpdate<StartupStage>(stage, current, total));
    }
}
