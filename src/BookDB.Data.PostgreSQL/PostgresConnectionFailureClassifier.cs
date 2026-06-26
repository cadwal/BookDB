using System;
using System.IO;
using System.Net.Sockets;
using BookDB.Data.Interfaces;
using Npgsql;

namespace BookDB.Data.PostgreSQL;

/// <inheritdoc cref="IConnectionFailureClassifier"/>
public sealed class PostgresConnectionFailureClassifier : IConnectionFailureClassifier
{
    public bool IsConnectionLoss(Exception exception)
    {
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                // A PostgresException is a server-side SQL error (e.g. a constraint violation): the connection
                // is fine, so it must NOT be treated as a loss. Any other NpgsqlException is transport-level.
                case PostgresException:
                    return false;
                case NpgsqlException:
                case SocketException:
                case IOException:
                case TimeoutException:
                    return true;
            }
        }

        return false;
    }

    public ConnectionProbeStatus Classify(Exception exception) =>
        PostgresConnectionProber.Classify(exception);
}
