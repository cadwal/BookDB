using System;
using BookDB.Contracts;

namespace BookDB.Companion.Host;

/// <summary>
/// A live pairing code: what to show, what to watch for it being claimed, and when it stops being valid.
/// </summary>
public sealed record CompanionPairingOffer(PairingPayload Payload, string Thumbprint, DateTimeOffset ExpiresAtUtc);
