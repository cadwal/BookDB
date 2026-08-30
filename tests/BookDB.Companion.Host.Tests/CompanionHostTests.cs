using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using BookDB.Companion.Host;
using BookDB.Contracts;
using Grpc.Core;
using Xunit;

namespace BookDB.Companion.Host.Tests;

/// <summary>
/// The host over a real socket: mutual TLS, the thumbprint allow-list, and the handshake call. Everything
/// runs on loopback with an OS-chosen port and freshly generated certificates.
/// </summary>
public sealed class CompanionHostTests
{
    [Fact]
    public async Task APairedDeviceGetsTheHandshakeAnswer()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();
        using var device = harness.PairDevice("Ulf's phone");

        var info = await harness.Connect(device).GetServerInfoAsync();

        Assert.Equal("4.0.0", info.AppVersion);
        Assert.Equal(ContractVersion.Current, info.ContractVersion);
        Assert.Equal("Böckerna", info.LibraryName);
        Assert.Equal("instance-1", info.InstanceId);
        Assert.Equal(1600, info.Capture.MaxLongEdgePx);
        Assert.Equal(80, info.Capture.JpegQuality);
    }

    [Fact]
    public async Task TheHandshakeReflectsAChangedLibraryWithoutARestart()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();
        using var device = harness.PairDevice("Phone");
        var client = harness.Connect(device);

        await client.GetServerInfoAsync();
        harness.Environment.LibraryName = "Andra biblioteket";

        Assert.Equal("Andra biblioteket", (await client.GetServerInfoAsync()).LibraryName);
    }

    [Fact]
    public async Task AnUnknownDeviceIsRefused()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();
        using var stranger = CertificateFactory.CreateDeviceCertificate("stranger");

        var error = await Assert.ThrowsAsync<RpcException>(
            async () => await harness.Connect(stranger).GetServerInfoAsync());

        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
    }

    [Fact]
    public async Task ARevokedDeviceIsRefusedOnItsNextCall()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();
        using var device = harness.PairDevice("Phone");
        var client = harness.Connect(device);
        await client.GetServerInfoAsync();

        harness.Registry.Revoke(CertificateFactory.Sha256Thumbprint(device));

        var error = await Assert.ThrowsAsync<RpcException>(async () => await client.GetServerInfoAsync());
        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
    }

    [Fact]
    public async Task AnUnclaimedOfferMayCallInSoItCanClaimItself()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();
        var offer = harness.Pairing.BeginPairing();

        var info = await harness.Connect(offer.DeviceCertificate).GetServerInfoAsync();

        Assert.Equal(ContractVersion.Current, info.ContractVersion);
    }

    /// <summary>
    /// Reaching the host is not the same as being allowed to use it. Until a device has claimed, it is not
    /// in the list the user can see and revoke from, so it may do exactly two things: ask who is answering,
    /// and claim. Reading the library or writing to it before that would leave no trace anywhere the owner
    /// could look.
    /// </summary>
    [Fact]
    public async Task AnUnclaimedOfferMayNotReadOrWriteTheLibrary()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();
        var offer = harness.Pairing.BeginPairing();
        var client = harness.Connect(offer.DeviceCertificate);

        var read = await Assert.ThrowsAsync<RpcException>(async () => await client.ListBooksAsync(new BookQuery()));
        Assert.Equal(StatusCode.PermissionDenied, read.StatusCode);

        var detail = await Assert.ThrowsAsync<RpcException>(
            async () => await client.GetBookDetailAsync(new BookRef { BookId = 1 }));
        Assert.Equal(StatusCode.PermissionDenied, detail.StatusCode);

        var isbn = await Assert.ThrowsAsync<RpcException>(
            async () => await client.CheckIsbnAsync(new IsbnQuery { Isbn = "9780441013593" }));
        Assert.Equal(StatusCode.PermissionDenied, isbn.StatusCode);

        // And once it claims, the same device may do all of it.
        await client.ClaimDeviceAsync(new ClaimDeviceRequest { DeviceName = "Kitchen tablet" });
        Assert.Empty((await client.ListBooksAsync(new BookQuery())).Books);
    }

    [Fact]
    public async Task AnExpiredOfferMayNoLongerCallIn()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();
        var offer = harness.Pairing.BeginPairing();

        harness.Clock.Now += PairingService.PairingTtl;

        var error = await Assert.ThrowsAsync<RpcException>(
            async () => await harness.Connect(offer.DeviceCertificate).GetServerInfoAsync());
        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
    }

    [Fact]
    public async Task AClientWithNoCertificateCannotEvenCompleteTheHandshake()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();

        var error = await Assert.ThrowsAsync<RpcException>(
            async () => await harness.Connect(null).GetServerInfoAsync());

        // A transport-level refusal, not an application one: the certificate is demanded during the TLS
        // handshake, so the call never reaches the interceptor.
        Assert.NotEqual(StatusCode.PermissionDenied, error.StatusCode);
    }

    [Fact]
    public async Task AClientThatPinsTheWrongServerRefusesToConnect()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();
        using var device = harness.PairDevice("Phone");
        using var otherServer = CertificateFactory.CreateServerCertificate("Someone-else");

        var client = harness.Connect(device, CertificateFactory.Sha256Thumbprint(otherServer));

        var error = await Assert.ThrowsAsync<RpcException>(async () => await client.GetServerInfoAsync());

        // The client rejects the server, so this fails in the handshake rather than being authorised and denied.
        Assert.NotEqual(StatusCode.PermissionDenied, error.StatusCode);
    }

    [Fact]
    public async Task ACallStampsTheDeviceAsRecentlyUsed()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();
        using var device = harness.PairDevice("Phone");
        string thumbprint = CertificateFactory.Sha256Thumbprint(device);

        harness.Clock.Now += TimeSpan.FromHours(3);
        await harness.Connect(device).GetServerInfoAsync();

        Assert.Equal(harness.Clock.Now, harness.Registry.Find(thumbprint)!.LastUsedUtc);
    }

    [Fact]
    public async Task TheHostBindsAnOsChosenPortAndReportsIt()
    {
        await using var harness = new CompanionHostHarness();

        await harness.StartAsync();

        Assert.True(harness.Host.IsRunning);
        Assert.True(harness.Host.BoundPort > 0);
    }

    [Fact]
    public async Task StoppingReleasesThePortAndTheHostCanTakeItAgain()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();
        int port = harness.Host.BoundPort;

        await harness.RestartOnAsync(port);

        Assert.Equal(port, harness.Host.BoundPort);
        using var device = harness.PairDevice("Phone");
        Assert.Equal(ContractVersion.Current, (await harness.Connect(device).GetServerInfoAsync()).ContractVersion);
    }

    [Fact]
    public async Task StoppingClosesTheListenerToNewConnections()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();
        int port = harness.Host.BoundPort;

        await harness.Host.StopAsync(TestContext.Current.CancellationToken);

        using var probe = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(
            async () => await probe.ConnectAsync("127.0.0.1", port, TestContext.Current.CancellationToken));
        Assert.False(harness.Host.IsRunning);
    }

    [Fact]
    public async Task StoppingATwiceStoppedHostIsHarmless()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();

        await harness.Host.StopAsync(TestContext.Current.CancellationToken);
        await harness.Host.StopAsync(TestContext.Current.CancellationToken);

        Assert.False(harness.Host.IsRunning);
    }

    [Fact]
    public async Task TheServerIdentityIsTheOneStoredInTheRegistryAndSurvivesARestart()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();
        string thumbprint = harness.Host.ServerThumbprint;

        await harness.RestartOnAsync(0);

        Assert.Equal(thumbprint, harness.Host.ServerThumbprint);
        using var stored = harness.Registry.GetOrCreateServerCertificate();
        Assert.Equal(CertificateFactory.Sha256Thumbprint(stored), thumbprint);
    }

    [Fact]
    public async Task AStoppedHostHasNoServerIdentityToOffer()
    {
        await using var harness = new CompanionHostHarness();
        await harness.StartAsync();
        await harness.Host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Throws<InvalidOperationException>(() => harness.Host.ServerThumbprint);
    }
}
