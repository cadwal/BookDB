using System.Globalization;
using BookDB.Data.Interfaces;
using BookDB.Data.PostgreSQL;

namespace BookDB.Desktop.Localization;

/// <summary>
/// Maps a <see cref="ConnectionProbeStatus"/> to a localized message. Shared by the Settings test, the startup
/// failure dialog, and the mid-session write-failure dialog so the same classification reads identically everywhere.
/// </summary>
public static class ConnectionErrorText
{
    public static string Describe(ConnectionProbeStatus status, string? errorDetail = null) => status switch
    {
        ConnectionProbeStatus.AuthenticationFailed => Resources.Settings_Database_TestError_Auth,
        ConnectionProbeStatus.ConnectionRefused => Resources.Settings_Database_TestError_Refused,
        ConnectionProbeStatus.Timeout => Resources.Settings_Database_TestError_Timeout,
        ConnectionProbeStatus.TlsError => Resources.Settings_Database_TestError_Tls,
        ConnectionProbeStatus.UnsupportedServerVersion => string.Format(
            CultureInfo.CurrentCulture,
            Resources.Settings_Database_TestError_UnsupportedVersion,
            PostgresConnectionProber.MinimumServerMajorVersion,
            errorDetail ?? string.Empty),
        _ => string.Format(CultureInfo.CurrentCulture, Resources.Settings_Database_TestError_Unknown, errorDetail ?? string.Empty),
    };
}
