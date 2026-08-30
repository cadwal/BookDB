using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using BookDB.Companion.Host;
using BookDB.Contracts;
using BookDB.Mobile.Services;
using Xunit;

namespace BookDB.Companion.Host.Tests;

/// <summary>
/// The phone's half of rediscovery, against the real beacon over loopback: it finds the computer it is
/// paired with, ignores anyone else's, and reads silence off a computer that is no longer there. The last
/// test drives the whole reconnect path — the address on file has gone stale, and the device gets itself
/// back on its own.
/// </summary>
public sealed class DesktopLocatorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"bookdb_locate_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Probes loopback rather than the broadcast addresses, so the test never leaves the machine.</summary>
    private static UdpDesktopLocator Locator(TimeSpan? timeout = null) =>
        new([IPAddress.Loopback], timeout ?? TimeSpan.FromSeconds(3));

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

    [Fact]
    public async Task ThePairedComputer_AnswersWithTheAddressToDial()
    {
        await using var harness = await StartWithDiscoveryAsync();

        var reply = await Locator().LocateAsync(
            harness.Environment.InstanceId,
            harness.Host.BoundPort,
            TestContext.Current.CancellationToken);

        Assert.NotNull(reply);
        Assert.Equal(harness.Environment.InstanceId, reply!.InstanceId);
        Assert.Equal("127.0.0.1", reply.Host);
        Assert.Equal(harness.Host.BoundPort, reply.Port);
    }

    [Fact]
    public async Task AnotherComputersBeacon_IsNotMistakenForOurs()
    {
        await using var harness = await StartWithDiscoveryAsync();

        var reply = await Locator(TimeSpan.FromMilliseconds(750)).LocateAsync(
            "someone-elses-instance",
            harness.Host.BoundPort,
            TestContext.Current.CancellationToken);

        Assert.Null(reply);
    }

    /// <summary>Windows reports a closed port with an ICMP error that surfaces as a socket reset on the next
    /// receive; to the phone that has to read exactly like silence.</summary>
    [Fact]
    public async Task AComputerThatIsNoLongerListening_ReadsAsNoAnswer()
    {
        await using var harness = await StartWithDiscoveryAsync();
        int discoveryPort = harness.Host.BoundPort;
        Assert.NotNull(await Locator().LocateAsync(
            harness.Environment.InstanceId, discoveryPort, TestContext.Current.CancellationToken));

        await harness.Host.StopAsync(TestContext.Current.CancellationToken);

        var reply = await Locator(TimeSpan.FromMilliseconds(750)).LocateAsync(
            harness.Environment.InstanceId, discoveryPort, TestContext.Current.CancellationToken);

        Assert.Null(reply);
    }

    [Fact]
    public async Task AComputerThatHasMoved_IsFoundAgainAndTheDeviceReconnectsToTheNewAddress()
    {
        await using var harness = await StartWithDiscoveryAsync();
        var store = new FileIdentityStore(_dir);
        var channelFactory = new CompanionChannelFactory();
        var coordinator = new PairingCoordinator(channelFactory, store);

        Assert.Equal(
            PairingOutcome.Paired,
            await coordinator.PairAsync(
                harness.CreatePayload().ToQrString(), "Kitchen tablet", TestContext.Current.CancellationToken));

        // The computer is now answering somewhere else: the address on file no longer has anything on it,
        // while the port — a setting, not something the network hands out — still holds.
        var paired = store.Load()!;
        store.Save(paired with { Endpoint = $"127.0.0.2:{harness.Host.BoundPort}" });

        using var connection = new ConnectionService(store, channelFactory, Locator());
        var live = await connection.EnsureConnectedAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(live);
        Assert.Equal(ConnectionStatus.Connected, connection.Status);
        Assert.Equal(harness.Environment.LibraryName, connection.ServerInfo?.LibraryName);
        Assert.Equal($"127.0.0.1:{harness.Host.BoundPort}", store.Load()!.Endpoint);
    }
}
