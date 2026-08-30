using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using BookDB.Contracts;

namespace BookDB.Mobile.Services;

/// <summary>Everything a paired device needs to reconnect on its own: where to dial, which server
/// certificates to pin, and the client certificate it presents. Extracted from the pairing code and then
/// persisted, so it survives an app restart.</summary>
public sealed record DeviceIdentity(
    string Endpoint,
    IReadOnlyList<string> ServerThumbprints,
    byte[] ClientPfx,
    string DeviceName = "",
    string InstanceId = "")
{
    /// <summary>The port half of <see cref="Endpoint"/>, or 0 when it cannot be read. Discovery listens one
    /// port above it, and the computer keeps its port across an address change, so this stays usable even
    /// when the host half has gone stale.</summary>
    public int Port
    {
        get
        {
            int separator = Endpoint.LastIndexOf(':');
            return separator >= 0
                && int.TryParse(
                    Endpoint.AsSpan(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port)
                ? port
                : 0;
        }
    }

    public X509Certificate2 LoadCertificate() => CompanionClientCertificates.LoadPfx(ClientPfx);

    public static DeviceIdentity FromPayload(PairingPayload payload, string deviceName = "") =>
        new(payload.Endpoint, payload.ServerThumbprints, Convert.FromBase64String(payload.ClientPfxBase64), deviceName);
}
