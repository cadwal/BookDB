using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BookDB.Desktop.Services.UpdateCheck;

/// <summary>Outcome of an update check: whether a newer version is available, that version, and the
/// install channel (which decides the upgrade hint).</summary>
public sealed record UpdateStatus(bool IsUpdateAvailable, UpdateVersion Current, UpdateVersion? Latest, InstallChannel Channel);

public interface IUpdateCheckService
{
    Task<UpdateStatus> CheckAsync(CancellationToken ct = default);
}

/// <summary>
/// Decides whether to show the "update available" indicator. Visibility is recomputed every call from
/// cached state (running version vs the last-seen latest), so an upgrade clears it immediately; the
/// network refresh that updates the cache is gated to at most once per week.
/// </summary>
public sealed class UpdateCheckService(
    UpdateVersion currentVersion,
    IInstallChannelProvider channelProvider,
    IUpdateCheckStateStore stateStore,
    Func<InstallChannel, IReleaseVersionSource> sourceSelector,
    Func<DateTimeOffset> utcNow,
    ILogger<UpdateCheckService> logger) : IUpdateCheckService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(7);

    public async Task<UpdateStatus> CheckAsync(CancellationToken ct = default)
    {
        var channel = channelProvider.Current;
        var state = stateStore.Load();

        UpdateVersion? latest = UpdateVersion.TryParse(state.LastSeenLatest, out var cached) ? cached : null;

        if (IsRefreshDue(state.LastCheckUtc))
        {
            var now = utcNow();
            UpdateVersion? fetched = null;
            try
            {
                fetched = await sourceSelector(channel).GetLatestStableAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogInformation(ex, "Update check source threw (ignored)");
            }

            // Record the attempt either way so a persistently-failing source cannot be re-hit on every
            // launch — the weekly cadence is also what keeps us within the unauthenticated API rate limit.
            if (fetched is { } f)
            {
                latest = f;
                stateStore.Save(new UpdateCheckState(now, f.ToString()));
            }
            else
            {
                stateStore.Save(state with { LastCheckUtc = now });
            }
        }

        var available = latest is { } l && l > currentVersion;
        return new UpdateStatus(available, currentVersion, latest, channel);
    }

    private bool IsRefreshDue(DateTimeOffset? lastCheck) =>
        lastCheck is not { } last || utcNow() - last >= CheckInterval;
}
