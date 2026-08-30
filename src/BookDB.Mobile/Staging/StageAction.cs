namespace BookDB.Mobile.Staging;

/// <summary>What the user asked for when they finished a book. All three stage the set; they differ only in
/// where the app goes next and whether an upload is kicked off.</summary>
public enum StageAction
{
    /// <summary>Straight back to ISBN capture — the fast loop through a stack of books.</summary>
    NextBook,

    /// <summary>Stage, then upload everything staged (queued until the computer is reachable).</summary>
    SendNow,

    /// <summary>Stage and stop; the batch waits for a deliberate upload.</summary>
    SaveToBatch,
}
