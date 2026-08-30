using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BookDB.Contracts;

/// <summary>
/// What the pairing QR carries: where to dial, which server to trust, and the identity the device should
/// present. Not a gRPC message — it travels as a QR code, before there is a connection to send it over.
/// </summary>
public sealed class PairingPayload
{
    /// <summary>host:port to dial, e.g. "192.168.1.20:7443".</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>SHA-256 thumbprints of the server certificates to pin. Plural so a re-mint can be rolled out.</summary>
    public string[] ServerThumbprints { get; set; } = [];

    /// <summary>
    /// The device's identity: certificate + private key as base64 PKCS#12, with no password. A password
    /// printed beside the blob it protects would secure nothing, and it costs QR capacity we cannot spare.
    /// </summary>
    public string ClientPfxBase64 { get; set; } = "";

    public DateTimeOffset IssuedAtUtc { get; set; }

    /// <summary>
    /// Compact JSON, not base64-wrapped JSON: base64 would inflate the payload by a third and push a
    /// P-256 pairing past what a single QR code can hold.
    /// </summary>
    public string ToQrString() => JsonSerializer.Serialize(this, PairingPayloadJsonContext.Default.PairingPayload);

    /// <summary>
    /// Whether some scanned text is a pairing code at all: valid JSON, and carrying the three things pairing
    /// cannot proceed without. Answered where the code is scanned so a poster or a Wi-Fi code is refused on
    /// the spot, rather than after the device has been named and pairing attempted.
    /// </summary>
    public static bool TryParse(string qr, out PairingPayload? payload)
    {
        payload = null;

        PairingPayload? parsed;
        try
        {
            parsed = FromQrString(qr);
        }
        catch (FormatException)
        {
            return false;
        }

        if (parsed.Endpoint.Length == 0
            || parsed.ServerThumbprints.Length == 0
            || parsed.ClientPfxBase64.Length == 0)
        {
            return false;
        }

        payload = parsed;
        return true;
    }

    public static PairingPayload FromQrString(string qr)
    {
        PairingPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize(qr, PairingPayloadJsonContext.Default.PairingPayload);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The scanned code is not a BookDB pairing code.", ex);
        }

        return payload ?? throw new FormatException("The scanned code is not a BookDB pairing code.");
    }
}

// Source-generated so the payload still serializes on a trimmed mobile head, where reflection-based
// serialization can be stripped away.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PairingPayload))]
internal sealed partial class PairingPayloadJsonContext : JsonSerializerContext
{
}
