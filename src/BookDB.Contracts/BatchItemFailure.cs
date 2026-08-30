namespace BookDB.Contracts;

/// <summary>
/// Why an item failed, as a code rather than a sentence: the desktop and the phone do not share a language,
/// so the phone localizes this itself.
/// </summary>
public enum BatchItemFailure
{
    None = 0,
    Unknown = 1,
    InvalidIsbn = 2,
    ImageRejected = 3,
    SaveFailed = 4,

    /// <summary>The book was stored, but cataloguing it afterwards failed; the desktop keeps the book.</summary>
    CatalogueFailed = 5,
}
