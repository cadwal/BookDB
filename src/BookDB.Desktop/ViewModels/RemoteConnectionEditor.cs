using System.Collections.Generic;
using System.Globalization;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Localization;
using BookDB.Models;

namespace BookDB.Desktop.ViewModels;

/// <summary>
/// The per-engine facts and pure builders shared by the two server-connection editors — Settings → Database and
/// the Move-library target. Owns the TLS token catalogs, the default port / TLS mode per backend, building the
/// typed options from the entered fields, and the localized test-success message, so the two view models don't
/// each carry their own copy. The view models keep their own (legitimately different) observable wiring and
/// backend-switch flow.
/// </summary>
internal static class RemoteConnectionEditor
{
    // TLS token sets differ per engine (Npgsql's SslMode names vs the MySQL driver's). Built fresh per call so the
    // display names follow the culture active when the editor opens; each view model caches one instance so the
    // selected option keeps reference-identity with its ComboBox items.
    public static IReadOnlyList<SslModeOption> PostgresSslModes() =>
    [
        new SslModeOption("Disable",    Resources.Settings_Database_Tls_Disable),
        new SslModeOption("Prefer",     Resources.Settings_Database_Tls_Prefer),
        new SslModeOption("Require",    Resources.Settings_Database_Tls_Require),
        new SslModeOption("VerifyFull", Resources.Settings_Database_Tls_VerifyFull),
    ];

    public static IReadOnlyList<SslModeOption> MySqlSslModes() =>
    [
        new SslModeOption("None",      Resources.Settings_Database_Tls_None),
        new SslModeOption("Preferred", Resources.Settings_Database_Tls_Preferred),
        new SslModeOption("Required",  Resources.Settings_Database_Tls_Required),
    ];

    // Driver defaults: MySQL/MariaDB 3306 + "Preferred", PostgreSQL (and the SQLite-target placeholder) 5432 + "Require".
    public static string DefaultPort(DatabaseBackend backend) => backend == DatabaseBackend.MySql ? "3306" : "5432";
    public static string DefaultSslMode(DatabaseBackend backend) => backend == DatabaseBackend.MySql ? "Preferred" : "Require";

    public static int ParsePort(string port, DatabaseBackend backend) =>
        int.TryParse(port, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : int.Parse(DefaultPort(backend), CultureInfo.InvariantCulture);

    public static PostgresOptions BuildPostgresOptions(string host, string port, string database, string username, string? sslMode) => new()
    {
        Host = host.Trim(),
        Port = ParsePort(port, DatabaseBackend.PostgreSql),
        Database = database.Trim(),
        Username = username.Trim(),
        SslMode = sslMode ?? DefaultSslMode(DatabaseBackend.PostgreSql),
    };

    public static MySqlOptions BuildMySqlOptions(string host, string port, string database, string username, string? sslMode) => new()
    {
        Host = host.Trim(),
        Port = ParsePort(port, DatabaseBackend.MySql),
        Database = database.Trim(),
        Username = username.Trim(),
        SslMode = sslMode ?? DefaultSslMode(DatabaseBackend.MySql),
    };

    // The MySQL probe's version string already names the engine ("MySQL 8.0.3" / "MariaDB 11.4"), so its message
    // omits the engine prefix the PostgreSQL one hardcodes.
    public static string DescribeSuccess(ConnectionProbeResult result, bool isMySql) => isMySql
        ? (result.BookCount.HasValue
            ? string.Format(CultureInfo.CurrentCulture, Resources.Settings_Database_TestSuccess_MySql, result.ServerVersion, result.BookCount.Value)
            : string.Format(CultureInfo.CurrentCulture, Resources.Settings_Database_TestSuccessUninitialized_MySql, result.ServerVersion))
        : (result.BookCount.HasValue
            ? string.Format(CultureInfo.CurrentCulture, Resources.Settings_Database_TestSuccess, result.ServerVersion, result.BookCount.Value)
            : string.Format(CultureInfo.CurrentCulture, Resources.Settings_Database_TestSuccessUninitialized, result.ServerVersion));
}
