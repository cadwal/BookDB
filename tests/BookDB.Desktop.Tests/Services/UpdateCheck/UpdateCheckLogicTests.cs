using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Desktop.Services.UpdateCheck;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookDB.Desktop.Tests.Services.UpdateCheck;

public class UpdateVersionTests
{
    [Theory]
    [InlineData("3.2.0", 3, 2, 0)]
    [InlineData("v3.2.0", 3, 2, 0)]
    [InlineData("V3.2.1", 3, 2, 1)]
    [InlineData("3.2", 3, 2, 0)]
    [InlineData("4", 4, 0, 0)]
    [InlineData("3.1.0.5", 3, 1, 0)]
    public void TryParse_AcceptsStableVersions(string text, int major, int minor, int patch)
    {
        Assert.True(UpdateVersion.TryParse(text, out var v));
        Assert.Equal(new UpdateVersion(major, minor, patch), v);
    }

    [Theory]
    [InlineData("3.2.0-beta")]   // pre-release ignored
    [InlineData("3.2.0+build")]  // build metadata ignored
    [InlineData("not-a-version")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_RejectsPrereleaseAndGarbage(string? text)
    {
        Assert.False(UpdateVersion.TryParse(text, out _));
    }

    [Fact]
    public void Compare_OrdersByMajorMinorPatch()
    {
        Assert.True(V("3.2.0") > V("3.1.9"));
        Assert.True(V("3.2.1") > V("3.2.0"));
        Assert.True(V("4.0.0") > V("3.9.9"));
        Assert.False(V("3.1.0") > V("3.1.0"));
        Assert.True(V("3.1.0") <= V("3.1.0"));
    }

    private static UpdateVersion V(string s) { UpdateVersion.TryParse(s, out var v); return v; }
}

public class InstallChannelProviderTests
{
    private static readonly string LocalAppData =
        System.IO.Path.Combine("C:", "Users", "me", "AppData", "Local");

    [Fact]
    public void Detect_WingetPackageFolder_IsWinget()
    {
        // Built with Path.Combine so the separators match whichever OS runs the test.
        var exe = System.IO.Path.Combine(LocalAppData, "Microsoft", "WinGet", "Packages", "cadwal.BookDB_x", "bookdb.exe");
        Assert.Equal(InstallChannel.Winget,
            InstallChannelProvider.Detect(exe, appImagePath: null, LocalAppData, _ => false));
    }

    [Fact]
    public void Detect_WingetLinksShim_IsWinget()
    {
        var exe = System.IO.Path.Combine(LocalAppData, "Microsoft", "WinGet", "Links", "bookdb.exe");
        Assert.Equal(InstallChannel.Winget,
            InstallChannelProvider.Detect(exe, appImagePath: null, LocalAppData, _ => false));
    }

    [Fact]
    public void Detect_AppImageWithAmUpdaterSidecar_IsAppMan()
    {
        var appImage = "/opt/bookdb/bookdb.AppImage";
        // Build the expected sidecar path the way Detect does (Path.Combine on the AppImage's directory),
        // so the mock matches on Windows too — there Path.Combine yields a '\' separator. AppImage/AppMan
        // detection is Linux-only in practice, but the test must stay culture/OS-independent.
        var sidecar = Path.Combine(Path.GetDirectoryName(appImage)!, "AM-updater");
        Assert.Equal(InstallChannel.AppMan,
            InstallChannelProvider.Detect(
                processPath: appImage, appImagePath: appImage, localAppData: "",
                fileExists: p => p == sidecar));
    }

    [Fact]
    public void Detect_StandaloneAppImage_IsGitHub()
    {
        var appImage = "/home/me/Downloads/bookdb.AppImage";
        Assert.Equal(InstallChannel.GitHub,
            InstallChannelProvider.Detect(
                processPath: appImage, appImagePath: appImage, localAppData: "", fileExists: _ => false));
    }

    [Fact]
    public void Detect_PlainExecutable_IsGitHub()
    {
        Assert.Equal(InstallChannel.GitHub,
            InstallChannelProvider.Detect(@"C:\Apps\BookDB\bookdb.exe", null, LocalAppData, _ => false));
    }
}

public class WingetVersionParseTests
{
    [Fact]
    public void Parses_EnglishVersionLine()
    {
        var output = "Found BookDB [cadwal.BookDB]\nVersion: 3.2.0\nPublisher: cadwal\n";
        Assert.Equal(new UpdateVersion(3, 2, 0), WingetVersionSource.ParseWingetVersion(output));
    }

