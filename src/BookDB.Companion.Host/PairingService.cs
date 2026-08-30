using System;
using System.Collections.Generic;
using System.Linq;

namespace BookDB.Companion.Host;

/// <summary>
/// The pairing half of the trust model: mints single-use, short-lived offers and turns a claimed one into a
/// registered device. Offers never touch disk, so an app restart cancels any pairing in flight.
/// </summary>
public sealed class PairingService : IPairingService
{
    public static readonly TimeSpan PairingTtl = TimeSpan.FromMinutes(2);

    private readonly IDeviceRegistry _registry;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, PendingPairing> _pending = new(StringComparer.OrdinalIgnoreCase);

    public PairingService(IDeviceRegistry registry, TimeProvider clock)
    {
        _registry = registry;
        _clock = clock;
    }

    public PendingPairing BeginPairing()
    {
        var now = _clock.GetUtcNow();
        var certificate = CertificateFactory.CreateDeviceCertificate(Guid.NewGuid().ToString("N"));
        var pairing = new PendingPairing(certificate, now, PairingTtl);

        lock (_gate)
        {
            DropExpired(now);
            _pending[pairing.Thumbprint] = pairing;
        }

        return pairing;
    }

    public bool IsAcceptableForConnection(string thumbprint)
    {
        if (_registry.IsRegistered(thumbprint))
        {
            return true;
        }

        var now = _clock.GetUtcNow();
        lock (_gate)
        {
            DropExpired(now);
            return _pending.TryGetValue(thumbprint, out var pairing) && !pairing.IsClaimed;
        }
    }

    public PairingClaimOutcome Claim(string thumbprint, string deviceName)
    {
        var now = _clock.GetUtcNow();

        lock (_gate)
        {
            DropExpired(now);

            if (_registry.IsRegistered(thumbprint))
            {
                return PairingClaimOutcome.AlreadyRegistered;
            }

            if (!_pending.TryGetValue(thumbprint, out var pairing))
            {
                return PairingClaimOutcome.UnknownOrExpired;
            }

            if (pairing.IsClaimed)
            {
                return PairingClaimOutcome.AlreadyUsed;
            }

            var device = new PairedDevice
            {
                Name = deviceName,
                Thumbprint = thumbprint,
                PairedAtUtc = now,
                LastUsedUtc = now,
            };

            if (!_registry.TryRegister(device))
            {
                return PairingClaimOutcome.DeviceLimitReached;
            }

            // Single-use: the offer is spent even though the certificate lives on as the device's identity.
            pairing.MarkClaimed();
            return PairingClaimOutcome.Registered;
        }
    }

    public void Withdraw(string thumbprint)
    {
        lock (_gate)
        {
            _pending.Remove(thumbprint);
        }
    }

    /// <summary>
    /// Forgetting an offer is all that expiry means; the certificate is not disposed here because the caller
    /// of <see cref="BeginPairing"/> owns it and may still be holding the QR on screen.
    /// </summary>
    private void DropExpired(DateTimeOffset now)
    {
        foreach (var thumbprint in _pending.Where(p => p.Value.HasExpired(now)).Select(p => p.Key).ToArray())
        {
            _pending.Remove(thumbprint);
        }
    }
}
