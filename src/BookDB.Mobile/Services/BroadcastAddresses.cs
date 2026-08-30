using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace BookDB.Mobile.Services;

/// <summary>Where a discovery probe is sent. Wi-Fi networks routinely drop the all-ones broadcast, so each
/// interface's own directed broadcast is probed as well — that is the one that reliably reaches a computer on
/// the same subnet.</summary>
public static class BroadcastAddresses
{
    /// <summary>The broadcast address of the subnet an interface address belongs to: its host bits set.</summary>
    public static IPAddress DirectedBroadcast(IPAddress address, IPAddress mask)
    {
        byte[] host = address.GetAddressBytes();
        byte[] bits = mask.GetAddressBytes();
        if (host.Length != 4 || bits.Length != 4)
            throw new ArgumentException("Directed broadcast is an IPv4 notion.", nameof(address));

        for (int i = 0; i < host.Length; i++)
            host[i] |= (byte)~bits[i];

        return new IPAddress(host);
    }

    public static IReadOnlyList<IPAddress> Local()
    {
        var destinations = new List<IPAddress> { IPAddress.Broadcast };
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up
                    || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null)
                        continue;

                    var directed = DirectedBroadcast(unicast.Address, unicast.IPv4Mask);
                    if (!destinations.Contains(directed))
                        destinations.Add(directed);
                }
            }
        }
        catch (NetworkInformationException)
        {
            // Some Android builds refuse to enumerate interfaces; the all-ones broadcast still stands a chance.
        }

        return destinations;
    }
}
