using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Contracts;

namespace BookDB.Mobile.Services;

/// <summary>How a run of the upload ended, from the phone's point of view. Cataloguing may well still be
/// going on at the computer — what these say is whether the books are safely across.</summary>
public enum UploadResult
{
    /// <summary>Every book was accepted; nothing is left staged.</summary>
    Completed,

    NothingStaged,

    /// <summary>No computer to send to. Nothing was attempted and nothing was lost.</summary>
    Offline,

    /// <summary>
    /// The computer answered and refused this device — it has been removed from its list. Separate from
    /// Offline because "try again" is the wrong offer: nothing but pairing again will change it.
    /// </summary>
    Refused,

    /// <summary>The upload stopped part-way. Whatever was not confirmed is still staged, ready to be sent again.</summary>
    Interrupted,
}

/// <summary>Sends what is staged and reports what the computer makes of each book. A book leaves the tray
/// only when the computer says it has it.</summary>
public interface IUploadService
{
    Task<UploadResult> RunAsync(Action<BatchItemStatus> onStatus, CancellationToken ct = default);
}