    [Fact]
    public void Parses_LocalizedVersionKey_ContainingVersion()
    {
        // Many latin-script locales keep "version" in the key (e.g. Italian "Versione").
        var output = "Trovato BookDB\nVersione: 3.3.1\n";
        Assert.Equal(new UpdateVersion(3, 3, 1), WingetVersionSource.ParseWingetVersion(output));
    }

    [Fact]
    public void ReturnsNull_WhenNoVersionLine()
    {
        Assert.Null(WingetVersionSource.ParseWingetVersion("No package found matching input criteria.\n"));
    }
}

public class UpdateCheckServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

    private static UpdateVersion V(string s) { UpdateVersion.TryParse(s, out var v); return v; }

    private sealed class FakeStore(UpdateCheckState state) : IUpdateCheckStateStore
    {
        public UpdateCheckState State = state;
        public UpdateCheckState Load() => State;
        public void Save(UpdateCheckState s) => State = s;
    }

    private sealed class FakeSource(UpdateVersion? version) : IReleaseVersionSource
    {
        public int Calls;
        public Task<UpdateVersion?> GetLatestStableAsync(CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(version);
        }
    }

    private sealed class FakeChannel(InstallChannel channel) : IInstallChannelProvider
    {
        public InstallChannel Current => channel;
    }

    private static UpdateCheckService Make(
        string current, FakeStore store, FakeSource source,
        InstallChannel channel = InstallChannel.GitHub, DateTimeOffset? now = null,
        Action<InstallChannel>? onSelect = null)
        => new(
            V(current), new FakeChannel(channel), store,
            ch => { onSelect?.Invoke(ch); return source; },
            () => now ?? Now,
            NullLogger<UpdateCheckService>.Instance);

    [Fact]
    public async Task WeekNotElapsed_UsesCachedState_WithoutHittingTheNetwork()
    {
        var store = new FakeStore(new UpdateCheckState(Now.AddDays(-1), "3.2.0"));
        var source = new FakeSource(V("9.9.9"));

        var status = await Make("3.1.0", store, source).CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, source.Calls);            // network not touched inside the weekly window
        Assert.True(status.IsUpdateAvailable);
        Assert.Equal(V("3.2.0"), status.Latest);  // from cache, not the source
    }

    [Fact]
    public async Task RunningVersionCaughtUp_HidesImmediately_FromCachedState()
    {
        var store = new FakeStore(new UpdateCheckState(Now.AddDays(-1), "3.1.0"));
        var source = new FakeSource(V("3.1.0"));

        var status = await Make("3.1.0", store, source).CheckAsync(TestContext.Current.CancellationToken);

        Assert.False(status.IsUpdateAvailable); // cleared the moment the running version matches
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task WeekElapsed_FetchesLatest_AndPersistsIt()
    {
        var store = new FakeStore(new UpdateCheckState(LastCheckUtc: null));
        var source = new FakeSource(V("3.2.0"));

        var status = await Make("3.1.0", store, source).CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, source.Calls);
        Assert.True(status.IsUpdateAvailable);
        Assert.Equal("3.2.0", store.State.LastSeenLatest);
        Assert.Equal(Now, store.State.LastCheckUtc);
    }

    [Fact]
    public async Task WeekElapsed_SourceFails_RecordsAttempt_KeepsPreviousLatest()
    {
        var store = new FakeStore(new UpdateCheckState(Now.AddDays(-8), "3.2.0"));
        var source = new FakeSource(version: null);

        var status = await Make("3.1.0", store, source).CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, source.Calls);
        Assert.True(status.IsUpdateAvailable);              // still known-available from cache
        Assert.Equal("3.2.0", store.State.LastSeenLatest);  // prior latest preserved
        Assert.Equal(Now, store.State.LastCheckUtc);        // attempt recorded → no re-hit next launch
    }

    [Fact]
    public async Task PassesDetectedChannel_ToTheSourceSelector()
    {
        var store = new FakeStore(new UpdateCheckState(LastCheckUtc: null));
        var source = new FakeSource(V("3.2.0"));
        InstallChannel? selected = null;

        await Make("3.1.0", store, source, channel: InstallChannel.Winget, onSelect: c => selected = c).CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(InstallChannel.Winget, selected);
    }
}
