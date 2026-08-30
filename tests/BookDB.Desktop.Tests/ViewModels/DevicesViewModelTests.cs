using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Companion.Host;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using BookDB.Models;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

/// <summary>
/// The Devices section of Maintenance: what it lists, what it says when there is nothing or the companion
/// is off, and that removing a device is confirmed before the pairing is thrown away.
/// </summary>
public sealed class DevicesViewModelTests : IDisposable
{
    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 7, 23, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"bookdb_devvm_{Guid.NewGuid():N}");
    private readonly FixedClock _clock = new();
    private readonly string _configPath;
    private readonly BootstrapConfigService _bootstrap;
    private readonly DeviceRegistry _registry;
    private readonly IWindowService _windows = Substitute.For<IWindowService>();

    public DevicesViewModelTests()
    {
        Directory.CreateDirectory(_dir);
        _configPath = Path.Combine(_dir, "config.json");
        _bootstrap = new BootstrapConfigService(_configPath);
        _registry = new DeviceRegistry(CompanionPaths.CompanionDirectory(_configPath), _clock);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    private DevicesViewModel NewViewModel()
    {
        var library = new CompanionLibrary(new StubPreview(), new StubBrowse(), new StubIntake(), new StubQueueStatus());
        var manager = new CompanionHostManager(
            _bootstrap,
            _registry,
            new PairingService(_registry, _clock),
            library,
            new AppSettings { ConfigPath = _configPath },
            _clock);

        return new DevicesViewModel(manager, _windows);
    }

    private void Confirm(bool answer) => _windows
        .ShowDeleteConfirmationAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
        .Returns(Task.FromResult<bool?>(answer));

    private void Pair(string name, string thumbprint, DateTimeOffset? lastUsed = null)
        => _registry.TryRegister(new PairedDevice
        {
            Name = name,
            Thumbprint = thumbprint,
            PairedAtUtc = _clock.Now,
            LastUsedUtc = lastUsed ?? _clock.Now,
        });

    [Fact]
    public void AnEmptyRegistrySaysSo()
    {
        var vm = NewViewModel();

        vm.Load();

        Assert.True(vm.HasNoDevices);
        Assert.Empty(vm.Devices);
        Assert.Contains("0", vm.CountText);
    }

    [Fact]
    public void PairedDevicesAreListedWithTheirNames()
    {
        Pair("Ulf's phone", "AA11");
        Pair("Kitchen tablet", "BB22");
        var vm = NewViewModel();

        vm.Load();

        Assert.False(vm.HasNoDevices);
        Assert.Equal(["Ulf's phone", "Kitchen tablet"], vm.Devices.Select(d => d.Name));
        Assert.Contains("2", vm.CountText);
    }

    [Fact]
    public void TheCompanionBeingOffIsCalledOut()
    {
        var vm = NewViewModel();
        vm.Load();

        Assert.True(vm.IsCompanionOff);
    }

    [Fact]
    public void TheCompanionBeingOnIsNotCalledOut()
    {
        _bootstrap.Update(c => c.Companion = new CompanionOptions { Enabled = true });
        var vm = NewViewModel();

        vm.Load();

        Assert.False(vm.IsCompanionOff);
    }

    /// <summary>
    /// The limit is the desktop's rule, so the desktop says so — before it offers a code that it has already
    /// decided to refuse, and beside the list the user has to remove a device from.
    /// </summary>
    [Fact]
    public void WithEverySlotTakenPairingIsNotOffered()
    {
        _bootstrap.Update(c => c.Companion = new CompanionOptions { Enabled = true });
        for (var i = 1; i <= DeviceRegistry.MaxDevices; i++)
        {
            Pair($"Device {i}", $"FILL{i}");
        }

        var vm = NewViewModel();
        vm.Load();

        Assert.True(vm.IsAtDeviceLimit);
        Assert.False(vm.CanPair);
        Assert.NotEmpty(vm.AtLimitText);
    }

    [Fact]
    public void WithASlotFreePairingIsOffered()
    {
        _bootstrap.Update(c => c.Companion = new CompanionOptions { Enabled = true });
        Pair("Ulf's phone", "AA11");

        var vm = NewViewModel();
        vm.Load();

        Assert.False(vm.IsAtDeviceLimit);
        Assert.True(vm.CanPair);
    }

    [Fact]
    public async Task RemovingADeviceAsksFirstAndThenRevokesIt()
    {
        Pair("Ulf's phone", "AA11");
        Confirm(true);
        var vm = NewViewModel();
        vm.Load();

        await vm.RemoveCommand.ExecuteAsync(vm.Devices[0]);

        await _windows.Received(1).ShowDeleteConfirmationAsync(
            Arg.Is<string>(m => m != null && m.Contains("Ulf's phone")),
            Arg.Any<string?>(),
            Arg.Any<string?>());
        Assert.False(_registry.IsRegistered("AA11"));
        Assert.True(vm.HasNoDevices);
    }

    /// <summary>
    /// The confirmation is the shared delete dialog, whose buttons are written for books — its cancel reads
    /// "Keep book". Removing a phone has to bring its own words, or it asks whether to keep a book that was
    /// never in question.
    /// </summary>
    [Fact]
    public async Task RemovingADeviceAsksInTheDevicesOwnWords()
    {
        Pair("Ulf's phone", "AA11");
        Confirm(false);
        var vm = NewViewModel();
        vm.Load();

        await vm.RemoveCommand.ExecuteAsync(vm.Devices[0]);

        await _windows.Received(1).ShowDeleteConfirmationAsync(
            Arg.Any<string>(),
            BookDB.Desktop.Localization.Resources.Devices_Remove,
            BookDB.Desktop.Localization.Resources.Common_Cancel);
        await _windows.DidNotReceive().ShowDeleteConfirmationAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            BookDB.Desktop.Localization.Resources.Delete_Cancel_Button);
    }

    [Fact]
    public async Task DecliningTheConfirmationKeepsTheDevice()
    {
        Pair("Ulf's phone", "AA11");
        Confirm(false);
        var vm = NewViewModel();
        vm.Load();

        await vm.RemoveCommand.ExecuteAsync(vm.Devices[0]);

        Assert.True(_registry.IsRegistered("AA11"));
        Assert.Single(vm.Devices);
    }

    [Fact]
    public async Task ShowingThePairingCodeRefreshesTheListAfterwards()
    {
        var vm = NewViewModel();
        vm.Load();
        // A device pairs while the dialog is open.
        _windows.ShowPairingDialogAsync().Returns(_ =>
        {
            Pair("Kitchen tablet", "BB22");
            return Task.CompletedTask;
        });

        await vm.ShowPairingCodeCommand.ExecuteAsync(null);

        Assert.Single(vm.Devices);
        Assert.Equal("Kitchen tablet", vm.Devices[0].Name);
    }

    [Fact]
    public void ARecentlyUsedDeviceReadsAsRecentRatherThanADate()
    {
        Pair("Ulf's phone", "AA11", lastUsed: DateTimeOffset.UtcNow);
        var vm = NewViewModel();

        vm.Load();

        Assert.Equal(BookDB.Desktop.Localization.Resources.Devices_LastUsed_JustNow, vm.Devices[0].LastUsed);
    }

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
