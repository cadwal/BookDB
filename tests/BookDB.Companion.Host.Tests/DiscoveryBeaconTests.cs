using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Companion.Host;
using BookDB.Contracts;
using Xunit;

namespace BookDB.Companion.Host.Tests;

/// <summary>
/// The beacon over a real UDP socket on loopback: it answers a probe for this desktop, stays silent for
/// anything else, and comes and goes with the host.
/// </summary>
public sealed class DiscoveryBeaconTests
{
    /// <summary>How long to wait for an answer we expect. Generous: under a full-suite load the reply can
    /// be scheduled late, and a slow answer must not read as no answer.</summary>
    private static readonly TimeSpan AnswerWindow = TimeSpan.FromSeconds(3);

    /// <summary>How long to wait when expecting silence. Shorter, since waiting longer only slows the run.</summary>
    private static readonly TimeSpan SilenceWindow = TimeSpan.FromMilliseconds(750);

    /// <summary>Sends one datagram and waits for an answer; null means the beacon stayed quiet.</summary>
    private static async Task<string?> ProbeAsync(int port, string message, TimeSpan? window = null)
    {
        using var prober = new UdpClient(AddressFamily.InterNetwork);
        prober.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        byte[] bytes = Encoding.UTF8.GetBytes(message);
        await prober.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, port));

        using var timeout = new CancellationTokenSource(window ?? AnswerWindow);
        try
        {
            var received = await prober.ReceiveAsync(timeout.Token);
            return Encoding.UTF8.GetString(received.Buffer);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (SocketException)
        {
            // Windows reports the ICMP "port unreachable" from a closed beacon as a reset on the *prober's*
            // next receive. Silence and a reset mean the same thing to a prober, so the phone's must do this too.
            return null;
        }
    }

    /// <summary>A started harness listening for discovery — see the harness for why the port is chosen there.</summary>
    private static async Task<CompanionHostHarness> StartWithDiscoveryAsync()
    {
        var harness = new CompanionHostHarness();
        try
        {
            await harness.StartWithDiscoveryAsync();
            return harness;
        }
        catch
        {
            await harness.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// One number, not two. TCP and UDP are separate port spaces, so the beacon binds the companion's own
    /// port — which is what lets a settings field, a firewall rule and a help topic all name one number.
    /// </summary>
    [Fact]
    public void TheBeaconListensOnTheCompanionPortItself()
    {
        Assert.Equal(7443, new DiscoveryBeacon("instance-1", companionPort: 7443).Port);
    }

    [Fact]
    public async Task AProbeForThisDesktopIsAnsweredWithTheAddressToDial()
    {
        await using var harness = await StartWithDiscoveryAsync();

        string? answer = await ProbeAsync(
            harness.Host.BoundPort,
            new DiscoveryProbe(harness.Environment.InstanceId).Format());

        Assert.NotNull(answer);
        Assert.True(DiscoveryReply.TryParse(answer!, out var reply));
        Assert.Equal(harness.Environment.InstanceId, reply.InstanceId);
        Assert.Equal(harness.Host.BoundPort, reply.Port);
        Assert.Equal("127.0.0.1", reply.Host);
    }

    /// <summary>
    /// The phone broadcasts, and a broadcast datagram carries the broadcast address as its destination. The
    /// beacon used to answer with that address verbatim, so every real rediscovery was told to dial
    /// 192.168.x.255 — undialable, and invisible to every socket test here because they all probe unicast.
    /// </summary>
    [Fact]
    public void ABroadcastDestinationIsNotHandedBackAsTheAddressToDial()
    {
        if (LocalAddresses.Best() is null)
        {
            // A machine with no routable IPv4 has no better answer to give, and nothing to assert about.
            return;
        }

        string host = DiscoveryBeacon.ChooseReplyHost(IPAddress.Broadcast, interfaceIndex: 0);

        Assert.NotEqual("255.255.255.255", host);
        Assert.True(LocalAddresses.IsOwnAddress(IPAddress.Parse(host)), $"not an address we own: {host}");
    }

    /// <summary>A probe aimed straight at one of our addresses is still answered with that same address —
    /// which is what keeps a multi-homed desktop naming the interface the phone can actually reach.</summary>
    [Fact]
    public void AProbeAimedAtUsIsAnsweredWithTheAddressItWasAimedAt()
    {
        Assert.Equal("127.0.0.1", DiscoveryBeacon.ChooseReplyHost(IPAddress.Loopback, interfaceIndex: 0));
    }

    [Fact]
    public async Task AProbeForAnotherDesktopIsIgnored()
    {
        await using var harness = await StartWithDiscoveryAsync();

        string? answer = await ProbeAsync(
            harness.Host.BoundPort,
            new DiscoveryProbe("someone-elses-instance").Format(),
            SilenceWindow);

        Assert.Null(answer);
    }

    [Fact]
    public async Task StrayTrafficOnTheDiscoveryPortIsIgnored()
    {
        await using var harness = await StartWithDiscoveryAsync();

        string? answer = await ProbeAsync(
            harness.Host.BoundPort, "some other protocol's broadcast", SilenceWindow);

        Assert.Null(answer);
    }

    [Fact]
    public async Task TheBeaconGoesQuietWhenTheHostStops()
    {
        await using var harness = await StartWithDiscoveryAsync();
        int discoveryPort = harness.Host.BoundPort;
        string probe = new DiscoveryProbe(harness.Environment.InstanceId).Format();
        Assert.NotNull(await ProbeAsync(discoveryPort, probe));

        await harness.Host.StopAsync(TestContext.Current.CancellationToken);

        Assert.False(harness.Host.IsDiscoveryListening);
        Assert.Null(await ProbeAsync(discoveryPort, probe, SilenceWindow));
    }

    [Fact]
    public async Task ADiscoveryPortAlreadyInUseCostsRediscoveryButNotTheHost()
    {
        using var occupier = new UdpClient(AddressFamily.InterNetwork);
        occupier.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        int takenPort = ((IPEndPoint)occupier.Client.LocalEndPoint!).Port;

        await using var beacon = new DiscoveryBeacon("instance-1", companionPort: takenPort);

        Assert.False(beacon.TryStart());
        Assert.False(beacon.IsListening);
    }

    [Fact]
    public async Task ADesktopWithNoInstanceIdDoesNotAnnounceItself()
    {
        await using var beacon = new DiscoveryBeacon("", companionPort: 0);

        Assert.False(beacon.TryStart());
    }
}
