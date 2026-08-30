using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Companion.Host;
using BookDB.Models;
using Serilog;

namespace BookDB.Desktop.Services;

public enum CompanionHostState
{
    Stopped,
    Running,

    /// <summary>Enabling failed — almost always the port being in use; the toggle stays on so the user can fix it.</summary>
    Failed,
}

public sealed record CompanionHostStatus(CompanionHostState State, string? Endpoint = null, string? Error = null)
{
    public static readonly CompanionHostStatus Stopped = new(CompanionHostState.Stopped);
}

/// <summary>
/// Owns the companion host for the life of the desktop process: keeps the current settings in memory (so
/// the handshake and the running host read them without disk access), and starts, stops or restarts the
/// listener when the user saves Settings or when the app launches with it already enabled.
/// </summary>
public sealed class CompanionHostManager : ICompanionConfig, ICompanionStatusReporter, IAsyncDisposable, IDisposable
{
    private readonly IBootstrapConfigService _bootstrapConfig;
    private readonly IDeviceRegistry _registry;
    private readonly IPairingService _pairing;
    private readonly CompanionLibrary _library;
    private readonly ICompanionEnvironment _environment;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CompanionHost? _host;

    public CompanionHostManager(
        IBootstrapConfigService bootstrapConfig,
        IDeviceRegistry registry,
        IPairingService pairing,
        CompanionLibrary library,
        AppSettings appSettings,
        TimeProvider clock)
    {
        _bootstrapConfig = bootstrapConfig;
        _registry = registry;
        _pairing = pairing;
        _library = library;
        _clock = clock;
        Current = _bootstrapConfig.Load().Companion;
        // Built here rather than injected: the environment reads the live config off this manager, so having
        // the manager own it avoids a constructor cycle.
        _environment = new DesktopCompanionEnvironment(appSettings, this);
    }

    public CompanionOptions Current { get; private set; }

    public CompanionHostStatus Status { get; private set; } = CompanionHostStatus.Stopped;

    public int PairedDeviceCount => _registry.Count;

    public event EventHandler? StatusChanged;

    /// <summary>Told from outside when the device list changes — pairing and removal happen in the dialogs,
    /// not here, and the status bar's device count has to follow them.</summary>
    public void NotifyDevicesChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>The running host, for pairing — null unless the companion is up.</summary>
    public CompanionHost? RunningHost => _host;

    public IDeviceRegistry Registry => _registry;

    /// <summary>At launch, brings the host up if the saved settings say it should be. Failure is logged, not fatal.</summary>
    public async Task StartupAsync()
    {
        if (Current.Enabled)
        {
            await ApplyAsync(Current).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Persists the new settings and makes the running host match them: enabling starts it (minting the
    /// instance id and certificates on first use), disabling stops it, and a port change restarts it. The
    /// resulting <see cref="Status"/> is what Settings shows.
    /// </summary>
    public async Task<CompanionHostStatus> ApplyAsync(CompanionOptions options, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // The instance id is minted the first time the companion is ever enabled and then never changes,
            // so a phone can keep recognising this desktop across restarts and address changes.
            if (options.Enabled && string.IsNullOrEmpty(options.InstanceId))
            {
                options.InstanceId = Guid.NewGuid().ToString("N");
            }

            _bootstrapConfig.Update(c => c.Companion = options);
            Current = options;

            await StopHostAsync().ConfigureAwait(false);

            if (!options.Enabled)
            {
                return Report(CompanionHostStatus.Stopped);
            }

            var host = new CompanionHost(
                new CompanionHostOptions { Port = options.Port, BindAllInterfaces = true },
                _registry,
                _pairing,
                _environment,
                _library,
                _clock);

            try
            {
                await host.StartAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await host.DisposeAsync().ConfigureAwait(false);
                Log.Error(ex, "Companion host failed to start on port {Port}", options.Port);
                return Report(new CompanionHostStatus(CompanionHostState.Failed, Error: ex.Message));
            }

            _host = host;
            return Report(new CompanionHostStatus(CompanionHostState.Running, Endpoint: host.BoundPort.ToString()));
        }
        finally
        {
            _gate.Release();
        }
    }

    private CompanionHostStatus Report(CompanionHostStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
        return status;
    }

    public async ValueTask DisposeAsync()
    {
        await StopHostAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    // The DI container disposes singletons synchronously; a service that is only IAsyncDisposable makes it
    // throw. Blocking here is safe — StopHostAsync only awaits Kestrel shutdown, not the caller's context.
    public void Dispose()
    {
        _host?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _host = null;
        _gate.Dispose();
    }

    private async Task StopHostAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync().ConfigureAwait(false);
            _host = null;
        }
    }
}
