namespace BookDB.Contracts;

/// <summary>
/// The tiny text protocol a phone uses to re-find its desktop after a LAN address change: the phone
/// broadcasts a probe naming the instance it is paired with, and only that desktop answers, unicast.
/// Broadcast out and unicast back — never multicast in — so no Android multicast lock is involved.
///
/// Discovery listens on the companion's own port number, over UDP. TCP and UDP are separate port spaces,
/// so Kestrel holding the port for gRPC leaves the UDP one of the same number free — one number is one
/// setting, one line in the help topic and one port to open in a firewall.
/// </summary>
public static class DiscoveryProtocol
{
    public const string Prefix = "BOOKDB/1";

    /// <summary>Anything longer is not one of ours; it also caps what a stray packet can make us allocate.</summary>
    public const int MaxDatagramBytes = 512;
}
