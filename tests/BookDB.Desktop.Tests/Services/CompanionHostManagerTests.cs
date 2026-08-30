using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Companion.Host;
using BookDB.Desktop.Services;
using BookDB.Logic.Services;
using BookDB.Models;
using Xunit;

namespace BookDB.Desktop.Tests.Services;

/// <summary>
/// The desktop's ownership of the companion host: enabling starts a real listener and mints a lasting
/// instance id, disabling stops it, a port already in use fails without throwing, and the whole thing is
/// driven off the saved settings both at Save and at startup.
/// </summary>
public sealed class CompanionHostManagerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"bookdb_mgr_{Guid.NewGuid():N}");
    private readonly string _configPath;
    private readonly BootstrapConfigService _bootstrap;

    public CompanionHostManagerTests()
    {
        Directory.CreateDirectory(_dir);
        _configPath = Path.Combine(_dir, "config.json");
        _bootstrap = new BootstrapConfigService(_configPath);
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

    private CompanionHostManager NewManager()
    {
        var appSettings = new AppSettings
        {
            Backend = DatabaseBackend.Sqlite,
            SqliteLibraryPath = Path.Combine(_dir, "My Library.db"),
            ConfigPath = _configPath,
        };
        var registry = new DeviceRegistry(CompanionPaths.CompanionDirectory(_configPath), TimeProvider.System);
        var pairing = new PairingService(registry, TimeProvider.System);
        var library = new CompanionLibrary(new StubPreview(), new StubBrowse(), new StubIntake(), new StubQueueStatus());

        return new CompanionHostManager(_bootstrap, registry, pairing, library, appSettings, TimeProvider.System);
    }

    private static CompanionOptions Enabled(int port) => new() { Enabled = true, Port = port };

    [Fact]
    public async Task ADisabledCompanionStaysStopped()
    {
        var manager = NewManager();

        await manager.StartupAsync();

        Assert.Equal(CompanionHostState.Stopped, manager.Status.State);
        Assert.Null(manager.RunningHost);
        Assert.Null(_bootstrap.Load().Companion.InstanceId);
    }

    [Fact]
    public async Task EnablingStartsAListenerAndMintsAnInstanceId()
    {
        var manager = NewManager();

        var status = await manager.ApplyAsync(Enabled(port: 0), TestContext.Current.CancellationToken);

        Assert.Equal(CompanionHostState.Running, status.State);
        Assert.NotNull(manager.RunningHost);
        Assert.True(manager.RunningHost!.IsRunning);
        Assert.False(string.IsNullOrEmpty(manager.Current.InstanceId));

        await manager.DisposeAsync();
    }

    [Fact]
    public async Task EnablingPersistsTheCompanionBlock()
    {
        var manager = NewManager();

        await manager.ApplyAsync(new CompanionOptions { Enabled = true, Port = 0, MaxLongEdgePx = 1200, JpegQuality = 65 }, TestContext.Current.CancellationToken);

        var saved = _bootstrap.Load().Companion;
        Assert.True(saved.Enabled);
        Assert.Equal(1200, saved.MaxLongEdgePx);
        Assert.Equal(65, saved.JpegQuality);
        Assert.False(string.IsNullOrEmpty(saved.InstanceId));

        await manager.DisposeAsync();
    }

    [Fact]
    public async Task DisablingStopsTheListener()
    {
        var manager = NewManager();
        await manager.ApplyAsync(Enabled(port: 0), TestContext.Current.CancellationToken);

        var status = await manager.ApplyAsync(new CompanionOptions { Enabled = false }, TestContext.Current.CancellationToken);

        Assert.Equal(CompanionHostState.Stopped, status.State);
        Assert.Null(manager.RunningHost);
        Assert.False(_bootstrap.Load().Companion.Enabled);
    }

    [Fact]
    public async Task TheInstanceIdIsMintedOnceAndKeptAcrossADisableAndReEnable()
    {
        var manager = NewManager();
        await manager.ApplyAsync(Enabled(port: 0), TestContext.Current.CancellationToken);
        string first = manager.Current.InstanceId!;

        await manager.ApplyAsync(new CompanionOptions { Enabled = false, InstanceId = first }, TestContext.Current.CancellationToken);
        await manager.ApplyAsync(new CompanionOptions { Enabled = true, Port = 0, InstanceId = first }, TestContext.Current.CancellationToken);

        Assert.Equal(first, manager.Current.InstanceId);

        await manager.DisposeAsync();
    }

    [Fact]
    public async Task APortAlreadyInUseFailsWithoutThrowingAndKeepsTheSettingEnabled()
    {
        // Bind the same address the host does (all interfaces), or the two sockets would not collide.
        using var occupier = new TcpListener(IPAddress.Any, 0);
        occupier.Start();
        int takenPort = ((IPEndPoint)occupier.LocalEndpoint).Port;

        var manager = NewManager();
        var status = await manager.ApplyAsync(Enabled(takenPort), TestContext.Current.CancellationToken);

        Assert.Equal(CompanionHostState.Failed, status.State);
        Assert.Null(manager.RunningHost);
        // The toggle stays on and is persisted, so the user can just change the port and save again.
        Assert.True(_bootstrap.Load().Companion.Enabled);
    }

    [Fact]
    public async Task StartupBringsUpAHostThatWasLeftEnabled()
    {
        // A first run enables and persists; a fresh manager (a new app launch) must start it from the saved config.
        _bootstrap.Update(c => c.Companion = new CompanionOptions { Enabled = true, Port = 0, InstanceId = "abc" });

        var manager = NewManager();
        await manager.StartupAsync();

        Assert.Equal(CompanionHostState.Running, manager.Status.State);
        Assert.NotNull(manager.RunningHost);

        await manager.DisposeAsync();
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
