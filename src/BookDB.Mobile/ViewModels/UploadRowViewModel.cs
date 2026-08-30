using System.Globalization;
using BookDB.Contracts;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Staging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BookDB.Mobile.ViewModels;

/// <summary>One book on its way across: the same cover and number the tray showed, plus whatever the computer
/// last said about it.</summary>
public sealed partial class UploadRowViewModel : ViewModelBase
{
    private const int TailLength = 6;

    public UploadRowViewModel(StagedItem item)
    {
        ClientItemId = item.ClientItemId;
        Thumbnail = item.Thumbnail;
        IsbnTail = item.Isbn.Length > TailLength
            ? string.Format(CultureInfo.CurrentCulture, Resources.Batch_IsbnTail, item.Isbn[^TailLength..])
            : item.Isbn;
    }

    public string ClientItemId { get; }

    public byte[]? Thumbnail { get; }

    public string IsbnTail { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(HasFailed))]
    private BatchItemState _state = BatchItemState.Unknown;

    /// <summary>The computer's own diagnostic wording. Shown under the translated line as an addendum, never
    /// in place of it.</summary>
    [ObservableProperty]
    private string? _detail;

    private BatchItemStatus _status = new();

    public string StateText => UploadStateText.For(_status);

    public bool HasFailed => State == BatchItemState.Failed;

    public void Apply(BatchItemStatus status)
    {
        _status = status;
        Detail = status.FailureDetail;
        State = status.State;

        // State only republishes the line when the state itself changed; a new title on the same state has
        // to be published too.
        OnPropertyChanged(nameof(StateText));
    }
}
