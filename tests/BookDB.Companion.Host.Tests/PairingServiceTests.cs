using System;
using System.IO;
using BookDB.Companion.Host;
using Xunit;

namespace BookDB.Companion.Host.Tests;

/// <summary>
/// The pairing lifecycle end to end without a socket: an offer is single-use, expires on its own, and the
/// device cap and revocation are enforced where a claim can see them.
/// </summary>
public sealed class PairingServiceTests : IDisposable
{
    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"bookdb_pairing_{Guid.NewGuid():N}");

    private readonly FixedClock _clock = new() { Now = new DateTimeOffset(2026, 7, 23, 9, 0, 0, TimeSpan.Zero) };

    private readonly DeviceRegistry _registry;
    private readonly PairingService _pairing;

    public PairingServiceTests()
    {
        _registry = new DeviceRegistry(_directory, _clock);
        _pairing = new PairingService(_registry, _clock);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string PairDevice(string name)
    {
        var offer = _pairing.BeginPairing();
        Assert.Equal(PairingClaimOutcome.Registered, _pairing.Claim(offer.Thumbprint, name));
        return offer.Thumbprint;
    }

    [Fact]
    public void AnOfferCarriesAUsableDeviceCertificateAndItsOwnExpiry()
    {
        var offer = _pairing.BeginPairing();

        Assert.True(offer.DeviceCertificate.HasPrivateKey);
        Assert.Equal(CertificateFactory.Sha256Thumbprint(offer.DeviceCertificate), offer.Thumbprint);
        Assert.Equal(_clock.Now, offer.IssuedAtUtc);
        Assert.Equal(_clock.Now + PairingService.PairingTtl, offer.ExpiresAtUtc);
        Assert.False(offer.IsClaimed);
    }

    [Fact]
    public void AnUnclaimedOfferMayConnectSoItCanClaim()
    {
        var offer = _pairing.BeginPairing();

        Assert.True(_pairing.IsAcceptableForConnection(offer.Thumbprint));
    }

    [Fact]
    public void AnUnknownCertificateMayNotConnect()
    {
        Assert.False(_pairing.IsAcceptableForConnection("DEADBEEF"));
    }

    [Fact]
    public void ClaimingRegistersTheDeviceUnderTheGivenName()
    {
        var offer = _pairing.BeginPairing();

        Assert.Equal(PairingClaimOutcome.Registered, _pairing.Claim(offer.Thumbprint, "Ulf's phone"));

        Assert.True(offer.IsClaimed);
        Assert.Equal("Ulf's phone", _registry.Find(offer.Thumbprint)!.Name);
        Assert.Equal(_clock.Now, _registry.Find(offer.Thumbprint)!.PairedAtUtc);
        Assert.True(_pairing.IsAcceptableForConnection(offer.Thumbprint));
    }

    [Fact]
    public void ReClaimingByTheSameDeviceIsIdempotent()
    {
        var offer = _pairing.BeginPairing();
        _pairing.Claim(offer.Thumbprint, "Phone");

        Assert.Equal(PairingClaimOutcome.AlreadyRegistered, _pairing.Claim(offer.Thumbprint, "Phone"));
        Assert.Equal(1, _registry.Count);
    }

    [Fact]
    public void ASpentOfferCannotBeReusedByAnotherDevice()
    {
        var offer = _pairing.BeginPairing();
        _pairing.Claim(offer.Thumbprint, "Phone");
        _registry.Revoke(offer.Thumbprint);

        Assert.Equal(PairingClaimOutcome.AlreadyUsed, _pairing.Claim(offer.Thumbprint, "Impostor"));
        Assert.Equal(0, _registry.Count);
    }

    [Fact]
    public void AnExpiredOfferIsRefused()
    {
        var offer = _pairing.BeginPairing();

        _clock.Now += PairingService.PairingTtl;

        Assert.Equal(PairingClaimOutcome.UnknownOrExpired, _pairing.Claim(offer.Thumbprint, "Too late"));
        Assert.Equal(0, _registry.Count);
    }

    [Fact]
    public void AnExpiredOfferMayNoLongerConnect()
    {
        var offer = _pairing.BeginPairing();

        _clock.Now += PairingService.PairingTtl;

        Assert.False(_pairing.IsAcceptableForConnection(offer.Thumbprint));
    }

    [Fact]
    public void AnOfferStillWorksJustBeforeItExpires()
    {
        var offer = _pairing.BeginPairing();

        _clock.Now += PairingService.PairingTtl - TimeSpan.FromSeconds(1);

        Assert.Equal(PairingClaimOutcome.Registered, _pairing.Claim(offer.Thumbprint, "Just in time"));
    }

    [Fact]
    public void MintingAFreshOfferDoesNotInvalidateTheOneBeforeIt()
    {
        var first = _pairing.BeginPairing();
        var second = _pairing.BeginPairing();

        Assert.NotEqual(first.Thumbprint, second.Thumbprint);
        Assert.True(_pairing.IsAcceptableForConnection(first.Thumbprint));
        Assert.Equal(PairingClaimOutcome.Registered, _pairing.Claim(second.Thumbprint, "Phone"));
    }

    [Fact]
    public void ClaimingASixthDeviceIsRefusedAndCostsNoSlot()
    {
        for (int i = 0; i < DeviceRegistry.MaxDevices; i++)
        {
            PairDevice($"Phone {i}");
        }

        var offer = _pairing.BeginPairing();

        Assert.Equal(PairingClaimOutcome.DeviceLimitReached, _pairing.Claim(offer.Thumbprint, "One too many"));
        Assert.Equal(DeviceRegistry.MaxDevices, _registry.Count);
    }

    [Fact]
    public void RevokingADeviceFreesTheSlotForANewPairing()
    {
        string first = PairDevice("Phone 0");
        for (int i = 1; i < DeviceRegistry.MaxDevices; i++)
        {
            PairDevice($"Phone {i}");
        }

        _registry.Revoke(first);

        var offer = _pairing.BeginPairing();
        Assert.Equal(PairingClaimOutcome.Registered, _pairing.Claim(offer.Thumbprint, "Replacement"));
        Assert.Equal(DeviceRegistry.MaxDevices, _registry.Count);
    }

    [Fact]
    public void ARevokedDeviceMayNoLongerConnect()
    {
        string thumbprint = PairDevice("Phone");

        _registry.Revoke(thumbprint);

        Assert.False(_pairing.IsAcceptableForConnection(thumbprint));
    }

    [Fact]
    public void OffersDoNotSurviveARestartOfTheService()
    {
        var offer = _pairing.BeginPairing();

        var restarted = new PairingService(_registry, _clock);

        Assert.False(restarted.IsAcceptableForConnection(offer.Thumbprint));
        Assert.Equal(PairingClaimOutcome.UnknownOrExpired, restarted.Claim(offer.Thumbprint, "Phone"));
    }

    [Fact]
    public void APairedDeviceIsStillRecognisedAfterAHostRestart()
    {
        string thumbprint = PairDevice("Phone");

        var restarted = new PairingService(new DeviceRegistry(_directory, _clock), _clock);

        Assert.True(restarted.IsAcceptableForConnection(thumbprint));
        Assert.Equal(PairingClaimOutcome.AlreadyRegistered, restarted.Claim(thumbprint, "Phone"));
    }
}
