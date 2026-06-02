using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Messages;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;

namespace BookDB.Desktop.ViewModels;

// ---------------------------------------------------------------------------
// BulkEditFieldOption — enum-backed ComboBox item for the field selector
// ---------------------------------------------------------------------------

public sealed record BulkEditFieldOption(BulkEditField Field, string Label)
{
    public override string ToString() => Label;
}

// ---------------------------------------------------------------------------
// BulkEditViewModel
// ---------------------------------------------------------------------------

public partial class BulkEditViewModel : ObservableObject
{
    private readonly IBookService _bookService;
    private readonly ILookupService _lookupService;

    private IReadOnlyList<int> _bookIds = Array.Empty<int>();

    public int BookCount => _bookIds.Count;

    public IReadOnlyList<BulkEditFieldOption> EditableFields { get; } =
    [
        new(BulkEditField.Status,   Resources.BulkEditField_Status),
        new(BulkEditField.Location, Resources.BulkEditField_Location),
        new(BulkEditField.Rating,   Resources.BulkEditField_Rating),
        new(BulkEditField.Format,   Resources.BulkEditField_Format),
        new(BulkEditField.Language, Resources.BulkEditField_Language),
        new(BulkEditField.Owner,    Resources.BulkEditField_Owner),
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private BulkEditFieldOption? _selectedField;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private LookupItem? _selectedValue;

    public ObservableCollection<LookupItem> Values { get; } = [];

    public string ApplyButtonLabel =>
        string.Format(Resources.BulkEdit_Apply_Button, BookCount);

    public Action<bool?>? CloseDialog { get; set; }

    public BulkEditViewModel(IBookService bookService, ILookupService lookupService)
    {
        _bookService = bookService;
        _lookupService = lookupService;
    }

    public async Task InitializeAsync(IReadOnlyList<int> bookIds)
    {
        _bookIds = bookIds;
        OnPropertyChanged(nameof(BookCount));
        OnPropertyChanged(nameof(ApplyButtonLabel));

        SelectedField = null;
        SelectedValue = null;
        Values.Clear();
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (SelectedValue == null || SelectedField == null) return;
        try
        {
            var id = SelectedValue.Id;
            switch (SelectedField.Field)
            {
                case BulkEditField.Status:
                    await _bookService.BulkSetStatusAsync(_bookIds, id);
                    break;
                case BulkEditField.Location:
                    await _bookService.BulkSetLocationAsync(_bookIds, id);
                    break;
                case BulkEditField.Rating:
                    await _bookService.BulkSetRatingAsync(_bookIds, id);
                    break;
                case BulkEditField.Format:
                    await _bookService.BulkSetFormatAsync(_bookIds, id);
                    break;
                case BulkEditField.Language:
                    await _bookService.BulkSetLanguageAsync(_bookIds, id);
                    break;
                case BulkEditField.Owner:
                    await _bookService.BulkSetOwnerAsync(_bookIds, id);
                    break;
            }
            foreach (var bookId in _bookIds)
                WeakReferenceMessenger.Default.Send(new BookSavedMessage(bookId));
            CloseDialog?.Invoke(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "BulkEditViewModel: Failed to apply bulk edit for field {Field}", SelectedField?.Field);
        }
    }

    private bool CanApply() => SelectedField != null && SelectedValue != null;

    [RelayCommand]
    private void Cancel() => CloseDialog?.Invoke(false);

    partial void OnSelectedFieldChanged(BulkEditFieldOption? value)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await LoadValuesForFieldAsync(value);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BulkEditViewModel: Failed to load values for field {Field}", value?.Field);
            }
        });
    }

    private async Task LoadValuesForFieldAsync(BulkEditFieldOption? option)
    {
        Values.Clear();
        SelectedValue = null;

        if (option == null) return;

        IEnumerable<LookupItem> items = option.Field switch
        {
            BulkEditField.Status   => (await _lookupService.GetAllAsync<Status>())
                                       .Select(s => new LookupItem(s.StatusId, s.Name)),
            BulkEditField.Location => (await _lookupService.GetAllAsync<Location>())
                                       .Select(l => new LookupItem(l.LocationId, l.Name)),
            BulkEditField.Rating   => (await _lookupService.GetAllAsync<Rating>())
                                       .Select(r => new LookupItem(r.RatingId, r.Name)),
            BulkEditField.Format   => (await _lookupService.GetAllAsync<Format>())
                                       .Select(f => new LookupItem(f.FormatId, f.Name)),
            BulkEditField.Language => (await _lookupService.GetAllAsync<Language>())
                                       .Select(l => new LookupItem(l.LanguageId, l.Name)),
            BulkEditField.Owner    => (await _lookupService.GetAllAsync<Owner>())
                                       .Select(o => new LookupItem(o.OwnerId, o.Name)),
            _                      => Enumerable.Empty<LookupItem>()
        };

        foreach (var item in items)
            Values.Add(item);
    }
}

public record LookupItem(int Id, string Name);
