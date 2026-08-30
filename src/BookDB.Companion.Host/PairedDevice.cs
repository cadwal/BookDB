using System;

namespace BookDB.Companion.Host;

/// <summary>
/// A device that has completed pairing. <see cref="Thumbprint"/> is the SHA-256 hex of the device's client
/// certificate and is the only identity the host recognises — the name is for the user's benefit alone.
/// </summary>
public sealed record PairedDevice
{
    public string Name { get; init; } = "";
    public string Thumbprint { get; init; } = "";
    public DateTimeOffset PairedAtUtc { get; init; }
    public DateTimeOffset LastUsedUtc { get; init; }
}
