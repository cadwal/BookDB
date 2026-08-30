namespace BookDB.Mobile.Services;

/// <summary>How a pairing attempt ended. The connection-refused cases (expired, spent or revoked code) all
/// arrive as a permission-denied status, since the desktop refuses the identity before the claim runs.</summary>
public enum PairingOutcome
{
    Paired,
    InvalidCode,
    CodeExpired,
    DeviceLimitReached,
    IncompatibleVersion,
    CannotConnect,
    Failed,
}
