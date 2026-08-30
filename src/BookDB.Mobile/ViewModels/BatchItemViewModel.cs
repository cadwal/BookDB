using System;
using System.Globalization;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Staging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Mobile.ViewModels;

/// <summary>One row of the batch tray: the cover, enough of the number to recognise the book by, and how many
/// photos it carries. Removing is a two-tap affair — the photos are the only copy.</summary>
public sealed partial class BatchItemViewModel : ViewModelBase
{
    /// <summary>Enough digits to tell two books apart at a glance without making the row about the number.</summary>
    private const int TailLength = 6;

    private readonly Action<StagedItem> _onEdit;
    private readonly Action<StagedItem> _onRemove;

    public BatchItemViewModel(StagedItem item, Action<StagedItem> onEdit, Action<StagedItem> onRemove)
    {
        Item = item;
        _onEdit = onEdit;
        _onRemove = onRemove;
    }

    public StagedItem Item { get; }

    public byte[]? Thumbnail => Item.Thumbnail;

    public string IsbnTail => Item.Isbn.Length > TailLength
        ? string.Format(CultureInfo.CurrentCulture, Resources.Batch_IsbnTail, Item.Isbn[^TailLength..])
        : Item.Isbn;

    public string PhotoCount => Item.ImageTypes.Count == 1
        ? Resources.Batch_PhotoCountOne
        : string.Format(CultureInfo.CurrentCulture, Resources.Batch_PhotoCount, Item.ImageTypes.Count);

    [ObservableProperty]
    private bool _isConfirmingRemove;

    [RelayCommand]
    private void Edit() => _onEdit(Item);

    [RelayCommand]
    private void Remove() => IsConfirmingRemove = true;

    [RelayCommand]
    private void CancelRemove() => IsConfirmingRemove = false;

    [RelayCommand]
    private void ConfirmRemove() => _onRemove(Item);
}
