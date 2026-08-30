using System;
using System.Threading.Tasks;
using BookDB.Contracts;
using BookDB.Mobile.Services;
using Xunit;

namespace BookDB.Mobile.Tests;

/// <summary>The coordinator's decision logic, isolated with a stub desktop: a bad code never dials, a
/// contract-version mismatch refuses without claiming, and only a successful claim persists the identity.
/// The real crypto/TLS round-trip against a live host is covered by the end-to-end test.</summary>
public class PairingCoordinatorTests
{
    private static PairingPayload Payload() => new()
    {
        Endpoint = "127.0.0.1:7443",
        ServerThumbprints = ["AA11"],
        ClientPfxBase64 = Convert.ToBase64String([1, 2, 3]),
        IssuedAtUtc = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task AnUnreadableCode_IsRejectedWithoutDialling()
    {
        var factory = new StubChannelFactory(new StubScannerService());
        var store = new FakeIdentityStore();
        var coordinator = new PairingCoordinator(factory, store);

        var outcome = await coordinator.PairAsync("not a pairing code", "Phone", TestContext.Current.CancellationToken);

        Assert.Equal(PairingOutcome.InvalidCode, outcome);
        Assert.Equal(0, factory.ConnectCount);
        Assert.False(store.HasIdentity);
    }

    [Fact]
    public async Task AContractVersionMismatch_RefusesWithoutClaimingOrPersisting()
    {
        var service = new StubScannerService { Info = new ServerInfo { ContractVersion = ContractVersion.Current + 1 } };
        var store = new FakeIdentityStore();
        var coordinator = new PairingCoordinator(new StubChannelFactory(service), store);

        var outcome = await coordinator.PairAsync(Payload().ToQrString(), "Phone", TestContext.Current.CancellationToken);

        Assert.Equal(PairingOutcome.IncompatibleVersion, outcome);
        Assert.False(store.HasIdentity);
    }

    [Fact]
    public async Task ASuccessfulClaim_PersistsTheIdentity()
    {
        var service = new StubScannerService { ClaimResult = new ClaimDeviceResult { Outcome = ClaimOutcome.Registered } };
        var store = new FakeIdentityStore();
        var coordinator = new PairingCoordinator(new StubChannelFactory(service), store);

        var outcome = await coordinator.PairAsync(Payload().ToQrString(), "Phone", TestContext.Current.CancellationToken);

        Assert.Equal(PairingOutcome.Paired, outcome);
        Assert.True(store.HasIdentity);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task AReClaimOfAnAlreadyRegisteredDevice_StillCountsAsPaired()
    {
        var service = new StubScannerService { ClaimResult = new ClaimDeviceResult { Outcome = ClaimOutcome.AlreadyRegistered } };
        var store = new FakeIdentityStore();
        var coordinator = new PairingCoordinator(new StubChannelFactory(service), store);

        Assert.Equal(PairingOutcome.Paired, await coordinator.PairAsync(Payload().ToQrString(), "Phone", TestContext.Current.CancellationToken));
        Assert.True(store.HasIdentity);
    }

    [Fact]
    public async Task TheDeviceLimit_IsReportedAndNothingIsPersisted()
    {
        var service = new StubScannerService { ClaimResult = new ClaimDeviceResult { Outcome = ClaimOutcome.DeviceLimitReached } };
        var store = new FakeIdentityStore();
        var coordinator = new PairingCoordinator(new StubChannelFactory(service), store);

        Assert.Equal(PairingOutcome.DeviceLimitReached, await coordinator.PairAsync(Payload().ToQrString(), "Phone", TestContext.Current.CancellationToken));
        Assert.False(store.HasIdentity);
    }
}
