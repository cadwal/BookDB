namespace BookDB.Contracts;

/// <summary>
/// Per-item lifecycle as the phone sees it: Received, then Saved or AddedToExisting once the desktop has
/// stored the item, then the catalogue queue's own progress. Failed can replace any of them.
/// </summary>
public enum BatchItemState
{
    Unknown = 0,
    Received = 1,
    Saved = 2,
    AddedToExisting = 3,
    Cataloguing = 4,
    Done = 5,
    NeedsReview = 6,
    Failed = 7,

    /// <summary>
    /// The ISBN was already in the library and the scan carried no photos, so nothing was added. Distinct
    /// from <see cref="AddedToExisting"/>, which means photos really were attached.
    /// </summary>
    AlreadyOwned = 8,
}
