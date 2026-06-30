using System;
using System.IO;
using System.Net.Sockets;
using BookDB.Data.Interfaces;
using MySqlConnector;

namespace BookDB.Data.MySql;

/// <inheritdoc cref="IConnectionFailureClassifier"/>
public sealed class MySqlConnectionFailureClassifier : IConnectionFailureClassifier
{
    public bool IsConnectionLoss(Exception exception)
    {
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                // MySqlConnector surfaces both server SQL errors (duplicate key, FK, …) and transport failures as
                // the same MySqlException type, so we split on the error code: only a connection-level code is a
                // loss (shared with the probe, so the two can't drift). A SQL-error code is NOT treated as a loss,
                // but we keep walking rather than returning false, because a real transport failure can be wrapped
                // with an inner Socket/IO exception below.
                case MySqlException mysql when MySqlConnectionProber.IsConnectionLevelError(mysql.ErrorCode):
                    return true;
                case SocketException:
                case IOException:        // EndOfStreamException derives from IOException
                case TimeoutException:
                    return true;
            }
        }

        return false;
    }

    public ConnectionProbeStatus Classify(Exception exception) =>
        MySqlConnectionProber.Classify(exception);
}
