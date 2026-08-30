namespace BookDB.Companion.Host;

public enum PairingClaimOutcome
{
    Registered,

    /// <summary>The device already holds a slot — a repeated claim of the same offer, which is not an error.</summary>
    AlreadyRegistered,

    UnknownOrExpired,

    /// <summary>The offer was claimed once already; a second device may not reuse it.</summary>
    AlreadyUsed,

    DeviceLimitReached,
}
