// The compiler treats a *.Designer.cs file as generated and switches nullability off unless it is told
// otherwise; this one is written by hand and means its annotations.
#nullable enable

using System.Globalization;
using System.Resources;

namespace BookDB.Mobile.Localization;

/// <summary>Typed access to the mobile UI strings. Keys resolve through the satellite assemblies built from
/// the <c>Resources.*.resx</c> set (same nine locales as the desktop). Hand-maintained rather than
/// designer-generated so it stays reviewable in diffs.</summary>
public static class Resources
{
    private static readonly ResourceManager Manager =
        new("BookDB.Mobile.Localization.Resources", typeof(Resources).Assembly);

    private static string Get(string key) =>
        Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    /// <summary>The string behind a key that is only known at run time, or null when this app has no such
    /// string. Unlike <see cref="Get"/> a miss is an answer, not the key echoed back.</summary>
    public static string? Find(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture);

    public static string App_Title => Get(nameof(App_Title));
    public static string Nav_Back => Get(nameof(Nav_Back));
    public static string Hub_ScanBooks => Get(nameof(Hub_ScanBooks));
    public static string Hub_Browse => Get(nameof(Hub_Browse));
    public static string Hub_Settings => Get(nameof(Hub_Settings));
    public static string Status_Connected => Get(nameof(Status_Connected));
    public static string Status_Reconnecting => Get(nameof(Status_Reconnecting));
    public static string Status_Offline => Get(nameof(Status_Offline));
    public static string Status_Revoked => Get(nameof(Status_Revoked));
    public static string Status_RevokedExplanation => Get(nameof(Status_RevokedExplanation));
    public static string Scan_Title => Get(nameof(Scan_Title));
    public static string Browse_Title => Get(nameof(Browse_Title));
    public static string Settings_Title => Get(nameof(Settings_Title));
    public static string Pairing_Title => Get(nameof(Pairing_Title));
    public static string Pairing_Intro => Get(nameof(Pairing_Intro));
    public static string Pairing_ScanButton => Get(nameof(Pairing_ScanButton));
    public static string Pairing_DeviceNameLabel => Get(nameof(Pairing_DeviceNameLabel));
    public static string Pairing_PairButton => Get(nameof(Pairing_PairButton));
    public static string Pairing_Scanned => Get(nameof(Pairing_Scanned));
    public static string Pairing_ScanCancelled => Get(nameof(Pairing_ScanCancelled));
    public static string Pairing_Busy => Get(nameof(Pairing_Busy));
    public static string Pairing_Result_Paired => Get(nameof(Pairing_Result_Paired));
    public static string Pairing_Result_InvalidCode => Get(nameof(Pairing_Result_InvalidCode));
    public static string Pairing_Result_CodeExpired => Get(nameof(Pairing_Result_CodeExpired));
    public static string Pairing_Result_DeviceLimitReached => Get(nameof(Pairing_Result_DeviceLimitReached));
    public static string Pairing_Result_IncompatibleVersion => Get(nameof(Pairing_Result_IncompatibleVersion));
    public static string Pairing_Result_CannotConnect => Get(nameof(Pairing_Result_CannotConnect));
    public static string Pairing_Result_Failed => Get(nameof(Pairing_Result_Failed));
    public static string Settings_Group_Computer => Get(nameof(Settings_Group_Computer));
    public static string Settings_Group_Capture => Get(nameof(Settings_Group_Capture));
    public static string Settings_Group_ThisDevice => Get(nameof(Settings_Group_ThisDevice));
    public static string Settings_CaptureCaption => Get(nameof(Settings_CaptureCaption));
    public static string Settings_Status => Get(nameof(Settings_Status));
    public static string Settings_Library => Get(nameof(Settings_Library));
    public static string Settings_ComputerApp => Get(nameof(Settings_ComputerApp));
    public static string Settings_LongEdge => Get(nameof(Settings_LongEdge));
    public static string Settings_JpegQuality => Get(nameof(Settings_JpegQuality));
    public static string Settings_PixelsValue => Get(nameof(Settings_PixelsValue));
    public static string Settings_DeviceNameLabel => Get(nameof(Settings_DeviceNameLabel));
    public static string Settings_AppVersion => Get(nameof(Settings_AppVersion));
    public static string Settings_CheckConnection => Get(nameof(Settings_CheckConnection));
    public static string Settings_Unpair => Get(nameof(Settings_Unpair));
    public static string Settings_UnpairWarning => Get(nameof(Settings_UnpairWarning));
    public static string Settings_UnpairConfirm => Get(nameof(Settings_UnpairConfirm));
    public static string Settings_Cancel => Get(nameof(Settings_Cancel));
    public static string Settings_NotAvailable => Get(nameof(Settings_NotAvailable));
    public static string Settings_OfflineHint => Get(nameof(Settings_OfflineHint));
    public static string Scan_ScanButton => Get(nameof(Scan_ScanButton));
    public static string Scan_Or => Get(nameof(Scan_Or));
    public static string Scan_ManualWatermark => Get(nameof(Scan_ManualWatermark));
    public static string Scan_UseManual => Get(nameof(Scan_UseManual));
    public static string Scan_ChecksumOkMark => Get(nameof(Scan_ChecksumOkMark));
    public static string Scan_ChecksumWarningMark => Get(nameof(Scan_ChecksumWarningMark));
    public static string Scan_ChecksumWarning => Get(nameof(Scan_ChecksumWarning));
    public static string Scan_NothingRead => Get(nameof(Scan_NothingRead));
    public static string Scan_Continuing => Get(nameof(Scan_Continuing));
    public static string Scan_Undo => Get(nameof(Scan_Undo));
    public static string Scan_AlreadyInLibrary => Get(nameof(Scan_AlreadyInLibrary));
    public static string Scan_TitleAndAuthors => Get(nameof(Scan_TitleAndAuthors));
    public static string Scan_UntitledBook => Get(nameof(Scan_UntitledBook));
    public static string Scan_AlreadyInBatch => Get(nameof(Scan_AlreadyInBatch));
    public static string Scan_NotChecked => Get(nameof(Scan_NotChecked));
    public static string Builder_Title => Get(nameof(Builder_Title));
    public static string Builder_IsbnLabel => Get(nameof(Builder_IsbnLabel));
    public static string Builder_SlotFrontCover => Get(nameof(Builder_SlotFrontCover));
    public static string Builder_SlotBackCover => Get(nameof(Builder_SlotBackCover));
    public static string Builder_SlotSpine => Get(nameof(Builder_SlotSpine));
    public static string Builder_SlotDustJacket => Get(nameof(Builder_SlotDustJacket));
    public static string Builder_Optional => Get(nameof(Builder_Optional));
    public static string Builder_AddPhoto => Get(nameof(Builder_AddPhoto));
    public static string Builder_Retake => Get(nameof(Builder_Retake));
    public static string Builder_UsePhoto => Get(nameof(Builder_UsePhoto));
    public static string Builder_Done => Get(nameof(Builder_Done));
    public static string Builder_SendNow => Get(nameof(Builder_SendNow));
    public static string Builder_SaveToBatch => Get(nameof(Builder_SaveToBatch));
    public static string Builder_PhotosOptionalHint => Get(nameof(Builder_PhotosOptionalHint));
    public static string Builder_PlayServicesMissing => Get(nameof(Builder_PlayServicesMissing));
    public static string Builder_PlayServicesOutdated => Get(nameof(Builder_PlayServicesOutdated));
    public static string Builder_ModuleUnavailable => Get(nameof(Builder_ModuleUnavailable));
    public static string Builder_CaptureFailed => Get(nameof(Builder_CaptureFailed));
    public static string Builder_Retry => Get(nameof(Builder_Retry));
    public static string Camera_Instruction => Get(nameof(Camera_Instruction));
    public static string Camera_PairingInstruction => Get(nameof(Camera_PairingInstruction));
    public static string Camera_PairingRationale => Get(nameof(Camera_PairingRationale));
    public static string Camera_Close => Get(nameof(Camera_Close));
    public static string Camera_Torch => Get(nameof(Camera_Torch));
    public static string Camera_PermissionRationale => Get(nameof(Camera_PermissionRationale));
    public static string Batch_Title => Get(nameof(Batch_Title));
    public static string Batch_CountLine => Get(nameof(Batch_CountLine));
    public static string Batch_IsbnTail => Get(nameof(Batch_IsbnTail));
    public static string Batch_PhotoCount => Get(nameof(Batch_PhotoCount));
    public static string Batch_PhotoCountOne => Get(nameof(Batch_PhotoCountOne));
    public static string Batch_NoPhotos => Get(nameof(Batch_NoPhotos));
    public static string Batch_Edit => Get(nameof(Batch_Edit));
    public static string Batch_Remove => Get(nameof(Batch_Remove));
    public static string Batch_RemoveConfirm => Get(nameof(Batch_RemoveConfirm));
    public static string Batch_RemoveWarning => Get(nameof(Batch_RemoveWarning));
    public static string Batch_Cancel => Get(nameof(Batch_Cancel));
    public static string Batch_Upload => Get(nameof(Batch_Upload));
    public static string Batch_OfflineReason => Get(nameof(Batch_OfflineReason));
    public static string Batch_Empty => Get(nameof(Batch_Empty));
    public static string Batch_EmptyHint => Get(nameof(Batch_EmptyHint));
    public static string Hub_BatchItems => Get(nameof(Hub_BatchItems));
    public static string Upload_Title => Get(nameof(Upload_Title));
    public static string Upload_Running => Get(nameof(Upload_Running));
    public static string Upload_Completed => Get(nameof(Upload_Completed));
    public static string Upload_Interrupted => Get(nameof(Upload_Interrupted));
    public static string Upload_Offline => Get(nameof(Upload_Offline));
    public static string Upload_NothingStaged => Get(nameof(Upload_NothingStaged));
    public static string Upload_Retry => Get(nameof(Upload_Retry));
    public static string Upload_Finish => Get(nameof(Upload_Finish));
    public static string Upload_State_Waiting => Get(nameof(Upload_State_Waiting));
    public static string Upload_State_Received => Get(nameof(Upload_State_Received));
    public static string Upload_State_Saved => Get(nameof(Upload_State_Saved));
    public static string Upload_State_AddedToExisting => Get(nameof(Upload_State_AddedToExisting));
    public static string Upload_State_AddedToExistingUntitled => Get(nameof(Upload_State_AddedToExistingUntitled));
    public static string Upload_State_AlreadyOwned => Get(nameof(Upload_State_AlreadyOwned));
    public static string Upload_State_AlreadyOwnedUntitled => Get(nameof(Upload_State_AlreadyOwnedUntitled));
    public static string Upload_State_Cataloguing => Get(nameof(Upload_State_Cataloguing));
    public static string Upload_State_Done => Get(nameof(Upload_State_Done));
    public static string Upload_State_DoneUntitled => Get(nameof(Upload_State_DoneUntitled));
    public static string Upload_State_NeedsReview => Get(nameof(Upload_State_NeedsReview));
    public static string Upload_Failure_Unknown => Get(nameof(Upload_Failure_Unknown));
    public static string Upload_Failure_InvalidIsbn => Get(nameof(Upload_Failure_InvalidIsbn));
    public static string Upload_Failure_ImageRejected => Get(nameof(Upload_Failure_ImageRejected));
    public static string Upload_Failure_SaveFailed => Get(nameof(Upload_Failure_SaveFailed));
    public static string Upload_Failure_CatalogueFailed => Get(nameof(Upload_Failure_CatalogueFailed));
    public static string Browse_SearchPlaceholder => Get(nameof(Browse_SearchPlaceholder));
    public static string Browse_Scan => Get(nameof(Browse_Scan));
    public static string Browse_AllCollections => Get(nameof(Browse_AllCollections));
    public static string Browse_CollectionWithCount => Get(nameof(Browse_CollectionWithCount));
    public static string Browse_AuthorsAndYear => Get(nameof(Browse_AuthorsAndYear));
    public static string Browse_Untitled => Get(nameof(Browse_Untitled));
    public static string Browse_ResultCount => Get(nameof(Browse_ResultCount));
    public static string Browse_Empty => Get(nameof(Browse_Empty));
    public static string Browse_EmptyHint => Get(nameof(Browse_EmptyHint));
    public static string Browse_Offline => Get(nameof(Browse_Offline));
    public static string Browse_Retry => Get(nameof(Browse_Retry));
    public static string Browse_LoadMore => Get(nameof(Browse_LoadMore));
    public static string Browse_Owned => Get(nameof(Browse_Owned));
    public static string Browse_NotOwned => Get(nameof(Browse_NotOwned));
    public static string Browse_ScannedIsbn => Get(nameof(Browse_ScannedIsbn));
    public static string Browse_ClearScan => Get(nameof(Browse_ClearScan));
    public static string Browse_NothingRead => Get(nameof(Browse_NothingRead));
    public static string Browse_NotAnIsbn => Get(nameof(Browse_NotAnIsbn));
    public static string Detail_Field_Authors => Get(nameof(Detail_Field_Authors));
    public static string Detail_Field_Series => Get(nameof(Detail_Field_Series));
    public static string Detail_Field_Publisher => Get(nameof(Detail_Field_Publisher));
    public static string Detail_Field_Published => Get(nameof(Detail_Field_Published));
    public static string Detail_Field_Format => Get(nameof(Detail_Field_Format));
    public static string Detail_Field_Language => Get(nameof(Detail_Field_Language));
    public static string Detail_Field_Pages => Get(nameof(Detail_Field_Pages));
    public static string Detail_Field_Isbn => Get(nameof(Detail_Field_Isbn));
    public static string Detail_Field_Collection => Get(nameof(Detail_Field_Collection));
    public static string Detail_Field_Comments => Get(nameof(Detail_Field_Comments));
    public static string Detail_Photos => Get(nameof(Detail_Photos));
    public static string Detail_Missing => Get(nameof(Detail_Missing));
    public static string Detail_Offline => Get(nameof(Detail_Offline));
    public static string Detail_Retry => Get(nameof(Detail_Retry));
    public static string Detail_Loading => Get(nameof(Detail_Loading));

