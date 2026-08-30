using System;
using System.IO;
using System.Threading.Tasks;
using BookDB.Companion.Host;
using BookDB.Contracts;
using BookDB.Mobile.Services;
using Grpc.Core;
using ProtoBuf.Grpc;
using Xunit;

namespace BookDB.Companion.Host.Tests;

/// <summary>End to end, both ends real: the phone-side services (pairing coordinator, pinning channel
/// factory, file-backed identity store) drive a live mutual-TLS host over loopback. Proves a fresh device
/// pairs, its identity survives a "restart", and the refusal cases map to the outcomes the UI shows.</summary>
public sealed class MobilePairingFlowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"bookdb_phone_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static CallContext Ct() => new(new CallOptions(cancellationToken: TestContext.Current.CancellationToken));

    [Fact]
    public async Task AFreshDevicePairs_PersistsItsIdentity_AndReconnectsAfterRestart()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();

        var store = new FileIdentityStore(_dir);
        var coordinator = new PairingCoordinator(new CompanionChannelFactory(), store);
        var payload = harness.CreatePayload();

        var outcome = await coordinator.PairAsync(payload.ToQrString(), "Ulf's phone", TestContext.Current.CancellationToken);

        Assert.Equal(PairingOutcome.Paired, outcome);
        Assert.True(store.HasIdentity);
        Assert.Equal(1, harness.Registry.Count);

        // Restart: a brand-new store loads the saved identity and, now that the device is registered, the
        // channel is accepted with no live pairing offer in play.
        var reloaded = new FileIdentityStore(_dir).Load();
        Assert.NotNull(reloaded);
        using var connection = new CompanionChannelFactory().Connect(reloaded!);
        var info = await connection.Service.GetServerInfoAsync(Ct());
        Assert.Equal(harness.Environment.LibraryName, info.LibraryName);
    }

    [Fact]
    public async Task AnExpiredCode_IsRefusedAndNothingIsPersisted()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();

        var store = new FileIdentityStore(_dir);
        var coordinator = new PairingCoordinator(new CompanionChannelFactory(), store);
        var payload = harness.CreatePayload();

        // Let the offer lapse before the phone gets round to claiming it.
        harness.Clock.Now += TimeSpan.FromMinutes(3);

        var outcome = await coordinator.PairAsync(payload.ToQrString(), "Ulf's phone", TestContext.Current.CancellationToken);

        Assert.Equal(PairingOutcome.CodeExpired, outcome);
        Assert.False(store.HasIdentity);
        Assert.Equal(0, harness.Registry.Count);
    }

    [Fact]
    public async Task AClaimAtTheDeviceLimit_ReportsTheLimitAndPersistsNothing()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();

        for (var i = 0; i < DeviceRegistry.MaxDevices; i++)
        {
            harness.Registry.TryRegister(new PairedDevice
            {
                Name = $"device {i}",
                Thumbprint = $"THUMB{i}",
                PairedAtUtc = harness.Clock.Now,
                LastUsedUtc = harness.Clock.Now,
            });
        }

        var store = new FileIdentityStore(_dir);
        var coordinator = new PairingCoordinator(new CompanionChannelFactory(), store);

        var outcome = await coordinator.PairAsync(harness.CreatePayload().ToQrString(), "One too many", TestContext.Current.CancellationToken);

        Assert.Equal(PairingOutcome.DeviceLimitReached, outcome);
        Assert.False(store.HasIdentity);
    }
}
