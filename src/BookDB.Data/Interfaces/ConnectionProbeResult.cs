namespace BookDB.Data.Interfaces;

/// <summary>
/// The classified outcome of a one-shot connection probe. The status is the localization key the Settings UI
/// maps to a message, so the data layer never produces user-facing English.
/// </summary>
public enum ConnectionProbeStatus
{
    Success,
    AuthenticationFailed,
    ConnectionRefused,
    Timeout,
    TlsError,
    UnsupportedServerVersion,
    Unknown,
}

/// <summary>
/// Result of probing a database server: on success the server version and (when the database already holds a
/// BookDB schema) the book count; on failure a classified <see cref="ConnectionProbeStatus"/> plus the raw
/// error detail for the catch-all message.
/// </summary>
public sealed record ConnectionProbeResult
{
    public ConnectionProbeStatus Status { get; init; }

    /// <summary>Server version on success; otherwise <c>null</c>.</summary>
    public string? ServerVersion { get; init; }

    /// <summary>Book count on success, or <c>null</c> when the server is reachable but has no BookDB tables yet.</summary>
    public int? BookCount { get; init; }

    /// <summary>Raw error text for the unknown-error message; <c>null</c> on success.</summary>
    public string? ErrorDetail { get; init; }

    public bool IsSuccess => Status == ConnectionProbeStatus.Success;

    public static ConnectionProbeResult Succeeded(string serverVersion, int? bookCount) =>
        new() { Status = ConnectionProbeStatus.Success, ServerVersion = serverVersion, BookCount = bookCount };

    public static ConnectionProbeResult Failed(ConnectionProbeStatus status, string? errorDetail) =>
        new() { Status = status, ErrorDetail = errorDetail };
}
