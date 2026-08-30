using System.Collections.Generic;
using System.Threading.Tasks;
using BookDB.Contracts;
using BookDB.Mobile.Services;
using Xunit;

namespace BookDB.Mobile.Tests;

/// <summary>The reconnect state machine with a fake transport: dial what is on file, fall back to asking the
/// network where the computer went, and be honest about being offline when neither works.</summary>
public class ConnectionServiceTests
{
    private const string Known = "192.168.1.9:7443";
    private const string Moved = "192.168.1.42:7443";

    private static DeviceIdentity Identity(string endpoint = Known, string instanceId = "instance-1") =>
        new(endpoint, ["AA"], [1, 2, 3], "Kitchen tablet", instanceId);

    /// <summary>A transport where only the named endpoints answer; everything else dials but never replies.</summary>
    private static StubChannelFactory FactoryReaching(StubScannerService service, params string[] endpoints)
    {
        var reachable = new HashSet<string>(endpoints);
        var silent = new StubScannerService { Reachable = false };
        return new StubChannelFactory(identity => reachable.Contains(identity.Endpoint) ? service : silent);
    }

    [Fact]
    public async Task AnUnpairedDevice_StaysOfflineAndDialsNothing()
    {
        var factory = new StubChannelFactory(new StubScannerService());
        using var connection = new ConnectionService(new FakeIdentityStore(), factory, new FakeDesktopLocator());

        Assert.Null(await connection.EnsureConnectedAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ConnectionStatus.Offline, connection.Status);
        Assert.Equal(0, factory.ConnectCount);
    }

    [Fact]
    public async Task TheAddressOnFile_IsDialledAndTheHandshakeIsKept()
    {
        var service = new StubScannerService
        {
            Info = new ServerInfo
            {
                ContractVersion = ContractVersion.Current,
                LibraryName = "Böckerna",
                AppVersion = "4.0.0",
                Capture = new CaptureSettings { MaxLongEdgePx = 1600, JpegQuality = 80 },
            },
        };
        using var connection = new ConnectionService(
            new FakeIdentityStore(Identity()), FactoryReaching(service, Known), new FakeDesktopLocator());

        Assert.NotNull(await connection.EnsureConnectedAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ConnectionStatus.Connected, connection.Status);
        Assert.Equal("Böckerna", connection.ServerInfo?.LibraryName);
        Assert.Equal(1600, connection.ServerInfo?.Capture.MaxLongEdgePx);
    }

    [Fact]
    public async Task AnOpenConnection_IsReusedRatherThanRedialled()
    {
        var factory = FactoryReaching(new StubScannerService(), Known);
        using var connection = new ConnectionService(
            new FakeIdentityStore(Identity()), factory, new FakeDesktopLocator());

        var first = await connection.EnsureConnectedAsync(TestContext.Current.CancellationToken);
        var second = await connection.EnsureConnectedAsync(TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Equal(1, factory.ConnectCount);
    }

    [Fact]
    public async Task AComputerThatHasMoved_IsFoundByItsInstanceIdAndItsNewAddressIsRemembered()
    {
        var factory = FactoryReaching(new StubScannerService(), Moved);
        var locator = new FakeDesktopLocator { Reply = new DiscoveryReply("instance-1", "192.168.1.42", 7443) };
        var store = new FakeIdentityStore(Identity());
        using var connection = new ConnectionService(store, factory, locator);

        Assert.NotNull(await connection.EnsureConnectedAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ConnectionStatus.Connected, connection.Status);
        Assert.Equal(new[] { Known, Moved }, factory.Dialled);
        // Discovery is probed on the companion port itself, over UDP, and the port survives the address change.
        Assert.Equal(("instance-1", 7443), locator.Asked[0]);
        Assert.Equal(Moved, store.Load()?.Endpoint);
        Assert.Equal("Kitchen tablet", store.Load()?.DeviceName);
    }

    [Fact]
    public async Task AComputerThatNothingAnswersFor_LeavesTheDeviceOffline()
    {
        var factory = FactoryReaching(new StubScannerService());
        var store = new FakeIdentityStore(Identity());
        using var connection = new ConnectionService(store, factory, new FakeDesktopLocator());
        var seen = new List<ConnectionStatus>();
        connection.StatusChanged += (_, _) => seen.Add(connection.Status);

        Assert.Null(await connection.EnsureConnectedAsync(TestContext.Current.CancellationToken));

        Assert.Equal(new[] { ConnectionStatus.Reconnecting, ConnectionStatus.Offline }, seen);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, factory.OpenConnections);
    }

    [Fact]
    public async Task ADeviceThatNeverLearnedTheInstanceId_DoesNotProbeTheNetwork()
    {
        var locator = new FakeDesktopLocator();
        using var connection = new ConnectionService(
            new FakeIdentityStore(Identity(instanceId: "")),
            FactoryReaching(new StubScannerService()),
            locator);

        await connection.EnsureConnectedAsync(TestContext.Current.CancellationToken);

        Assert.Empty(locator.Asked);
        Assert.Equal(ConnectionStatus.Offline, connection.Status);
    }

    [Fact]
    public async Task AnAnswerFromTheAddressAlreadyOnFile_IsNotRedialled()
    {
        var factory = FactoryReaching(new StubScannerService());
        var locator = new FakeDesktopLocator { Reply = new DiscoveryReply("instance-1", "192.168.1.9", 7443) };
        using var connection = new ConnectionService(new FakeIdentityStore(Identity()), factory, locator);

        Assert.Null(await connection.EnsureConnectedAsync(TestContext.Current.CancellationToken));

        Assert.Equal(new[] { Known }, factory.Dialled);
    }

