using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Companion.Host;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using BookDB.Models;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

/// <summary>
/// The pairing code's life on screen: it counts down, it replaces itself when it lapses, and it notices
/// the moment a device claims it. Driven by ticking the view model directly, so no timer or wall clock is
/// involved.
/// </summary>
public sealed class PairingDialogViewModelTests : IDisposable
{
    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 7, 23, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"bookdb_pairvm_{Guid.NewGuid():N}");
    private readonly FixedClock _clock = new();
    private readonly CompanionHostManager _manager;
    private readonly PairingService _pairing;

    public PairingDialogViewModelTests()
    {
        Directory.CreateDirectory(_dir);
        string configPath = Path.Combine(_dir, "config.json");
        var registry = new DeviceRegistry(CompanionPaths.CompanionDirectory(configPath), _clock);
        _pairing = new PairingService(registry, _clock);
        var library = new CompanionLibrary(new StubPreview(), new StubBrowse(), new StubIntake(), new StubQueueStatus());

        _manager = new CompanionHostManager(
            new BootstrapConfigService(configPath),
            registry,
            _pairing,
            library,
            new AppSettings { ConfigPath = configPath },
            _clock);
    }

    public void Dispose()
    {
        _manager.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    private async Task<PairingDialogViewModel> StartedAsync()
    {
        await _manager.ApplyAsync(
            new CompanionOptions { Enabled = true, Port = 0 }, TestContext.Current.CancellationToken);

        var vm = new PairingDialogViewModel(_manager, _clock);
        vm.Start();
        return vm;
    }

    [Fact]
    public async Task AStartedDialogShowsACodeAndCountsDown()
    {
        using var vm = await StartedAsync();

        Assert.NotEmpty(vm.QrPng!);
        Assert.Null(vm.ErrorText);
        Assert.False(vm.IsPaired);
        Assert.NotEmpty(vm.ExpiryText);
        Assert.NotEmpty(vm.Addresses);
        Assert.Equal(vm.Addresses[0], vm.SelectedAddress);
    }

    [Fact]
    public async Task TheCountdownFollowsTheClock()
    {
        using var vm = await StartedAsync();
        string atStart = vm.ExpiryText;

        _clock.Now = _clock.Now.AddSeconds(30);
        vm.Tick();

        Assert.NotEqual(atStart, vm.ExpiryText);
    }

    [Fact]
    public async Task ALapsedCodeIsReplacedRatherThanLeftDead()
    {
        using var vm = await StartedAsync();

        _clock.Now = _clock.Now.Add(PairingService.PairingTtl);
        vm.Tick();

        // A fresh code, still counting down, and no error shown to a user who simply looked away.
        Assert.False(vm.IsPaired);
        Assert.Null(vm.ErrorText);
        Assert.NotEmpty(vm.QrPng!);
        Assert.NotEmpty(vm.ExpiryText);
    }

    [Fact]
    public async Task ClaimingTheCodeSwitchesTheDialogToItsConfirmation()
    {
        using var vm = await StartedAsync();
        ClaimTheOfferAs(vm, "Kitchen tablet");

        vm.Tick();

        Assert.True(vm.IsPaired);
        Assert.Equal("Kitchen tablet", vm.PairedDeviceName);
        Assert.Contains("1", vm.DeviceCountText);
    }

    [Fact]
    public async Task PairingAnotherDeviceGoesBackToAFreshCode()
    {
        using var vm = await StartedAsync();
        ClaimTheOfferAs(vm, "Kitchen tablet");
        vm.Tick();

        vm.PairAnotherCommand.Execute(null);

        Assert.False(vm.IsPaired);
        Assert.Null(vm.PairedDeviceName);
        Assert.NotEmpty(vm.QrPng!);
    }

    [Fact]
    public async Task ChoosingADifferentAddressMintsANewCode()
    {
        using var vm = await StartedAsync();
        int before = _manager.Registry.Count;
        string replaced = vm.CodeThumbprint!;

        // The second address is put there by the test rather than taken from the machine: selecting
        // `Addresses[^1]` re-selects what is already selected on a one-adapter host, mints nothing, and the
        // test then passes or fails on how many network cards the machine happens to have.
        vm.Addresses.Add("10.99.99.99");
        vm.SelectedAddress = vm.Addresses[^1];

        Assert.NotEmpty(vm.QrPng!);
        Assert.Null(vm.ErrorText);
        Assert.Equal(before, _manager.Registry.Count); // minting a code pairs nothing by itself
        // The code that left the screen left circulation with it; only what is shown can be claimed.
        Assert.NotEqual(replaced, vm.CodeThumbprint);
        Assert.False(_pairing.IsAcceptableForConnection(replaced));
        Assert.True(_pairing.IsAcceptableForConnection(vm.CodeThumbprint!));
    }

    [Fact]
    public void WithTheCompanionStoppedTheDialogExplainsItselfInsteadOfShowingACode()
    {
        using var vm = new PairingDialogViewModel(_manager, _clock);

        vm.Start();

        Assert.True(vm.HasError);
        Assert.Equal(Resources.Pairing_NotRunning, vm.ErrorText);
        Assert.Null(vm.QrPng);
    }

    /// <summary>
    /// The refusal has to come before the code, not after it: a phone that scans a code minted at the limit
    /// gets all the way to naming itself and tapping Pair before the desktop admits it has no room.
    /// </summary>
    [Fact]
    public async Task WithEverySlotTakenTheDialogRefusesInsteadOfShowingACode()
    {
        FillEverySlot();
        await _manager.ApplyAsync(
            new CompanionOptions { Enabled = true, Port = 0 }, TestContext.Current.CancellationToken);
        using var vm = new PairingDialogViewModel(_manager, _clock);

        vm.Start();

        Assert.True(vm.HasError);
        Assert.Equal(
            string.Format(Resources.Devices_AtLimit, DeviceRegistry.MaxDevices), vm.ErrorText);
        Assert.Null(vm.QrPng);
        Assert.Null(vm.CodeThumbprint);
    }

    /// <summary>The device that just paired may have been the last free slot.</summary>
    [Fact]
    public async Task PairingAnotherWithNoSlotLeftRefusesRatherThanMintingACode()
    {
        for (var i = 1; i < DeviceRegistry.MaxDevices; i++)
        {
            Register($"Device {i}", $"FILL{i}");
        }

        using var vm = await StartedAsync();
        ClaimTheOfferAs(vm, "Last one in");
        vm.Tick();
        string? claimed = vm.CodeThumbprint;

        vm.PairAnotherCommand.Execute(null);

        Assert.True(vm.HasError);
        Assert.Equal(claimed, vm.CodeThumbprint); // no new offer was minted
    }

    /// <summary>
    /// The code is a secret on a screen, so closing the screen has to take it out of circulation — otherwise
    /// a phone that scanned it a moment too late still pairs, into a list the user has already stopped
    /// looking at.
    /// </summary>
    [Fact]
    public async Task ClosingTheDialogTakesAnUnclaimedCodeOutOfCirculation()
    {
        var vm = await StartedAsync();
        string thumbprint = vm.CodeThumbprint!;
        Assert.True(_pairing.IsAcceptableForConnection(thumbprint));

        vm.Dispose();

        Assert.False(_pairing.IsAcceptableForConnection(thumbprint));
        Assert.Equal(PairingClaimOutcome.UnknownOrExpired, _pairing.Claim(thumbprint, "Too late"));
    }

    /// <summary>Withdrawal is for a code nobody used; a device that did pair stays paired.</summary>
    [Fact]
    public async Task ClosingTheDialogLeavesADeviceThatAlreadyPairedAlone()
    {
        var vm = await StartedAsync();
        ClaimTheOfferAs(vm, "Kitchen tablet");
        vm.Tick();
        string thumbprint = vm.CodeThumbprint!;

        vm.Dispose();

        Assert.True(_manager.Registry.IsRegistered(thumbprint));
        Assert.True(_pairing.IsAcceptableForConnection(thumbprint));
    }

    private void FillEverySlot()
    {
        for (var i = 1; i <= DeviceRegistry.MaxDevices; i++)
        {
            Register($"Device {i}", $"FILL{i}");
        }
    }

    private void Register(string name, string thumbprint)
        => Assert.True(_manager.Registry.TryRegister(new PairedDevice
        {
            Name = name,
            Thumbprint = thumbprint,
            PairedAtUtc = _clock.Now,
            LastUsedUtc = _clock.Now,
        }));

    /// <summary>
    /// Registers the code currently on screen, which is exactly the state a real claim leaves behind and
    /// what the dialog watches the registry for.
    /// </summary>
    private void ClaimTheOfferAs(PairingDialogViewModel vm, string deviceName)
        => _manager.Registry.TryRegister(new PairedDevice
        {
            Name = deviceName,
            Thumbprint = vm.CodeThumbprint!,
            PairedAtUtc = _clock.Now,
            LastUsedUtc = _clock.Now,
        });

    private sealed class StubPreview : ICompanionPreviewService
    {
        public Task<IsbnPreviewResult> PreviewIsbnAsync(string isbn, CancellationToken ct = default)
            => Task.FromResult(new IsbnPreviewResult(false, null, null, null, IsbnPreviewOrigin.None));
    }

    private sealed class StubBrowse : ICompanionBrowseService
    {
        public Task<BrowsePage> BrowseAsync(string? search, int? collectionId, string? isbn, int skip, int take, CancellationToken ct = default)
            => Task.FromResult(new BrowsePage([], 0));

        public Task<IReadOnlyList<BrowseCollection>> GetCollectionsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BrowseCollection>>([]);

        public Task<BrowseDetail?> GetDetailAsync(int bookId, CancellationToken ct = default)
            => Task.FromResult<BrowseDetail?>(null);

        public Task<byte[]?> GetImageAsync(int bookId, int imageTypeId, CancellationToken ct = default)
            => Task.FromResult<byte[]?>(null);
    }

    private sealed class StubIntake : ICompanionIntakeService
    {
        public Task<CompanionIntakeResult> IntakeAsync(CompanionScanItem item, CancellationToken ct = default)
            => Task.FromResult(new CompanionIntakeResult(item.ClientItemId, CompanionIntakeOutcome.Failed, null, null, CompanionIntakeFailure.None));
    }

    private sealed class StubQueueStatus : ICompanionQueueStatusService
    {
        public Task<IReadOnlyList<CompanionQueueItemStatus>> GetStatusesAsync(IReadOnlyList<int> batchQueueItemIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CompanionQueueItemStatus>>([]);
    }
}
