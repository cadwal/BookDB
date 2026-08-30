using System;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using BookDB.Contracts;
using Grpc.Net.Client;
using ProtoBuf.Grpc.Client;

namespace BookDB.Mobile.Services;

public interface ICompanionChannelFactory
{
    ICompanionConnection Connect(DeviceIdentity identity);
}

/// <summary>Opens the mutual-TLS gRPC channel to the desktop. The transport is fixed to
/// <see cref="SocketsHttpHandler"/> (Android's default handler cannot carry HTTP/2 gRPC), the OS trust store
/// is bypassed, and the server is accepted only when its SHA-256 thumbprint is one the pairing code pinned.</summary>
public sealed class CompanionChannelFactory : ICompanionChannelFactory
{
    public ICompanionConnection Connect(DeviceIdentity identity)
    {
        var certificate = identity.LoadCertificate();
        try
        {
            var handler = new SocketsHttpHandler
            {
                SslOptions = new SslClientAuthenticationOptions
                {
                    ClientCertificates = [certificate],
                    RemoteCertificateValidationCallback = (_, serverCertificate, _, _) =>
                        serverCertificate is X509Certificate2 server && IsPinned(identity, server),
                },
            };

            var channel = GrpcChannel.ForAddress(
                $"https://{identity.Endpoint}",
                new GrpcChannelOptions { HttpHandler = handler, DisposeHttpClient = true });

            return new CompanionConnection(channel, certificate, channel.CreateGrpcService<IBookScannerService>());
        }
        catch
        {
            certificate.Dispose();
            throw;
        }
    }

    private static bool IsPinned(DeviceIdentity identity, X509Certificate2 server)
    {
        var thumbprint = CompanionClientCertificates.Sha256Thumbprint(server);
        foreach (var pinned in identity.ServerThumbprints)
        {
            if (string.Equals(pinned, thumbprint, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private sealed class CompanionConnection : ICompanionConnection
    {
        private readonly GrpcChannel _channel;
        private readonly X509Certificate2 _certificate;

        public CompanionConnection(GrpcChannel channel, X509Certificate2 certificate, IBookScannerService service)
        {
            _channel = channel;
            _certificate = certificate;
            Service = service;
        }

        public IBookScannerService Service { get; }

        public void Dispose()
        {
            _channel.Dispose();
            _certificate.Dispose();
        }
    }
}
