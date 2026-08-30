namespace BookDB.Companion.Host;

/// <summary>
/// How the listener is bound. These are fixed for the lifetime of a run — changing the port means
/// restarting the host — unlike the values in <see cref="ICompanionEnvironment"/>, which are read per call.
/// </summary>
public sealed record CompanionHostOptions
{
    /// <summary>A high port so binding never needs elevation. 0 lets the OS choose, which tests rely on.</summary>
    public int Port { get; init; } = 7443;

    /// <summary>
    /// False binds loopback only — nothing on the LAN can reach the host, and no firewall prompt appears.
    /// </summary>
    public bool BindAllInterfaces { get; init; } = true;

    /// <summary>
    /// Largest single message accepted. A photo downscaled to the desktop's capture settings is a few
    /// hundred kilobytes, so this leaves an order of magnitude of headroom and still bounds what one
    /// message can cost the desktop.
    /// </summary>
    public int MaxMessageBytes { get; init; } = 4 * 1024 * 1024;
}
