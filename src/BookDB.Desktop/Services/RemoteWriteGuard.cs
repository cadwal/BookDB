using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Localization;
using BookDB.Logic.Services;

namespace BookDB.Desktop.Services;

public enum WriteFailureChoice { Retry, Discard }

public enum WriteResult { Saved, Discarded }

/// <summary>
/// Runs a database write and, if the connection is lost while it executes, blocks with a Retry / Discard modal so
/// the user's work is never silently dropped. Ordinary errors (constraint violations, etc.) are not intercepted —
/// they propagate to the caller's existing handling. No-op interception on the local SQLite backend, whose
/// classifier never reports a connection loss.
/// </summary>
public interface IRemoteWriteGuard
{
    Task<WriteResult> ExecuteAsync(Func<CancellationToken, Task> write, CancellationToken ct = default);
}

/// <inheritdoc cref="IRemoteWriteGuard"/>
public sealed class RemoteWriteGuard : IRemoteWriteGuard
{
    private readonly IConnectionFailureClassifier _classifier;
    private readonly IConnectionHealthMonitor _monitor;
    private readonly IWindowService _windowService;

    public RemoteWriteGuard(
        IConnectionFailureClassifier classifier,
        IConnectionHealthMonitor monitor,
        IWindowService windowService)
    {
        _classifier = classifier;
        _monitor = monitor;
        _windowService = windowService;
    }

    public async Task<WriteResult> ExecuteAsync(Func<CancellationToken, Task> write, CancellationToken ct = default)
    {
        while (true)
        {
            try
            {
                await write(ct);
                return WriteResult.Saved;
            }
            catch (Exception ex) when (_classifier.IsConnectionLoss(ex))
            {
                // A write failed on a dropped connection: also nudge the read-path indicator, then let the user decide.
                _monitor.ReportConnectionFailure();
                var message = ConnectionErrorText.Describe(_classifier.Classify(ex));
                var choice = await _windowService.ShowWriteFailureDialogAsync(message);
                if (choice == WriteFailureChoice.Discard)
                    return WriteResult.Discarded;
                // Retry: loop and attempt the write again.
            }
        }
    }
}
