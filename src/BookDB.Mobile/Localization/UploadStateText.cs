using System.Globalization;
using BookDB.Contracts;

namespace BookDB.Mobile.Localization;

/// <summary>
/// Turns what the computer reports about a book into a line the reader can act on. The wire carries states
/// and failure codes, never sentences — the two ends do not share a language — so this is where they become
/// words, and a switch over the enums is what keeps a new code from going out untranslated.
/// </summary>
public static class UploadStateText
{
    public static string For(BatchItemStatus status) => status.State == BatchItemState.Failed
        ? ForFailure(status.Failure)
        : ForState(status.State, status.Title);

    public static string ForState(BatchItemState state, string? title) => state switch
    {
        BatchItemState.Received => Resources.Upload_State_Received,
        BatchItemState.Saved => Resources.Upload_State_Saved,
        BatchItemState.AddedToExisting => Titled(Resources.Upload_State_AddedToExisting, Resources.Upload_State_AddedToExistingUntitled, title),
        BatchItemState.AlreadyOwned => Titled(Resources.Upload_State_AlreadyOwned, Resources.Upload_State_AlreadyOwnedUntitled, title),
        BatchItemState.Cataloguing => Resources.Upload_State_Cataloguing,
        BatchItemState.Done => Titled(Resources.Upload_State_Done, Resources.Upload_State_DoneUntitled, title),
        BatchItemState.NeedsReview => Resources.Upload_State_NeedsReview,
        BatchItemState.Failed => ForFailure(BatchItemFailure.Unknown),

        // Unknown is also the row's own starting state, before the computer has said anything about it.
        _ => Resources.Upload_State_Waiting,
    };

    public static string ForFailure(BatchItemFailure failure) => failure switch
    {
        BatchItemFailure.InvalidIsbn => Resources.Upload_Failure_InvalidIsbn,
        BatchItemFailure.ImageRejected => Resources.Upload_Failure_ImageRejected,
        BatchItemFailure.SaveFailed => Resources.Upload_Failure_SaveFailed,
        BatchItemFailure.CatalogueFailed => Resources.Upload_Failure_CatalogueFailed,

        // A failure with no code, or one this version does not know, still has to say that it failed.
        _ => Resources.Upload_Failure_Unknown,
    };

    private static string Titled(string withTitle, string without, string? title) =>
        string.IsNullOrWhiteSpace(title)
            ? without
            : string.Format(CultureInfo.CurrentCulture, withTitle, title);
}
