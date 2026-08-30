namespace BookDB.Contracts;

/// <summary>
/// How a claim ended, as a code the phone localizes itself. Note that an unknown, expired or revoked
/// identity never reaches a claim at all — the connection is refused first — so those cases surface as a
/// permission-denied status rather than as an outcome here.
/// </summary>
public enum ClaimOutcome
{
    Unknown = 0,
    Registered = 1,
    AlreadyRegistered = 2,
    UnknownOrExpired = 3,
    AlreadyUsed = 4,
    DeviceLimitReached = 5,
}
