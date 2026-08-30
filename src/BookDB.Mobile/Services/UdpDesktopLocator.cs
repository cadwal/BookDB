using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Contracts;

namespace BookDB.Mobile.Services;

/// <summary>Broadcasts a probe naming the paired computer and takes the first unicast answer that names it
/// back. Plain UDP in both directions — nothing is joined or listened for multicast, so no Android multicast
/// lock is involved.</summary>
public sealed class UdpDesktopLocator : IDesktopLocator
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    private readonly IReadOnlyList<IPAddress>? _destinations;
    private readonly TimeSpan _timeout;

    /// <param name="destinations">Where to send the probe; by default every local broadcast address.</param>
    public UdpDesktopLocator(IReadOnlyList<IPAddress>? destinations = null, TimeSpan? timeout = null)
    {
        _destinations = destinations;
        _timeout = timeout ?? DefaultTimeout;
    }

    public async Task<DiscoveryReply?> LocateAsync(
        string instanceId, int port, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(instanceId) || port is <= 0 or > 65535)
            return null;

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.EnableBroadcast = true;
        socket.Bind(new IPEndPoint(IPAddress.Any, 0));

        byte[] probe = Encoding.UTF8.GetBytes(new DiscoveryProbe(instanceId).Format());
        foreach (var destination in _destinations ?? BroadcastAddresses.Local())
        {
            try
            {
                await socket.SendToAsync(probe, SocketFlags.None, new IPEndPoint(destination, port), ct);
            }
            catch (SocketException)
            {
                // One unreachable interface must not stop the probe going out on the others.
            }
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_timeout);

        byte[] buffer = new byte[DiscoveryProtocol.MaxDatagramBytes];
        var anySender = new IPEndPoint(IPAddress.Any, 0);

        // The deadline is checked here as well as awaited on: a socket that has taken an ICMP error can fail
        // its receives outright, and without this the retry below would spin instead of giving up.
        while (!deadline.IsCancellationRequested)
        {
            SocketReceiveFromResult received;
            try
            {
                received = await socket.ReceiveFromAsync(buffer, SocketFlags.None, anySender, deadline.Token);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (SocketException)
            {
                // A computer that has stopped listening answers with an ICMP "port unreachable", which the OS
                // hands back here as a reset on the *next* receive instead of as silence. Both mean "no answer
                // from that address"; keep listening until the deadline in case another one replies.
                continue;
            }

            string text = Encoding.UTF8.GetString(buffer, 0, received.ReceivedBytes);
            if (DiscoveryReply.TryParse(text, out var reply)
                && string.Equals(reply.InstanceId, instanceId, StringComparison.Ordinal))
            {
                return reply;
            }
        }

        return null;
    }
}