    [Fact]
    public async Task ARefreshAfterTheComputerGoesAway_FallsToOffline()
    {
        var service = new StubScannerService();
        using var connection = new ConnectionService(
            new FakeIdentityStore(Identity()), FactoryReaching(service, Known), new FakeDesktopLocator());
        await connection.EnsureConnectedAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ConnectionStatus.Connected, connection.Status);

        service.Reachable = false;
        await connection.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionStatus.Offline, connection.Status);
    }

    [Fact]
    public async Task ARefreshWhileTheComputerIsStillThere_KeepsTheSameConnection()
    {
        var factory = FactoryReaching(new StubScannerService(), Known);
        using var connection = new ConnectionService(
            new FakeIdentityStore(Identity()), factory, new FakeDesktopLocator());
        var first = await connection.EnsureConnectedAsync(TestContext.Current.CancellationToken);

        await connection.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Same(first, await connection.EnsureConnectedAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, factory.ConnectCount);
        Assert.Equal(ConnectionStatus.Connected, connection.Status);
    }

    [Fact]
    public async Task AComputerSpeakingAnotherContractVersion_IsNotUsed()
    {
        var service = new StubScannerService
        {
            Info = new ServerInfo { ContractVersion = ContractVersion.Current + 1, LibraryName = "Newer" },
        };
        var factory = FactoryReaching(service, Known);
        using var connection = new ConnectionService(
            new FakeIdentityStore(Identity()), factory, new FakeDesktopLocator());

        Assert.Null(await connection.EnsureConnectedAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ConnectionStatus.Offline, connection.Status);
        Assert.Null(connection.ServerInfo);
        Assert.Equal(0, factory.OpenConnections);
    }

    /// <summary>
    /// Being removed on the computer is not an outage: the computer answered. Reporting it as Offline is what
    /// left the phone offering "try again" for something no amount of trying can fix.
    /// </summary>
    [Fact]
    public async Task AComputerThatHasRemovedThisDevice_SaysSoRatherThanOffline()
    {
        var service = new StubScannerService { Revoked = true };
        var locator = new FakeDesktopLocator { Reply = new DiscoveryReply("instance-1", "192.168.1.42", 7443) };
        using var connection = new ConnectionService(
            new FakeIdentityStore(Identity()), FactoryReaching(service, Known), locator);

        Assert.Null(await connection.EnsureConnectedAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ConnectionStatus.Revoked, connection.Status);
        // No hunting for a computer that has already answered; a refusal is not a computer that has moved.
        Assert.Empty(locator.Asked);
        Assert.Null(connection.ServerInfo);
    }

    [Fact]
    public async Task ADeviceRemovedWhileConnected_TurnsToRevokedOnTheNextCheck()
    {
        var service = new StubScannerService();
        using var connection = new ConnectionService(
            new FakeIdentityStore(Identity()), FactoryReaching(service, Known), new FakeDesktopLocator());
        await connection.EnsureConnectedAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ConnectionStatus.Connected, connection.Status);

        service.Revoked = true;
        await connection.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionStatus.Revoked, connection.Status);
        Assert.Null(await connection.EnsureConnectedAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Found in UAT: the chip flickered back to grey after a revocation. A computer that cannot be reached
    /// has not changed its mind about this device, so an outage on top of a revocation must not read as an
    /// ordinary outage — the way back is re-pairing either way, and only the red chip says so.
    /// </summary>
    [Fact]
    public async Task ARevokedDeviceThatThenLosesTheNetwork_StaysRevokedRatherThanGoingGrey()
    {
        var service = new StubScannerService { Revoked = true };
        using var connection = new ConnectionService(
            new FakeIdentityStore(Identity()), FactoryReaching(service, Known), new FakeDesktopLocator());

        await connection.EnsureConnectedAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ConnectionStatus.Revoked, connection.Status);

        service.Reachable = false;
        await connection.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionStatus.Revoked, connection.Status);
    }

    /// <summary>The counterpart: a computer that takes the device back is believed at once, so the state is
    /// sticky and not a dead end.</summary>
    [Fact]
    public async Task ARevokedDeviceTheComputerTakesBack_GoesGreenAgain()
    {
        var service = new StubScannerService { Revoked = true };
        using var connection = new ConnectionService(
            new FakeIdentityStore(Identity()), FactoryReaching(service, Known), new FakeDesktopLocator());

        await connection.EnsureConnectedAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ConnectionStatus.Revoked, connection.Status);

        service.Revoked = false;
        await connection.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionStatus.Connected, connection.Status);
    }

    /// <summary>Unpairing is the other way out, and the one the settings screen offers.</summary>
    [Fact]
    public async Task Reset_ClearsARevocation()
    {
        using var connection = new ConnectionService(
            new FakeIdentityStore(Identity()),
            FactoryReaching(new StubScannerService { Revoked = true }, Known),
            new FakeDesktopLocator());

        await connection.EnsureConnectedAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ConnectionStatus.Revoked, connection.Status);

        connection.Reset();

        Assert.Equal(ConnectionStatus.Offline, connection.Status);
    }

    [Fact]
    public async Task Reset_ClosesTheConnectionAndForgetsTheHandshake()
    {
        var factory = FactoryReaching(new StubScannerService(), Known);
        using var connection = new ConnectionService(
            new FakeIdentityStore(Identity()), factory, new FakeDesktopLocator());
        await connection.EnsureConnectedAsync(TestContext.Current.CancellationToken);

        connection.Reset();

        Assert.Equal(ConnectionStatus.Offline, connection.Status);
        Assert.Null(connection.ServerInfo);
        Assert.Equal(0, factory.OpenConnections);
    }
}
