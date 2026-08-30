using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;

namespace BookDB.Companion.Host;

/// <summary>
/// The allow-list gate. TLS accepts any client certificate — a self-signed device certificate has no chain
/// to validate — so authorisation happens here instead: the connection's certificate is reduced to its
/// SHA-256 thumbprint and matched against the paired devices (or a live pairing offer, which still has to
/// be able to call in to claim itself).
/// </summary>
public sealed class ClientCertificateInterceptor : Interceptor
{
    internal const string ThumbprintItemKey = "bookdb.companion.thumbprint";

    private readonly IPairingService _pairing;
    private readonly IDeviceRegistry _registry;
    private readonly IHttpContextAccessor _http;

    public ClientCertificateInterceptor(IPairingService pairing, IDeviceRegistry registry, IHttpContextAccessor http)
    {
        _pairing = pairing;
        _registry = registry;
        _http = http;
    }

    private void Authorize()
    {
        var context = _http.HttpContext
            ?? throw new RpcException(new Status(StatusCode.Internal, "No HTTP context for this call."));

        var certificate = context.Connection.ClientCertificate
            ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "No client certificate."));

        string thumbprint = CertificateFactory.Sha256Thumbprint(certificate);
        if (!_pairing.IsAcceptableForConnection(thumbprint))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Unknown or revoked device."));
        }

        context.Items[ThumbprintItemKey] = thumbprint;
        _registry.TouchLastUsed(thumbprint);
    }

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        Authorize();
        return continuation(request, context);
    }

    public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream, ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Authorize();
        return continuation(requestStream, context);
    }

    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Authorize();
        return continuation(request, responseStream, context);
    }

    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Authorize();
        return continuation(requestStream, responseStream, context);
    }
}