    // The library's own seeded words, carried here so a book's format and language read in the language of
    // the device holding it rather than the one the library was seeded in. Kept identical to the desktop's.
    public static string Format_Audiobook => Get(nameof(Format_Audiobook));
    public static string Format_Comic => Get(nameof(Format_Comic));
    public static string Format_Ebook => Get(nameof(Format_Ebook));
    public static string Format_GraphicNovel => Get(nameof(Format_GraphicNovel));
    public static string Format_Hardcover => Get(nameof(Format_Hardcover));
    public static string Format_Magazine => Get(nameof(Format_Magazine));
    public static string Format_MassMarketPaperback => Get(nameof(Format_MassMarketPaperback));
    public static string Format_Paperback => Get(nameof(Format_Paperback));
    public static string Format_TradePaperback => Get(nameof(Format_TradePaperback));
    public static string Language_Danish => Get(nameof(Language_Danish));
    public static string Language_Finnish => Get(nameof(Language_Finnish));
    public static string Language_French => Get(nameof(Language_French));
    public static string Language_German => Get(nameof(Language_German));
    public static string Language_Italian => Get(nameof(Language_Italian));
    public static string Language_Japanese => Get(nameof(Language_Japanese));
    public static string Language_Norwegian => Get(nameof(Language_Norwegian));
    public static string Language_Spanish => Get(nameof(Language_Spanish));
    public static string Language_Swedish => Get(nameof(Language_Swedish));

    public static string Camera_Rationale => Get(nameof(Camera_Rationale));
    public static string Camera_PermissionDenied => Get(nameof(Camera_PermissionDenied));
    public static string Camera_Unavailable => Get(nameof(Camera_Unavailable));
    public static string Camera_OpenSettings => Get(nameof(Camera_OpenSettings));
}
