using System;

namespace BookDB.Contracts;

/// <summary>Broadcast by a phone looking for the desktop it is already paired with.</summary>
public sealed record DiscoveryProbe(string InstanceId)
{
    private const string Verb = "PROBE";

    public string Format() => $"{DiscoveryProtocol.Prefix} {Verb} {InstanceId}";

    public static bool TryParse(string text, out DiscoveryProbe probe)
    {
        probe = new DiscoveryProbe("");

        string[] parts = (text ?? "").Split(' ');
        if (parts.Length != 3 || parts[0] != DiscoveryProtocol.Prefix || parts[1] != Verb
            || parts[2].Length == 0)
        {
            return false;
        }

        probe = new DiscoveryProbe(parts[2]);
        return true;
    }
}
