namespace BookDB.Contracts;

public static class ContractVersion
{
    /// <summary>
    /// Bumped only when the wire shape changes incompatibly. Both ends compare it during the handshake
    /// and refuse to talk on a mismatch, so it must never be reused for a compatible change.
    /// </summary>
    public const int Current = 1;
}
