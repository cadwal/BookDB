using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Companion.Host;
using BookDB.Contracts;
using BookDB.Logic.Services;
using Grpc.Net.Client;
using ProtoBuf.Grpc.Client;

namespace BookDB.Companion.Host.Tests;

/// <summary>
/// A real host on loopback with a real mutual-TLS client — the same handler configuration the phone uses
/// (<c>SocketsHttpHandler</c>, server pinned by SHA-256 thumbprint, OS trust store bypassed). Loopback and
/// an OS-chosen port keep the tests off the LAN and away from any firewall prompt.
/// </summary>
internal sealed class CompanionHostHarness : IAsyncDisposable
{
    internal sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 7, 23, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"bookdb_host_{Guid.NewGuid():N}");

    private readonly List<GrpcChannel> _channels = [];
    private readonly List<X509Certificate2> _certificates = [];

    public CompanionHostHarness()
    {
        Registry = new DeviceRegistry(_directory, Clock);
        Pairing = new PairingService(Registry, Clock);
        Host = NewHost(port: 0);
    }

    private CompanionHost NewHost(int port) => new(
        new CompanionHostOptions { Port = port, BindAllInterfaces = false },
        Registry,
        Pairing,
        Environment,
        new CompanionLibrary(Preview, Browse, Intake, QueueStatus),
        Clock);

    public FixedClock Clock { get; } = new();
    public DeviceRegistry Registry { get; }
    public PairingService Pairing { get; }
    public StubEnvironment Environment { get; } = new();
    public StubPreviewService Preview { get; } = new();
    public StubBrowseService Browse { get; } = new();
    public StubIntakeService Intake { get; } = new();
    public StubQueueStatusService QueueStatus { get; } = new();
    public CompanionHost Host { get; private set; }

    public Task StartAsync() => Host.StartAsync();

    /// <summary>
    /// Starts on a port free in <em>both</em> port spaces, which is what the discovery tests need.
    /// Kestrel's OS-chosen port is a TCP number and the beacon binds that same number over UDP: Windows
    /// hands out ephemeral ports sequentially and reserves whole 100-port UDP blocks (Hyper-V/WSL), so a
    /// retry loop over TCP-chosen numbers walks straight back into the block it just failed in — inside one,
    /// TCP binds and UDP cannot. Letting the OS choose a free <em>UDP</em> port is what skips the
    /// reservations; the remaining race is a number taken between the probe and the bind, which is what the
    /// retries are for.
    /// </summary>
    public async Task StartWithDiscoveryAsync()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            await RestartOnAsync(FreePortForBothProtocols());
            if (Host.IsDiscoveryListening)
            {
                return;
            }
        }

        throw new InvalidOperationException("Could not obtain a port free for both TCP and UDP.");
    }

    /// <summary>
    /// A number the OS reports free for UDP on every interface — where the beacon listens — and for TCP on
    /// loopback, where the host listens.
    /// </summary>
    private static int FreePortForBothProtocols()
    {
        // Rejected candidates stay bound until the search ends, so each pass is offered a fresh number
        // instead of the one that just failed; the caller gets the port only after they are all released.
        var rejected = new List<Socket>();
        try
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                rejected.Add(udp);
                udp.Bind(new IPEndPoint(IPAddress.Any, 0));
                int port = ((IPEndPoint)udp.LocalEndPoint!).Port;

                try
                {
                    using var tcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    tcp.Bind(new IPEndPoint(IPAddress.Loopback, port));
                    return port;
                }
                catch (SocketException)
                {
                    // Free for UDP, taken on TCP.
                }
            }
        }
        finally
        {
            foreach (var probe in rejected)
            {
                probe.Dispose();
            }
        }

        throw new InvalidOperationException("Could not obtain a port free for both TCP and UDP.");
    }

    /// <summary>Restarts on a specific port, which is how the "stopping frees the socket" check is made.</summary>
    public async Task RestartOnAsync(int port)
    {
        await Host.StopAsync();
        Host = NewHost(port);
        await Host.StartAsync();
    }

    /// <summary>Pairs a device the way the real flow does and hands back its client certificate.</summary>
    public X509Certificate2 PairDevice(string name)
    {
        var offer = Pairing.BeginPairing();
        Pairing.Claim(offer.Thumbprint, name);
        return offer.DeviceCertificate;
    }

    /// <summary>Mints a QR payload pointing at the loopback listener.</summary>
    public PairingPayload CreatePayload() => Host.CreatePairingPayload("127.0.0.1");

    /// <summary>
    /// Connects the way a freshly scanned phone does: identity and pinned server both taken from the
    /// payload, endpoint dialled as written.
    /// </summary>
    public IBookScannerService ConnectWith(PairingPayload payload)
    {
        var certificate = CertificateFactory.LoadPfx(Convert.FromBase64String(payload.ClientPfxBase64));
        _certificates.Add(certificate);

        return Connect(certificate, payload.ServerThumbprints[0], payload.Endpoint);
    }

    public IBookScannerService Connect(
        X509Certificate2? clientCertificate, string? pinnedServerThumbprint = null, string? endpoint = null)
    {
        string pinned = pinnedServerThumbprint ?? Host.ServerThumbprint;

        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    certificate is X509Certificate2 server
                    && string.Equals(
                        CertificateFactory.Sha256Thumbprint(server), pinned, StringComparison.OrdinalIgnoreCase),
            },
        };

        if (clientCertificate is not null)
        {
            handler.SslOptions.ClientCertificates = [clientCertificate];
        }

        var channel = GrpcChannel.ForAddress(
            $"https://{endpoint ?? $"127.0.0.1:{Host.BoundPort}"}",
            new GrpcChannelOptions { HttpHandler = handler, DisposeHttpClient = true });

        _channels.Add(channel);
        return channel.CreateGrpcService<IBookScannerService>();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var channel in _channels)
        {
            channel.Dispose();
        }

        foreach (var certificate in _certificates)
        {
            certificate.Dispose();
        }

        await Host.DisposeAsync();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    internal sealed class StubEnvironment : ICompanionEnvironment
    {
        public string AppVersion { get; set; } = "4.0.0";
        public string LibraryName { get; set; } = "Böckerna";
        public string InstanceId { get; set; } = "instance-1";
        public CaptureSettings Capture { get; set; } = new() { MaxLongEdgePx = 1600, JpegQuality = 80 };
    }

    /// <summary>
    /// The library behind the read calls, stubbed: what the host itself has to get right is the mapping,
    /// the downscaling and the caching, and the real services have their own tests against a real database.
    /// </summary>
    internal sealed class StubPreviewService : ICompanionPreviewService
    {
        public List<string> Queried { get; } = [];

        public IsbnPreviewResult Answer { get; set; } = new(false, null, null, null, IsbnPreviewOrigin.None);

        public Task<IsbnPreviewResult> PreviewIsbnAsync(string isbn, CancellationToken ct = default)
        {
            Queried.Add(isbn);
            return Task.FromResult(Answer);
        }
    }

    internal sealed class StubBrowseService : ICompanionBrowseService
    {
        public List<(string? Search, int? CollectionId, string? Isbn, int Skip, int Take)> Queries { get; } = [];
        public List<(int BookId, int ImageTypeId)> ImageReads { get; } = [];

        public BrowsePage Page { get; set; } = new([], 0);
        public Dictionary<(int BookId, int ImageTypeId), byte[]> Images { get; } = [];

        public List<BrowseCollection> Collections { get; } = [];

        /// <summary>Keyed by book id; a book that is not here stands for one deleted since it was listed.</summary>
        public Dictionary<int, BrowseDetail> Details { get; } = [];

        public List<int> DetailReads { get; } = [];

        public Task<BrowsePage> BrowseAsync(
            string? search, int? collectionId, string? isbn, int skip, int take, CancellationToken ct = default)
        {
            Queries.Add((search, collectionId, isbn, skip, take));
            return Task.FromResult(Page);
        }

        public Task<IReadOnlyList<BrowseCollection>> GetCollectionsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BrowseCollection>>(Collections);

        public Task<BrowseDetail?> GetDetailAsync(int bookId, CancellationToken ct = default)
        {
            DetailReads.Add(bookId);
            return Task.FromResult(Details.TryGetValue(bookId, out var detail) ? detail : null);
        }

        public Task<byte[]?> GetImageAsync(int bookId, int imageTypeId, CancellationToken ct = default)
        {
            ImageReads.Add((bookId, imageTypeId));
            return Task.FromResult(Images.TryGetValue((bookId, imageTypeId), out var bytes) ? bytes : null);
        }
    }

    /// <summary>
    /// Stands in for the real intake so the streaming protocol can be tested on its own: how the host reads
    /// the stream, groups images under headers, and only finalizes a complete item — the persistence itself
    /// has its own tests against a real database.
    /// </summary>
    internal sealed class StubIntakeService : ICompanionIntakeService
    {
        public List<CompanionScanItem> Taken { get; } = [];

        /// <summary>Assigns each accepted item an outcome and, for created items, a queue-item id to track.</summary>
        public Func<CompanionScanItem, CompanionIntakeResult> Handle { get; set; } =
            item => new CompanionIntakeResult(
                item.ClientItemId, CompanionIntakeOutcome.Created, 1, 100, CompanionIntakeFailure.None);

        public Task<CompanionIntakeResult> IntakeAsync(CompanionScanItem item, CancellationToken ct = default)
        {
            Taken.Add(item);
            return Task.FromResult(Handle(item));
        }
    }

    internal sealed class StubQueueStatusService : ICompanionQueueStatusService
    {
        public Dictionary<int, CompanionQueueItemStatus> Rows { get; } = [];

        public Task<IReadOnlyList<CompanionQueueItemStatus>> GetStatusesAsync(
            IReadOnlyList<int> batchQueueItemIds, CancellationToken ct = default)
        {
            IReadOnlyList<CompanionQueueItemStatus> rows =
                [.. batchQueueItemIds.Where(Rows.ContainsKey).Select(id => Rows[id])];
            return Task.FromResult(rows);
        }
    }
}
