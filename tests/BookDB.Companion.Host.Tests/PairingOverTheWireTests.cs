using System;
using System.Threading.Tasks;
using BookDB.Companion.Host;
using BookDB.Contracts;
using Grpc.Core;
using Xunit;

namespace BookDB.Companion.Host.Tests;

/// <summary>
/// The whole pairing journey as a phone experiences it: scan a payload, dial the endpoint it names, claim a
/// name, and be a working device from then on.
/// </summary>
public sealed class PairingOverTheWireTests
{
    [Fact]
    public async Task AScannedPayloadPointsAtThisHostAndCarriesAUsableIdentity()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();

        var payload = PairingPayload.FromQrString(harness.CreatePayload().ToQrString());

        Assert.Equal($"127.0.0.1:{harness.Host.BoundPort}", payload.Endpoint);
        Assert.Equal([harness.Host.ServerThumbprint], payload.ServerThumbprints);
        Assert.Equal(harness.Clock.Now, payload.IssuedAtUtc);
        using var identity = CertificateFactory.LoadPfx(Convert.FromBase64String(payload.ClientPfxBase64));
        Assert.True(identity.HasPrivateKey);
    }

    [Fact]
    public async Task ScanningAPayloadAndClaimingANameProducesAWorkingDevice()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();
        var client = harness.ConnectWith(PairingPayload.FromQrString(harness.CreatePayload().ToQrString()));

        var claim = await client.ClaimDeviceAsync(new ClaimDeviceRequest { DeviceName = "Ulf's phone" });

        Assert.Equal(ClaimOutcome.Registered, claim.Outcome);
        Assert.Equal(1, claim.RegisteredDeviceCount);
        Assert.Equal(DeviceRegistry.MaxDevices, claim.MaxDevices);
        Assert.Equal("Ulf's phone", Assert.Single(harness.Registry.List()).Name);

        // And the same connection keeps working as an ordinary paired device.
        Assert.Equal(ContractVersion.Current, (await client.GetServerInfoAsync()).ContractVersion);
    }

    [Fact]
    public async Task TheDeviceStaysPairedAfterItsOfferHasExpired()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();
        var client = harness.ConnectWith(harness.CreatePayload());
        await client.ClaimDeviceAsync(new ClaimDeviceRequest { DeviceName = "Phone" });

        harness.Clock.Now += PairingService.PairingTtl * 2;

        Assert.Equal(ContractVersion.Current, (await client.GetServerInfoAsync()).ContractVersion);
    }

    [Fact]
    public async Task ClaimingTwiceDoesNotTakeASecondSlot()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();
        var client = harness.ConnectWith(harness.CreatePayload());
        await client.ClaimDeviceAsync(new ClaimDeviceRequest { DeviceName = "Phone" });

        var again = await client.ClaimDeviceAsync(new ClaimDeviceRequest { DeviceName = "Phone" });

        Assert.Equal(ClaimOutcome.AlreadyRegistered, again.Outcome);
        Assert.Equal(1, again.RegisteredDeviceCount);
    }

    [Fact]
    public async Task AnExpiredPayloadCannotEvenReachTheClaim()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();
        var payload = harness.CreatePayload();

        harness.Clock.Now += PairingService.PairingTtl;

        var error = await Assert.ThrowsAsync<RpcException>(async () =>
            await harness.ConnectWith(payload).ClaimDeviceAsync(new ClaimDeviceRequest { DeviceName = "Late" }));

        // The identity is refused at the door, so an expired code is one refusal, not a claim outcome.
        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
        Assert.Equal(0, harness.Registry.Count);
    }

    [Fact]
    public async Task ARevokedDeviceCannotClaimItsOldPayloadAgain()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();
        var payload = harness.CreatePayload();
        await harness.ConnectWith(payload).ClaimDeviceAsync(new ClaimDeviceRequest { DeviceName = "Phone" });
        harness.Registry.Revoke(harness.Registry.List()[0].Thumbprint);

        var error = await Assert.ThrowsAsync<RpcException>(async () =>
            await harness.ConnectWith(payload).ClaimDeviceAsync(new ClaimDeviceRequest { DeviceName = "Back again" }));

        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
        Assert.Equal(0, harness.Registry.Count);
    }

    [Fact]
    public async Task ASixthDeviceIsToldWhyItWasRefused()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();
        for (int i = 0; i < DeviceRegistry.MaxDevices; i++)
        {
            await harness.ConnectWith(harness.CreatePayload())
                .ClaimDeviceAsync(new ClaimDeviceRequest { DeviceName = $"Phone {i}" });
        }

        var refused = await harness.ConnectWith(harness.CreatePayload())
            .ClaimDeviceAsync(new ClaimDeviceRequest { DeviceName = "One too many" });

        Assert.Equal(ClaimOutcome.DeviceLimitReached, refused.Outcome);
        Assert.Equal(DeviceRegistry.MaxDevices, refused.RegisteredDeviceCount);
        Assert.Equal(DeviceRegistry.MaxDevices, refused.MaxDevices);
    }

    [Fact]
    public async Task RevokingADeviceLetsAReplacementPairInItsPlace()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();
        for (int i = 0; i < DeviceRegistry.MaxDevices; i++)
        {
            await harness.ConnectWith(harness.CreatePayload())
                .ClaimDeviceAsync(new ClaimDeviceRequest { DeviceName = $"Phone {i}" });
        }

        harness.Registry.Revoke(harness.Registry.List()[0].Thumbprint);

        var claim = await harness.ConnectWith(harness.CreatePayload())
            .ClaimDeviceAsync(new ClaimDeviceRequest { DeviceName = "Replacement" });

        Assert.Equal(ClaimOutcome.Registered, claim.Outcome);
        Assert.Equal(DeviceRegistry.MaxDevices, claim.RegisteredDeviceCount);
    }

    [Fact]
    public async Task ANamelessClaimIsRejected()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();
        var client = harness.ConnectWith(harness.CreatePayload());

        var error = await Assert.ThrowsAsync<RpcException>(
            async () => await client.ClaimDeviceAsync(new ClaimDeviceRequest { DeviceName = "   " }));

        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        Assert.Equal(0, harness.Registry.Count);
    }

    [Fact]
    public async Task AnAbsurdlyLongNameIsCutDownRatherThanStored()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();
        var client = harness.ConnectWith(harness.CreatePayload());

        await client.ClaimDeviceAsync(new ClaimDeviceRequest { DeviceName = new string('x', 500) });

        Assert.Equal(64, Assert.Single(harness.Registry.List()).Name.Length);
    }

    [Fact]
    public async Task APayloadCannotBeMintedBeforeTheHostIsListening()
    {
        await using var harness = new CompanionHostHarness();

        Assert.Throws<InvalidOperationException>(() => harness.CreatePayload());
    }
}
