using System;
using System.Text.RegularExpressions;
using BookDB.Models;
using Npgsql;

namespace BookDB.Data.PostgreSQL;

/// <summary>
/// Builds the Npgsql connection string from the non-secret server parameters in <see cref="PostgresOptions"/>
/// (config.json) plus a password supplied separately by the credential store, and redacts the password from any
/// string before it reaches a log. The password is never persisted in config.json, so it is passed in here.
/// </summary>
public static class PostgresConnectionStringFactory
{
    // Connect attempt budget (seconds); short so a down/wrong host fails fast into the retry/failure UX rather
    // than freezing interactive operations while a dead connection is awaited.
    private const int ConnectTimeoutSeconds = 4;

    private const string RedactedPassword = "***";

    public static string Build(PostgresOptions options, string? password = null)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = options.Host,
            Port = options.Port,
            Database = options.Database,
            Username = options.Username,
            SslMode = Enum.TryParse<SslMode>(options.SslMode, ignoreCase: true, out var mode) ? mode : SslMode.Require,
            Timeout = ConnectTimeoutSeconds,
        };

        if (!string.IsNullOrEmpty(password))
            builder.Password = password;

        return builder.ConnectionString;
    }

    /// <summary>
    /// Returns the connection string with any password replaced by a placeholder, safe to log. Falls back to a
    /// coarse textual redaction if the string cannot be parsed, so a malformed value still cannot leak a secret.
    /// </summary>
    public static string Sanitize(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return connectionString;

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrEmpty(builder.Password))
                return builder.ConnectionString;

            builder.Password = RedactedPassword;
            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            return RedactRaw(connectionString);
        }
    }

    // Matches Password= or Pwd= (Npgsql's aliases) and blanks the value up to the next ';'.
    private static string RedactRaw(string connectionString) =>
        Regex.Replace(
            connectionString,
            @"(?<key>\b(?:password|pwd)\s*=)[^;]*",
            "${key}" + RedactedPassword,
            RegexOptions.IgnoreCase);
}
