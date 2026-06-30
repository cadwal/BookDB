using System;
using System.Text.RegularExpressions;
using BookDB.Models;
using MySqlConnector;

namespace BookDB.Data.MySql;

/// <summary>
/// Builds the MySqlConnector connection string from the non-secret server parameters in <see cref="MySqlOptions"/>
/// (config.json) plus a password supplied separately by the credential store, and redacts the password from any
/// string before it reaches a log. The password is never persisted in config.json, so it is passed in here.
/// </summary>
public static class MySqlConnectionStringFactory
{
    // Connect attempt budget (seconds); short so a down/wrong host fails fast into the retry/failure UX rather
    // than freezing interactive operations while a dead connection is awaited.
    private const uint ConnectTimeoutSeconds = 4;

    private const string RedactedPassword = "***";

    public static string Build(MySqlOptions options, string? password = null)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = options.Host,
            Port = (uint)options.Port,
            Database = options.Database,
            UserID = options.Username,
            SslMode = Enum.TryParse<MySqlSslMode>(options.SslMode, ignoreCase: true, out var mode) ? mode : MySqlSslMode.Preferred,
            ConnectionTimeout = ConnectTimeoutSeconds,
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
            var builder = new MySqlConnectionStringBuilder(connectionString);
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

    // Matches Password= or Pwd= (MySqlConnector's aliases) and blanks the value up to the next ';'.
    private static string RedactRaw(string connectionString) =>
        Regex.Replace(
            connectionString,
            @"(?<key>\b(?:password|pwd)\s*=)[^;]*",
            "${key}" + RedactedPassword,
            RegexOptions.IgnoreCase);
}
