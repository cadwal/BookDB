using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BookDB.Logic.Services;

public enum CompanionIntakeOutcome
{
    /// <summary>A new book was created from the ISBN and queued for cataloguing.</summary>
    Created,

    /// <summary>The ISBN was already in the library, and the scanned photos were added to that book.</summary>
    AddedToExisting,

    /// <summary>The ISBN was already in the library and there was nothing to add.</summary>
    AlreadyOwned,

    Failed,
}

public enum CompanionIntakeFailure
{
    None,
    InvalidIsbn,
    SaveFailed,
}

/// <summary>One photo from a scan, already cropped and downscaled by the phone.</summary>
public sealed record CompanionScanImage(int ImageTypeId, byte[] Jpeg);

/// <summary>
/// One scanned book as it arrived: an ISBN and the photos that belong with it. Photos are optional — a
/// book can be staged from its ISBN alone.
/// </summary>
public sealed record CompanionScanItem(string ClientItemId, string Isbn, IReadOnlyList<CompanionScanImage> Images);

public sealed record CompanionIntakeResult(
    string ClientItemId,
    CompanionIntakeOutcome Outcome,
    int? BookId,
    int? BatchQueueItemId,
    CompanionIntakeFailure Failure);

public interface ICompanionIntakeService
{
    /// <summary>
    /// Takes in one complete scanned item. Called only once every one of the item's photos has arrived, so
    /// an interrupted upload leaves nothing behind.
    /// </summary>
    Task<CompanionIntakeResult> IntakeAsync(CompanionScanItem item, CancellationToken ct = default);
}
