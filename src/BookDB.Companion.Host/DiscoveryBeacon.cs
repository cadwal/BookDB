using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Contracts;

namespace BookDB.Companion.Host;

/// <summary>
/// Answers discovery probes for one desktop instance. A probe naming this instance gets a unicast reply
/// with the address to dial; anything else is ignored — including probes for other desktops, which is what
/// keeps a shared LAN quiet.
/// </summary>
public sealed class DiscoveryBeacon : IAsyncDisposable
{
    private readonly string _instanceId;
    private readonly int _companionPort;

    private Socket? _socket;
    private CancellationTokenSource? _stopping;
    private Task? _loop;

    public DiscoveryBeacon(string instanceId, int companionPort)
    {
        _instanceId = instanceId;
        _companionPort = companionPort;
    }

    public bool IsListening => _socket is not null;

    /// <summary>UDP, on the companion's own number — see <see cref="DiscoveryProtocol"/>.</summary>
    public int Port => _companionPort;

    /// <summary>
    /// Starts listening, reporting false instead of throwing when the port is unavailable: rediscovery is a
    /// convenience, and losing it must never stop the companion itself from serving.
    /// </summary>
    public bool TryStart()
    {
        if (_socket is not null || string.IsNullOrEmpty(_instanceId))
        {
            return false;
        }

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            // Tells us which local interface each probe arrived on. The interface, not the destination the
            // datagram carries: a phone broadcasts, so that destination is the broadcast address.
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, Port));
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            socket.Dispose();
            return false;
        }

        _socket = socket;
        _stopping = new CancellationTokenSource();
        _loop = Task.Run(() => ListenAsync(socket, _stopping.Token));
        return true;
    }

    public async Task StopAsync()
    {
        var socket = _socket;
        if (socket is null)
        {
            return;
        }

        _socket = null;
        _stopping!.Cancel();
        socket.Dispose();

        try
        {
            await _loop!.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected: cancelling the receive is how the loop ends.
        }

        _stopping.Dispose();
        _stopping = null;
        _loop = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    /// <summary>
    /// The address to tell the prober to dial. The destination a datagram carries is only dialable when the
    /// probe was aimed at us; a phone broadcasts, so that field holds the broadcast address and answering
    /// with it hands back something nothing can connect to. The interface the datagram arrived on is the
    /// part that survives a broadcast, so that interface's own address is the answer.
    /// </summary>
    private static string ReplyHost(SocketReceiveMessageFromResult received) =>
        ChooseReplyHost(received.PacketInformation.Address, received.PacketInformation.Interface);

    /// <summary>
    /// Split out from the socket so it can be tested directly: exercising it over a real broadcast needs the
    /// discovery port open in the host's own firewall, which a unit test cannot arrange.
    /// </summary>
    internal static string ChooseReplyHost(IPAddress destination, int interfaceIndex)
    {
        if (LocalAddresses.IsOwnAddress(destination))
        {
            return destination.ToString();
        }

        return LocalAddresses.OnInterface(interfaceIndex)
            ?? LocalAddresses.Best()
            ?? destination.ToString();
    }

    private async Task ListenAsync(Socket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[DiscoveryProtocol.MaxDatagramBytes];
        EndPoint anySender = new IPEndPoint(IPAddress.Any, 0);

        while (!cancellationToken.IsCancellationRequested)
        {
            SocketReceiveMessageFromResult received;
            try
            {
                received = await socket
                    .ReceiveMessageFromAsync(buffer, SocketFlags.None, anySender, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                // A refused ICMP response from an earlier reply surfaces here; the listener stays up.
                continue;
            }

            string text = Encoding.UTF8.GetString(buffer, 0, received.ReceivedBytes);
            if (!DiscoveryProbe.TryParse(text, out var probe)
                || !string.Equals(probe.InstanceId, _instanceId, StringComparison.Ordinal))
            {
                continue;
            }

            var reply = new DiscoveryReply(_instanceId, ReplyHost(received), _companionPort);
            byte[] bytes = Encoding.UTF8.GetBytes(reply.Format());

            try
            {
                await socket.SendToAsync(bytes, SocketFlags.None, received.RemoteEndPoint, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                // The prober vanished between probe and reply; nothing to do but wait for the next one.
            }
        }
    }
}
