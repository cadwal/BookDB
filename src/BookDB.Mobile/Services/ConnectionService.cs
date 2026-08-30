using System;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Contracts;
using Grpc.Core;
using ProtoBuf.Grpc;

namespace BookDB.Mobile.Services;

/// <summary>
/// The reconnect state machine: dial the address on file, and if that fails ask the local network where the
/// paired computer went — it is the same computer as long as it answers under the instance id from pairing.
/// A newly discovered address is written back to the identity, so the next start dials it directly. When
/// neither works the device is simply offline; capture goes on regardless, and re-pairing is the way back.
/// </summary>
/// <remarks>No <c>ConfigureAwait(false)</c> anywhere in here: status changes are raised inline, and staying
/// on the caller's context is what keeps a UI caller's chip updates on the UI thread.</remarks>
public sealed class ConnectionService : IConnectionService, IDisposable
{
    /// <summary>A computer that is reachable but not answering is no use to a screen waiting on it.</summary>
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);

    private readonly IIdentityStore _identityStore;
    private readonly ICompanionChannelFactory _channelFactory;
    private readonly IDesktopLocator _locator;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ICompanionConnection? _connection;
    private ConnectionStatus _status = ConnectionStatus.Offline;

    public ConnectionService(
        IIdentityStore identityStore, ICompanionChannelFactory channelFactory, IDesktopLocator locator)
    {
        _identityStore = identityStore;
        _channelFactory = channelFactory;
        _locator = locator;
    }

    public ConnectionStatus Status
    {
        get => _status;
        private set
        {
            if (_status == value)
                return;

            _status = value;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ServerInfo? ServerInfo { get; private set; }

    public event EventHandler? StatusChanged;

    /// <summary>
    /// Records what the last attempt found, except that revocation is not something the network can undo:
    /// once the computer has said it does not know this device, failing to reach it later says nothing new.
    /// Letting that fall back to Offline turned the chip grey again on the next hiccup and invited the user
    /// to wait for a connection that was never coming. Being accepted clears it; so does unpairing, which
    /// goes through <see cref="Reset"/> and is meant to.
    /// </summary>
    private void Report(ConnectionStatus status)
    {
        if (Status == ConnectionStatus.Revoked && status != ConnectionStatus.Connected)
            return;

        Status = status;
    }

    public async Task<ICompanionConnection?> EnsureConnectedAsync(CancellationToken ct = default) =>
        await ConnectAsync(revalidate: false, ct);

    public async Task RefreshAsync(CancellationToken ct = default) => await ConnectAsync(revalidate: true, ct);

    /// <summary>Unpairing, and the only way out of a revocation — so this one sets the status outright rather
    /// than going through <see cref="Report"/>.</summary>
    public void Reset()
    {
        Drop();
        ServerInfo = null;
        Status = ConnectionStatus.Offline;
    }

    public void Dispose()
    {
        Drop();
        _gate.Dispose();
    }

    private async Task<ICompanionConnection?> ConnectAsync(bool revalidate, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var identity = _identityStore.Load();
            if (identity is null)
            {
                Reset();
                return null;
            }

            if (_connection is not null && !revalidate)
                return _connection;

            Report(ConnectionStatus.Reconnecting);

            if (_connection is not null)
            {
                switch (await HandshakeAsync(_connection, ct))
                {
                    case Handshake.Accepted:
                        return _connection;
                    case Handshake.Refused:
                        Drop();
                        Report(ConnectionStatus.Revoked);
                        return null;
                }

                Drop();
            }

            switch (await DialAsync(identity, ct))
            {
                case Handshake.Accepted:
                    return _connection;
                case Handshake.Refused:
                    Report(ConnectionStatus.Revoked);
                    return null;
            }

            var moved = await RelocateAsync(identity, ct);
            if (moved is not null)
            {
                switch (await DialAsync(moved, ct))
                {
                    case Handshake.Accepted:
                        _identityStore.Save(moved);
                        return _connection;
                    case Handshake.Refused:
                        // The computer is where the broadcast said; it just will not have us.
                        _identityStore.Save(moved);
                        Report(ConnectionStatus.Revoked);
                        return null;
                }
            }

            Report(ConnectionStatus.Offline);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The same computer at a new address, or null when nothing answered for this instance.</summary>
    private async Task<DeviceIdentity?> RelocateAsync(DeviceIdentity identity, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(identity.InstanceId) || identity.Port <= 0)
            return null;

        var reply = await _locator.LocateAsync(
            identity.InstanceId, identity.Port, ct);
        if (reply is null)
            return null;

        string endpoint = $"{reply.Host}:{reply.Port}";
        return endpoint == identity.Endpoint ? null : identity with { Endpoint = endpoint };
    }

    private async Task<Handshake> DialAsync(DeviceIdentity identity, CancellationToken ct)
    {
        ICompanionConnection connection;
        try
        {
            connection = _channelFactory.Connect(identity);
        }
        catch (Exception ex) when (ex is CryptographicException or UriFormatException or ArgumentException)
        {
            // An unusable certificate or a nonsense endpoint reads as "cannot connect": the way out of that
            // is re-pairing, which the settings screen offers.
            return Handshake.Unreachable;
        }

        var outcome = await HandshakeAsync(connection, ct);
        if (outcome == Handshake.Accepted)
        {
            _connection = connection;
            return outcome;
        }

        connection.Dispose();
        return outcome;
    }

    private async Task<Handshake> HandshakeAsync(ICompanionConnection connection, CancellationToken ct)
    {
        var options = new CallContext(new CallOptions(
            deadline: DateTime.UtcNow + HandshakeTimeout, cancellationToken: ct));

        ServerInfo info;
        try
        {
            info = await connection.Service.GetServerInfoAsync(options);
        }
        catch (RpcException ex) when (
            ex.StatusCode is StatusCode.PermissionDenied or StatusCode.Unauthenticated)
        {
            // The computer is there and knows who we are; it has taken this device off its list. Telling the
            // user to check the network would send them looking for a fault that is not theirs.
            return Handshake.Refused;
        }
        catch (Exception ex) when (ex is RpcException or HttpRequestException or IOException or SocketException)
        {
            return Handshake.Unreachable;
        }

        // A computer that has moved on to a newer contract is not one this build can talk to; better an
        // honest offline than calls that fail one by one further in.
        if (info.ContractVersion != ContractVersion.Current)
            return Handshake.Unreachable;

        ServerInfo = info;
        Report(ConnectionStatus.Connected);
        return Handshake.Accepted;
    }

    /// <summary>Three outcomes, because "no answer" and "answered no" need different words on screen.</summary>
    private enum Handshake
    {
        Accepted,
        Unreachable,
        Refused,
    }

    private void Drop()
    {
        _connection?.Dispose();
        _connection = null;
    }
}
