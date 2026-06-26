using System;
using System.Threading;
using System.Threading.Tasks;

namespace BookDB.Logic.Services;

/// <summary>
/// Surfaces mid-session reachability of a remote database to the shell: an indicator while retrying, a refresh
/// signal on reconnection, and an escalation signal when the loss outlasts the retry window.
/// </summary>
public interface IConnectionHealthMonitor
{
    /// <summary>True only for a remote backend; the local SQLite backend has no connection to lose.</summary>
    bool IsEnabled { get; }

    ConnectionHealth State { get; }

    /// <summary>Raised on every state transition (e.g. for the status-bar indicator).</summary>
    event EventHandler? StateChanged;

    /// <summary>Raised once when the database becomes reachable again, so views can reload.</summary>
    event EventHandler? Reconnected;

    /// <summary>Raised once when the loss has lasted past the escalation window.</summary>
    event EventHandler? Escalated;

    /// <summary>Called by a read or write path that just observed a connection loss; starts the retry cycle.</summary>
    void ReportConnectionFailure();

    /// <summary>One reachability check; the production monitor calls this on a backoff loop.</summary>
    Task CheckOnceAsync(CancellationToken ct = default);
}
