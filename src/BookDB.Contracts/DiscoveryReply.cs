using System;
using System.Globalization;

namespace BookDB.Contracts;

/// <summary>
/// The desktop's answer to a probe. <paramref name="Host"/> is the address of the interface the probe
/// arrived on, so a multi-homed desktop names the one that is actually on the phone's subnet.
/// </summary>
public sealed record DiscoveryReply(string InstanceId, string Host, int Port)
{
    private const string Verb = "OFFER";

    public string Format() =>
        $"{DiscoveryProtocol.Prefix} {Verb} {InstanceId} {Host} {Port.ToString(CultureInfo.InvariantCulture)}";

    public static bool TryParse(string text, out DiscoveryReply reply)
    {
        reply = new DiscoveryReply("", "", 0);

        string[] parts = (text ?? "").Split(' ');
        if (parts.Length != 5 || parts[0] != DiscoveryProtocol.Prefix || parts[1] != Verb
            || parts[2].Length == 0 || parts[3].Length == 0
            || !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int port)
            || port is <= 0 or > 65535)
        {
            return false;
        }

        reply = new DiscoveryReply(parts[2], parts[3], port);
        return true;
    }
}
