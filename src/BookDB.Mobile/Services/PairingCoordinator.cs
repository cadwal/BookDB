using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Contracts;
using Grpc.Core;
using ProtoBuf.Grpc;

namespace BookDB.Mobile.Services;

/// <summary>Runs a pairing attempt end to end: read the scanned code, dial the desktop, check the two ends
/// speak the same contract version, claim the device, and — only on success — persist the identity so the
/// device reconnects on its own from then on.</summary>
public sealed class PairingCoordinator : IPairingCoordinator
{
    private readonly ICompanionChannelFactory _channelFactory;
    private readonly IIdentityStore _identityStore;

    public PairingCoordinator(ICompanionChannelFactory channelFactory, IIdentityStore identityStore)
    {
        _channelFactory = channelFactory;
        _identityStore = identityStore;
    }

    public async Task<PairingOutcome> PairAsync(string scannedCode, string deviceName, CancellationToken ct = default)
    {
        DeviceIdentity identity;
        try
        {
            identity = DeviceIdentity.FromPayload(PairingPayload.FromQrString(scannedCode), deviceName);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return PairingOutcome.InvalidCode;
        }

        using var connection = _channelFactory.Connect(identity);
        var options = new CallContext(new CallOptions(cancellationToken: ct));

        try
        {
            var info = await connection.Service.GetServerInfoAsync(options);
            if (info.ContractVersion != ContractVersion.Current)
                return PairingOutcome.IncompatibleVersion;

            var result = await connection.Service.ClaimDeviceAsync(new ClaimDeviceRequest { DeviceName = deviceName }, options);
            switch (result.Outcome)
            {
                case ClaimOutcome.Registered:
                case ClaimOutcome.AlreadyRegistered:
                    // The instance id is only learned from the handshake, and it is what lets this device
                    // recognise the same computer again after its LAN address changes.
                    _identityStore.Save(identity with { InstanceId = info.InstanceId });
                    return PairingOutcome.Paired;
                case ClaimOutcome.DeviceLimitReached:
                    return PairingOutcome.DeviceLimitReached;
                default:
                    // The refused cases are supposed to be caught by the transport, not reach a claim; treat a
                    // stray one the same way the user should read it — the code is no longer good.
                    return PairingOutcome.CodeExpired;
            }
        }
        catch (RpcException rpc)
        {
            return rpc.StatusCode switch
            {
                // The desktop refuses an unknown, expired, spent or revoked identity before the claim.
                StatusCode.PermissionDenied or StatusCode.Unauthenticated => PairingOutcome.CodeExpired,
                StatusCode.Unavailable or StatusCode.DeadlineExceeded => PairingOutcome.CannotConnect,
                _ => PairingOutcome.Failed,
            };
        }
        catch (HttpRequestException)
        {
            // Wrong address, host down, or a server certificate that does not match the pinned thumbprint.
            return PairingOutcome.CannotConnect;
        }
    }
}
