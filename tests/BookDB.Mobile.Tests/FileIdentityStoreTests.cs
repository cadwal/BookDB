using System;
using System.IO;
using BookDB.Mobile.Services;
using Xunit;

namespace BookDB.Mobile.Tests;

/// <summary>The persisted identity must survive an app restart (a fresh store on the same directory) and
/// treat a corrupt file as "not paired" rather than crashing.</summary>
public sealed class FileIdentityStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"bookdb_id_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void SavedIdentity_IsReadBackByAFreshStore()
    {
        var identity = new DeviceIdentity("192.168.1.20:7443", ["AA11", "BB22"], [1, 2, 3, 4]);
        new FileIdentityStore(_dir).Save(identity);

        var reloaded = new FileIdentityStore(_dir);

        Assert.True(reloaded.HasIdentity);
        var loaded = reloaded.Load();
        Assert.NotNull(loaded);
        Assert.Equal("192.168.1.20:7443", loaded!.Endpoint);
        Assert.Equal(["AA11", "BB22"], loaded.ServerThumbprints);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, loaded.ClientPfx);
    }

    [Fact]
    public void TheDeviceNameAndTheComputersInstanceId_SurviveTooSinceReconnectingNeedsThem()
    {
        new FileIdentityStore(_dir).Save(
            new DeviceIdentity("192.168.1.20:7443", ["AA11"], [1], "Kitchen tablet", "instance-1"));

        var loaded = new FileIdentityStore(_dir).Load();

        Assert.Equal("Kitchen tablet", loaded!.DeviceName);
        Assert.Equal("instance-1", loaded.InstanceId);
        Assert.Equal(7443, loaded.Port);
    }

    [Theory]
    [InlineData("192.168.1.20:7443", 7443)]
    [InlineData("no-port-here", 0)]
    [InlineData("host:not-a-number", 0)]
    public void ThePortIsReadOffTheEndpoint_BecauseDiscoveryListensOneAboveIt(string endpoint, int expected)
    {
        Assert.Equal(expected, new DeviceIdentity(endpoint, ["AA"], [1]).Port);
    }

    [Fact]
    public void AFreshDirectory_IsNotPaired()
    {
        var store = new FileIdentityStore(_dir);

        Assert.False(store.HasIdentity);
        Assert.Null(store.Load());
    }

    [Fact]
    public void Clearing_RemovesTheIdentity()
    {
        var store = new FileIdentityStore(_dir);
        store.Save(new DeviceIdentity("host:1", ["AA"], [9]));

        store.Clear();

        Assert.False(store.HasIdentity);
        Assert.Null(store.Load());
    }

    [Fact]
    public void ACorruptFile_ReadsAsNotPaired()
    {
        var store = new FileIdentityStore(_dir);
        store.Save(new DeviceIdentity("host:1", ["AA"], [9]));
        File.WriteAllText(Path.Combine(_dir, "identity.json"), "{ this is not valid json");

        Assert.False(store.HasIdentity);
        Assert.Null(store.Load());
    }
}
