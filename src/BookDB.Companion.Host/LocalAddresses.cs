using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace BookDB.Companion.Host;

/// <summary>
/// The addresses a device on the same network could dial this desktop on. The pairing code has to name
/// one, and only the desktop can know which network the user means — so the best guess leads and the rest
/// are offered as alternatives.
/// </summary>
public static class LocalAddresses
{
    /// <summary>
    /// Usable IPv4 addresses, best guess first. Virtual adapters (VPNs, hypervisor host-only networks)
    /// sort last: they are up and routable but almost never the network the phone or tablet is on.
    /// </summary>
    public static IReadOnlyList<string> Candidates()
    {
        var found = new List<(string Address, int Rank)>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            int rank = nic.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Wireless80211 => 0,
                NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet => 0,
                _ => 1,
            };

            // A tunnel or a host-only adapter is reachable but is not where a household device lives.
            if (nic.Description.IndexOf("virtual", System.StringComparison.OrdinalIgnoreCase) >= 0
                || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
            {
                rank = 2;
            }

            foreach (var info in nic.GetIPProperties().UnicastAddresses)
            {
                if (info.Address.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(info.Address))
                {
                    found.Add((info.Address.ToString(), rank));
                }
            }
        }

        return [.. found.OrderBy(a => a.Rank).Select(a => a.Address).Distinct()];
    }

    /// <summary>The address to offer by default, or null when the machine has no usable network address.</summary>
    public static string? Best() => Candidates().FirstOrDefault();

    /// <summary>
    /// The network this machine is on, in CIDR form, for the address the pairing code would name — or null
    /// when there is no usable address or the OS reports no mask for it. This is what makes a firewall rule
    /// scoped to the LAN rather than open to everyone: the phone is on this network and nothing else has to
    /// be let in.
    /// </summary>
    public static string? LocalSubnet()
    {
        if (Best() is not { } best || !IPAddress.TryParse(best, out var address))
        {
            return null;
        }

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            foreach (var info in nic.GetIPProperties().UnicastAddresses)
            {
                if (info.Address.Equals(address) && info.IPv4Mask is { } mask
                    && !mask.Equals(IPAddress.Any))
                {
                    return $"{NetworkAddress(address, mask)}/{PrefixLength(mask)}";
                }
            }
        }

        return null;
    }

    /// <summary>The address with every host bit cleared — what a firewall wants as the source network.</summary>
    internal static IPAddress NetworkAddress(IPAddress address, IPAddress mask)
    {
        byte[] bytes = address.GetAddressBytes();
        byte[] maskBytes = mask.GetAddressBytes();

        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] &= maskBytes[i];
        }

        return new IPAddress(bytes);
    }

    /// <summary>How many leading bits the mask sets. Masks are contiguous by definition, so counting the
    /// ones is the whole of it.</summary>
    internal static int PrefixLength(IPAddress mask)
    {
        int bits = 0;
        foreach (byte b in mask.GetAddressBytes())
        {
            bits += System.Numerics.BitOperations.PopCount(b);
        }

        return bits;
    }

    /// <summary>
    /// Whether an address is one this machine answers on — the test for "a probe was aimed straight at us"
    /// rather than broadcast at the whole subnet.
    /// </summary>
    public static bool IsOwnAddress(IPAddress address) =>
        IPAddress.IsLoopback(address) || Candidates().Contains(address.ToString());

    /// <summary>
    /// The IPv4 address of one interface, by the index a received datagram reports, or null when that
    /// interface has none. This is how a broadcast probe gets a dialable answer: the destination it carries
    /// is the broadcast address, but the interface it arrived on is real.
    /// </summary>
    public static string? OnInterface(int index)
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            var properties = nic.GetIPProperties();

            int nicIndex;
            try
            {
                nicIndex = properties.GetIPv4Properties()?.Index ?? -1;
            }
            catch (NetworkInformationException)
            {
                // An adapter that has lost IPv4 between enumeration and here is simply not the one we want.
                continue;
            }

            if (nicIndex != index)
            {
                continue;
            }

            foreach (var info in properties.UnicastAddresses)
            {
                if (info.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    return info.Address.ToString();
                }
            }
        }

        return null;
    }
}
