using System;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Models;
using Npgsql;

namespace BookDB.Data.PostgreSQL;

/// <inheritdoc cref="IPostgresConnectionProber"/>
public sealed class PostgresConnectionProber : IPostgresConnectionProber
{
    /// <summary>
    /// Oldest PostgreSQL major version BookDB's schema can run on. The Book full-text column is a stored
    /// generated column (<c>GENERATED ALWAYS AS … STORED</c>), a feature introduced in PostgreSQL 12; on
    /// older servers the schema's create script fails to parse. The probe rejects such a server up front so
    /// a move never starts (and never writes a safety backup) against a target it can't migrate to.
    /// </summary>
    public const int MinimumServerMajorVersion = 12;

    public static bool IsSupportedVersion(Version serverVersion) =>
        serverVersion.Major >= MinimumServerMajorVersion;

    public async Task<ConnectionProbeResult> ProbeAsync(
        PostgresOptions options, string? password, CancellationToken ct = default)
    {
        // Throwaway, non-pooled connection: a failed probe must not poison the app's pool, and the 4s
        // connect timeout from the factory only bounds the attempt when pooling is off.
        var connectionString = new NpgsqlConnectionStringBuilder(
            PostgresConnectionStringFactory.Build(options, password))
        {
            Pooling = false,
        }.ConnectionString;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(ct);

            if (!IsSupportedVersion(connection.PostgreSqlVersion))
                return ConnectionProbeResult.Failed(
                    ConnectionProbeStatus.UnsupportedServerVersion,
                    connection.PostgreSqlVersion.ToString());

            var version = connection.PostgreSqlVersion.ToString();
            var bookCount = await TryReadBookCountAsync(connection, ct);
            return ConnectionProbeResult.Succeeded(version, bookCount);
        }
        catch (Exception ex)
        {
            return ConnectionProbeResult.Failed(Classify(ex), ex.Message);
        }
    }

    /// <summary>
    /// Reads the book count, or returns <c>null</c> when the Book table is absent — a reachable server that is
    /// not yet a BookDB database is a successful probe, not an error.
    /// </summary>
    private static async Task<int?> TryReadBookCountAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        try
        {
            await using var command = new NpgsqlCommand("SELECT count(*) FROM \"Book\"", connection);
            var scalar = await command.ExecuteScalarAsync(ct);
            return scalar is long count ? (int)count : null;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return null;
        }
    }

    /// <summary>
    /// Classifies a probe failure into a user-facing category, walking the exception chain so a wrapped
    /// socket, auth, or TLS cause is recognised rather than reported as Unknown.
    /// </summary>
    public static ConnectionProbeStatus Classify(Exception exception)
    {
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case PostgresException pg when pg.SqlState is PostgresErrorCodes.InvalidPassword
                                                           or PostgresErrorCodes.InvalidAuthorizationSpecification:
                    return ConnectionProbeStatus.AuthenticationFailed;
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
}
