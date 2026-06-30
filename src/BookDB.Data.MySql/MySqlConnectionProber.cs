using System;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Models;
using Microsoft.EntityFrameworkCore;
using Microting.EntityFrameworkCore.MySql.Infrastructure;
using MySqlConnector;

namespace BookDB.Data.MySql;

/// <inheritdoc cref="IMySqlConnectionProber"/>
public sealed class MySqlConnectionProber : IMySqlConnectionProber
{
    /// <summary>Oldest MySQL version BookDB's schema can run on (InnoDB FULLTEXT, utf8mb4, datetime(6), and the
    /// modern collations the create script depends on).</summary>
    public const string MinimumMySqlVersionText = "8.0";

    /// <summary>Oldest MariaDB version BookDB's schema can run on (same feature floor as MySQL 8.0).</summary>
    public const string MinimumMariaDbVersionText = "10.6";

    private static readonly Version MinimumMySqlVersion = new(8, 0);
    private static readonly Version MinimumMariaDbVersion = new(10, 6);

    // Set when resolved from DI (active backend); null for the throwaway probers in tests / Settings stubs. A
    // successful probe records the detected version here so the DbContext options builder reuses it (see
    // MySqlServerVersionCache).
    private readonly MySqlServerVersionCache? _versionCache;

    public MySqlConnectionProber(MySqlServerVersionCache? versionCache = null) => _versionCache = versionCache;

    // The schema needs InnoDB FULLTEXT, utf8mb4, datetime(6), and modern collations — all present from MySQL 8.0
    // / MariaDB 10.6. Older servers can't run the create script, so the probe rejects them up front: a move never
    // starts (and never writes a safety backup) against a target it can't migrate to. The two engines have
    // independent floors, so the family must be known — hence ServerVersion.AutoDetect, which also unwraps the
    // MariaDB "5.5.5-" handshake prefix that a naive version-string parse would misread.
    public static bool IsSupportedVersion(ServerType type, Version version) => type switch
    {
        ServerType.MariaDb => version >= MinimumMariaDbVersion,
        _ => version >= MinimumMySqlVersion,
    };

    public async Task<ConnectionProbeResult> ProbeAsync(
        MySqlOptions options, string? password, CancellationToken ct = default)
    {
        // Throwaway, non-pooled connection: a failed probe must not poison the app's pool, and the short connect
        // timeout from the factory only bounds the attempt when pooling is off.
        var connectionString = new MySqlConnectionStringBuilder(MySqlConnectionStringFactory.Build(options, password))
        {
            Pooling = false,
        }.ConnectionString;

        try
        {
            // AutoDetect opens a connection to read the family + version (and so also proves the server is
            // reachable and the credentials valid). It returns the true MariaDB version, not the "5.5.5-" alias.
            var serverVersion = await ServerVersion.AutoDetectAsync(connectionString, ct);

            if (!IsSupportedVersion(serverVersion.Type, serverVersion.Version))
                return ConnectionProbeResult.Failed(
                    ConnectionProbeStatus.UnsupportedServerVersion, DescribeVersion(serverVersion));

            // Hand the detected version to the options builder so EF doesn't re-probe (or family-guess) for this
            // same server moments later when it builds the DbContext options.
            _versionCache?.Record(serverVersion);

            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(ct);
            var bookCount = await TryReadBookCountAsync(connection, ct);
            return ConnectionProbeResult.Succeeded(DescribeVersion(serverVersion), bookCount);
        }
        catch (Exception ex)
        {
            return ConnectionProbeResult.Failed(Classify(ex), ex.Message);
        }
    }

    private static string DescribeVersion(ServerVersion serverVersion) =>
        $"{(serverVersion.Type == ServerType.MariaDb ? "MariaDB" : "MySQL")} {serverVersion.Version}";

    /// <summary>
    /// Reads the book count, or returns <c>null</c> when the Book table is absent — a reachable server that is
    /// not yet a BookDB database is a successful probe, not an error.
    /// </summary>
    private static async Task<int?> TryReadBookCountAsync(MySqlConnection connection, CancellationToken ct)
    {
        try
        {
            await using var command = new MySqlCommand("SELECT COUNT(*) FROM `Book`", connection);
            var scalar = await command.ExecuteScalarAsync(ct);
            return scalar is long count ? (int)count : null;
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.NoSuchTable)
        {
            return null;
        }
    }

    /// <summary>
    /// Classifies a probe failure into a user-facing category, walking the exception chain so a wrapped socket,
    /// auth, or TLS cause is recognised rather than reported as Unknown. Shared with
    /// <see cref="MySqlConnectionFailureClassifier"/> so the same failure reads identically in Settings, at
    /// startup, and mid-session.
    /// </summary>
    public static ConnectionProbeStatus Classify(Exception exception)
    {
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case MySqlException { ErrorCode: MySqlErrorCode.AccessDenied or MySqlErrorCode.DatabaseAccessDenied }:
                    return ConnectionProbeStatus.AuthenticationFailed;
                // A command/query timeout is a slow statement, not a dropped link — classify it before the
                // connection-level check (which deliberately excludes it).
                case MySqlException { ErrorCode: MySqlErrorCode.CommandTimeoutExpired or MySqlErrorCode.QueryTimeout }:
                    return ConnectionProbeStatus.Timeout;
                case MySqlException mysql when IsConnectionLevelError(mysql.ErrorCode):
                    return ConnectionProbeStatus.ConnectionRefused;
                case TimeoutException:
                    return ConnectionProbeStatus.Timeout;
                case SocketException { SocketErrorCode: SocketError.ConnectionRefused }:
                    return ConnectionProbeStatus.ConnectionRefused;
                case AuthenticationException:
                    return ConnectionProbeStatus.TlsError;
            }
        }

        return ConnectionProbeStatus.Unknown;
    }

    // The MySqlConnector error codes that mean the server is unreachable or the link dropped (transport-level), as
    // opposed to a server SQL error such as a constraint violation. Single source of truth for both Classify above
    // and MySqlConnectionFailureClassifier.IsConnectionLoss, so the two can't drift. A command/query timeout is
    // deliberately NOT here: a slow statement is not a lost connection.
    public static bool IsConnectionLevelError(MySqlErrorCode code) => code is
        MySqlErrorCode.UnableToConnectToHost
        or MySqlErrorCode.ConnectionCountError
        or MySqlErrorCode.ServerShutdown
        or MySqlErrorCode.AbortingConnection
        or MySqlErrorCode.NetReadError
        or MySqlErrorCode.NetReadErrorFromPipe
        or MySqlErrorCode.NetReadInterrupted
        or MySqlErrorCode.NetPacketsOutOfOrder
        or MySqlErrorCode.IPSocketError;
}
