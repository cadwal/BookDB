using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Contracts;

namespace BookDB.Mobile.Services;

/// <summary>Owns the one link to the paired computer: dials it, re-finds it after an address change, and
/// reports where that stands. Nothing else opens a channel.</summary>
public interface IConnectionService
{
    ConnectionStatus Status { get; }

    /// <summary>The last successful handshake — library name, capture parameters, versions. Null until one
    /// has succeeded; kept afterwards so a screen can still show what it knows while offline.</summary>
    ServerInfo? ServerInfo { get; }

    /// <summary>Raised on whichever thread the connection work ran on. Because the implementation never
    /// leaves the caller's synchronization context, a caller on the UI thread is notified on the UI thread.</summary>
    event EventHandler? StatusChanged;

    /// <summary>The open connection, dialling if there is not one yet. Null means offline. The service owns
    /// it and hands the same one to everybody — a caller must not dispose it.</summary>
    Task<ICompanionConnection?> EnsureConnectedAsync(CancellationToken ct = default);

    /// <summary>Re-runs the handshake, dropping a channel that has gone dead and rediscovering the computer
    /// if its address has moved. This is what makes the chip fall to Offline once the computer is gone.</summary>
    Task RefreshAsync(CancellationToken ct = default);

    /// <summary>Closes the connection and forgets the handshake — for unpairing.</summary>
    void Reset();
}
