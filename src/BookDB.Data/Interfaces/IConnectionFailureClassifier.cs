using System;

namespace BookDB.Data.Interfaces;

/// <summary>
/// Decides whether an exception thrown by a live database operation is a lost connection (the server went away
/// mid-session) rather than an ordinary error such as a constraint violation. Registered per backend: the local
/// SQLite store can never lose a network connection, so its implementation always answers <c>false</c>.
/// </summary>
public interface IConnectionFailureClassifier
{
    /// <summary>True when the exception chain indicates a transport-level loss of the database connection.</summary>
    bool IsConnectionLoss(Exception exception);

    /// <summary>The user-facing category for a connection loss, reusing the probe classification so the same
    /// failure reads identically in Settings, at startup, and mid-session.</summary>
    ConnectionProbeStatus Classify(Exception exception);
}
