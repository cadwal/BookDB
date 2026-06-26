using System;
using BookDB.Data.Interfaces;

namespace BookDB.Data.Sqlite;

/// <summary>
/// The local SQLite store is a file on the same machine: there is no network connection to lose mid-session, so
/// every failure is an ordinary error left to the normal handling path.
/// </summary>
public sealed class SqliteConnectionFailureClassifier : IConnectionFailureClassifier
{
    public bool IsConnectionLoss(Exception exception) => false;

    public ConnectionProbeStatus Classify(Exception exception) => ConnectionProbeStatus.Unknown;
}
