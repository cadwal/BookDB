using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Contracts;
using BookDB.Logic.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProtoBuf.Grpc.Server;

namespace BookDB.Companion.Host;

/// <summary>
/// The in-process gRPC listener: Kestrel over HTTP/2 with mutual TLS, started and stopped on demand by the
/// desktop. Nothing listens until the user turns the companion on.
/// </summary>
public sealed class CompanionHost : IAsyncDisposable
{
    private readonly CompanionHostOptions _options;
    private readonly IDeviceRegistry _registry;
    private readonly IPairingService _pairing;
    private readonly ICompanionEnvironment _environment;
    private readonly CompanionLibrary _library;
    private readonly TimeProvider _clock;

    private WebApplication? _app;
    private X509Certificate2? _serverCertificate;
    private DiscoveryBeacon? _beacon;

    public CompanionHost(
        CompanionHostOptions options,
        IDeviceRegistry registry,
        IPairingService pairing,
        ICompanionEnvironment environment,
        CompanionLibrary library,
        TimeProvider clock)
    {
        _options = options;
        _registry = registry;
        _pairing = pairing;
        _environment = environment;
        _library = library;
        _clock = clock;
    }

    public bool IsRunning => _app is not null;

    /// <summary>The port actually listening, which differs from the requested one when that was 0.</summary>
    public int BoundPort { get; private set; }

    /// <summary>Whether a phone can re-find this desktop after an address change; false is degraded, not broken.</summary>
    public bool IsDiscoveryListening => _beacon?.IsListening ?? false;

    /// <summary>What a phone pins to recognise this desktop. Available only while running.</summary>
    public string ServerThumbprint => _serverCertificate is null
        ? throw new InvalidOperationException("The companion host is not running.")
        : CertificateFactory.Sha256Thumbprint(_serverCertificate);

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_app is not null)
        {
            return;
        }

        var certificate = _registry.GetOrCreateServerCertificate();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddSingleton(_registry);
        builder.Services.AddSingleton(_pairing);
        builder.Services.AddSingleton(_environment);
        builder.Services.AddSingleton(_library);
        builder.Services.AddSingleton(_clock);
        builder.Services.AddSingleton(new CompanionBatchSessions(_clock));
        // Per run: the cache is a session convenience, and a restart is the cheapest way to drop stale covers.
        builder.Services.AddSingleton(new ThumbnailCache());
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<ClientCertificateInterceptor>();
        builder.Services.AddCodeFirstGrpc(grpc =>
        {
            grpc.Interceptors.Add<ClientCertificateInterceptor>();
            // The phone downscales every photo to the capture settings before sending, so anything this
            // far above them is a bug or an attack; either way it is refused rather than buffered.
            grpc.MaxReceiveMessageSize = _options.MaxMessageBytes;
        });

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            var address = _options.BindAllInterfaces ? IPAddress.Any : IPAddress.Loopback;
            kestrel.Listen(address, _options.Port, listen =>
            {
                listen.Protocols = HttpProtocols.Http2;
                listen.UseHttps(https =>
                {
                    https.ServerCertificate = certificate;
                    // HTTP/2 forbids TLS renegotiation, so the certificate has to be asked for during the
                    // handshake. It is accepted unconditionally here — a self-signed device certificate has
                    // no chain to validate — and the interceptor enforces the actual allow-list.
                    https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    https.ClientCertificateValidation = (_, _, _) => true;
                });
            });
        });

        var app = builder.Build();
        app.MapGrpcService<CompanionScannerService>();

        try
        {
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A failed bind (port in use) must leave nothing half-started for the next attempt.
            await app.DisposeAsync().ConfigureAwait(false);
            certificate.Dispose();
            throw;
        }

        _app = app;
        _serverCertificate = certificate;
        BoundPort = ResolveBoundPort(app);

        // Only now is the port known, and a beacon that cannot bind is not a reason to refuse to serve.
        _beacon = new DiscoveryBeacon(_environment.InstanceId, BoundPort);
        _beacon.TryStart();
    }

    /// <summary>
    /// Mints a pairing offer and wraps it as the payload the QR carries. <paramref name="host"/> is the
    /// address the phone should dial — the desktop picks it, since only it knows which interface the user
    /// means; the port is whatever is actually bound.
    /// </summary>
    public PairingPayload CreatePairingPayload(string host) => BeginPairing(host).Payload;

    /// <summary>
    /// Mints an offer and everything the pairing UI needs to follow it: the payload for the QR, the
    /// thumbprint to watch for a claim, and when the code stops being valid.
    /// </summary>
    public CompanionPairingOffer BeginPairing(string host)
    {
        if (_app is null)
        {
            throw new InvalidOperationException("The companion host is not running.");
        }

        var offer = _pairing.BeginPairing();

        var payload = new PairingPayload
        {
            Endpoint = $"{host}:{BoundPort}",
            ServerThumbprints = [ServerThumbprint],
            ClientPfxBase64 = Convert.ToBase64String(CertificateFactory.ExportPfx(offer.DeviceCertificate)),
            IssuedAtUtc = offer.IssuedAtUtc,
        };

        return new CompanionPairingOffer(payload, offer.Thumbprint, offer.ExpiresAtUtc);
    }

    /// <summary>Takes an offer out of circulation — what closing the screen that showed it has to do.</summary>
    public void WithdrawPairing(string thumbprint) => _pairing.Withdraw(thumbprint);

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var app = _app;
        if (app is null)
        {
            return;
        }

        _app = null;

        if (_beacon is not null)
        {
            await _beacon.StopAsync().ConfigureAwait(false);
            _beacon = null;
        }

        await app.StopAsync(cancellationToken).ConfigureAwait(false);
        // Disposing is what actually releases the socket, so a restart on the same port can bind again.
        await app.DisposeAsync().ConfigureAwait(false);

        _serverCertificate?.Dispose();
        _serverCertificate = null;
        BoundPort = 0;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private static int ResolveBoundPort(WebApplication app)
    {
        string? address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();

        return address is not null && Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri.Port : 0;
    }
}
