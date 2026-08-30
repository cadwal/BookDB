namespace BookDB.Mobile.Services;

/// <summary>What the shell's status chip reports about the link to the paired computer.</summary>
public enum ConnectionStatus
{
    Offline,
    Reconnecting,
    Connected,

    /// <summary>
    /// The computer answered and refused us: this device has been removed from its list. Distinct from
    /// Offline because it is not a wait — nothing about retrying, moving network or switching the computer on
    /// will change it, and only pairing again will.
    /// </summary>
    Revoked,
}
