using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using BookDB.Contracts;
using BookDB.Mobile.Services;
using BookDB.Mobile.Staging;
using BookDB.Mobile.ViewModels;

// The headless UI smokes need a phone in the same states these fakes describe — a computer that answers or
// does not, a camera that refuses, a batch with something in it — so they share this set rather than keep a
// second one that can drift away from it.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("BookDB.Mobile.UITests")]

namespace BookDB.Mobile.Tests;

internal sealed class FakeIdentityStore : IIdentityStore
{
    private DeviceIdentity? _identity;

    public FakeIdentityStore(bool paired = false)
    {
        if (paired)
            _identity = new DeviceIdentity("host:1", ["AA"], [1, 2, 3]);
    }

    public FakeIdentityStore(DeviceIdentity identity) => _identity = identity;

    public int SaveCount { get; private set; }

    public bool HasIdentity => _identity is not null;

    public DeviceIdentity? Load() => _identity;

    public void Save(DeviceIdentity identity)
    {
        _identity = identity;
        SaveCount++;
    }

    public void Clear() => _identity = null;
}

internal sealed class FakeBarcodeScanner : IBarcodeScanner
{
    /// <summary>What the next scan reads. Setting <see cref="NextScan"/> instead lets a test say the camera
    /// refused rather than that nothing was read.</summary>
    public string? Next { get; set; }

    public BarcodeScan? NextScan { get; set; }

    /// <summary>What the last caller said it was scanning for. The kind decides which symbologies the real
    /// camera will look at, so a screen asking for the wrong one is a screen that cannot read its own code.</summary>
    public BarcodeKind? LastKind { get; private set; }

    public Task<BarcodeScan> ScanOnceAsync(BarcodeKind kind, CancellationToken ct = default)
    {
        LastKind = kind;
        return Task.FromResult(NextScan ?? (Next is null ? BarcodeScan.Nothing : BarcodeScan.Of(Next)));
    }
}

internal sealed class FakeDeviceSettings : IDeviceSettings
{
    public int OpenCount { get; private set; }

    public void OpenAppSettings() => OpenCount++;
}

/// <summary>A page with nothing on it, for tests about where the shell goes rather than what it shows.</summary>
internal sealed class StubPage : PageViewModel
{
    private readonly string _title;

    public StubPage(string title = "stub") => _title = title;

    public override string Title => _title;
}

internal sealed class FakeDocumentScanner : IDocumentScanner
{
    /// <summary>Answers in order; the last one repeats once the queue runs dry, so a test only spells out the
    /// results it cares about.</summary>
    public Queue<DocumentScanResult> Results { get; } = new();

    public DocumentScanResult Next { get; set; } = DocumentScanResult.Cancelled;

    public int Opened { get; private set; }

    public Task<DocumentScanResult> ScanPageAsync(CancellationToken ct = default)
    {
        Opened++;
        return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : Next);
    }
}

internal sealed class FakeCaptureProbe : ICaptureAvailabilityProbe
{
    public CaptureAvailability Availability { get; set; } = CaptureAvailability.Available;

    public CaptureAvailability Check() => Availability;
}

internal sealed class FakePairingCoordinator : IPairingCoordinator
{
    public PairingOutcome Outcome { get; set; } = PairingOutcome.Paired;
    public string? LastCode { get; private set; }
    public string? LastName { get; private set; }

    /// <summary>Set to have a successful pair leave an identity behind, as the real coordinator does — for
    /// tests about what the app does once it has one.</summary>
    public FakeIdentityStore? Store { get; set; }

    public Task<PairingOutcome> PairAsync(string scannedCode, string deviceName, CancellationToken ct = default)
    {
        LastCode = scannedCode;
        LastName = deviceName;

        if (Outcome == PairingOutcome.Paired)
        {
            Store?.Save(new DeviceIdentity("host:1", ["AA"], [1, 2, 3]));
        }

        return Task.FromResult(Outcome);
    }
}

internal sealed class FakeDesktopLocator : IDesktopLocator
{
    public DiscoveryReply? Reply { get; set; }

    public List<(string InstanceId, int Port)> Asked { get; } = [];

    public Task<DiscoveryReply?> LocateAsync(string instanceId, int port, CancellationToken ct = default)
    {
        Asked.Add((instanceId, port));
        return Task.FromResult(Reply);
    }
}

internal sealed class FakeConnectionService : IConnectionService
{
    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Offline;

    public ServerInfo? ServerInfo { get; set; }

    public int RefreshCount { get; private set; }

    public bool WasReset { get; private set; }

    public event EventHandler? StatusChanged;

    public void Report(ConnectionStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The open connection to hand out; null stands for offline.</summary>
    public ICompanionConnection? Connection { get; set; }

    /// <summary>Holds the connect attempt open. The real one costs seconds when there is nothing to reach —
    /// a 5 s handshake and a 2 s discovery — so a caller that only looks right against an instant answer is
    /// not being tested at all.</summary>
    public Task? ConnectGate { get; set; }

    public async Task<ICompanionConnection?> EnsureConnectedAsync(CancellationToken ct = default)
    {
        if (ConnectGate is not null)
            await ConnectGate.WaitAsync(ct);

        return Connection;
    }

    public Task RefreshAsync(CancellationToken ct = default)
    {
        RefreshCount++;
        return Task.CompletedTask;
    }

    public void Reset()
    {
        WasReset = true;
        Report(ConnectionStatus.Offline);
    }
}

internal sealed class FakeStagingStore : IStagingStore
{
    private readonly List<StagedItem> _items = [];

    /// <summary>The photos, kept in memory the way the real store keeps them on disk — an upload reads them
    /// back through <see cref="ReadImage"/>, so a fake that forgot them would send headers alone.</summary>
    private readonly Dictionary<(string, ScanImageType), byte[]> _images = [];

    public IReadOnlyList<StagedItem> Items => _items;

    public List<string> Removed { get; } = [];

    public event EventHandler? Changed;

    public void Save(StagedBook book)
    {
        foreach (var image in book.Images)
            _images[(book.ClientItemId, image.Type)] = image.Jpeg;

        var staged = new StagedItem
        {
            ClientItemId = book.ClientItemId,
            Isbn = book.Isbn,
            ImageTypes = [.. book.Images.Select(image => image.Type)],
            StagedAt = DateTimeOffset.UtcNow,
            Thumbnail = book.Images.Count > 0 ? book.Images[0].Jpeg : null,
        };

        int existing = _items.FindIndex(item => item.ClientItemId == book.ClientItemId);
        if (existing >= 0)
            _items[existing] = staged;
        else
            _items.Add(staged);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string clientItemId)
    {
        Removed.Add(clientItemId);
        _items.RemoveAll(item => item.ClientItemId == clientItemId);
        foreach (var key in _images.Keys.Where(key => key.Item1 == clientItemId).ToList())
            _images.Remove(key);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public byte[]? ReadImage(string clientItemId, ScanImageType type) =>
        _images.TryGetValue((clientItemId, type), out var jpeg) ? jpeg : null;
}

internal sealed class FakeNavigator : INavigator
{
    public List<PageViewModel> Pushed { get; } = [];
    public List<HubDestination> Destinations { get; } = [];
    public int HubCount { get; private set; }
    public int PairingCount { get; private set; }

    public List<PageViewModel> Replaced { get; } = [];

    public void NavigateTo(PageViewModel page) => Pushed.Add(page);

    public void NavigateTo(HubDestination destination) => Destinations.Add(destination);

    public void Replace(PageViewModel page) => Replaced.Add(page);

    public void ShowHub() => HubCount++;

    public void ShowPairing() => PairingCount++;
}
