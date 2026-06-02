using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Desktop.Localization;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;

namespace BookDB.Desktop.ViewModels;

public sealed partial class ManageBorrowersViewModel : ObservableObject
{
    private readonly IBorrowerService _borrowerService;
    private readonly IMessenger _messenger;

    public Action? CloseWindow { get; set; }

    public ObservableCollection<Borrower> AllBorrowers { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredBorrowers))]
    private string _filterText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private Borrower? _selectedBorrower;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private int _addFocusSequence;

    public bool HasSelection => SelectedBorrower is not null;

    public IEnumerable<Borrower> FilteredBorrowers =>
        string.IsNullOrWhiteSpace(FilterText)
            ? AllBorrowers
            : AllBorrowers.Where(b =>
                (b.FirstName + " " + b.LastName).Contains(FilterText, StringComparison.OrdinalIgnoreCase)
                || (b.Organization ?? string.Empty).Contains(FilterText, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Editor sub-VM — constructed directly (not via DI).
    /// </summary>
    public BorrowerEditorViewModel Editor { get; }

    public ManageBorrowersViewModel(IBorrowerService borrowerService, IMessenger messenger)
    {
        _borrowerService = borrowerService;
        _messenger = messenger;
        Editor = new BorrowerEditorViewModel(borrowerService);
        Editor.BorrowerSaved = async id =>
        {
            await LoadBorrowersAsync();
            SelectedBorrower = AllBorrowers.FirstOrDefault(b => b.BorrowerId == id);
            OnPropertyChanged(nameof(FilteredBorrowers));
        };
    }

    public async Task InitializeAsync()
    {
        try
        {
            await LoadBorrowersAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ManageBorrowersViewModel: LoadBorrowersAsync failed");
        }
    }

    private async Task LoadBorrowersAsync()
    {
        var all = await _borrowerService.GetAllAsync();
        AllBorrowers.Clear();
        foreach (var b in all)
            AllBorrowers.Add(b);
    }

    partial void OnSelectedBorrowerChanged(Borrower? value)
    {
        if (value is not null)
            Editor.LoadBorrower(value);
        else
            Editor.Clear();
    }

    /// <summary>
    /// Creates an in-memory stub borrower (BorrowerId=0) and selects it for editing.
    /// No DB record is written until the user saves via the editor (SaveAsync handles INSERT).
    /// CanSave on the editor requires FirstName or LastName to be non-empty, so no
    /// blank record ever reaches the database.
    /// </summary>
    [RelayCommand]
    private void Add()
    {
        var stub = new Borrower { BorrowerId = 0, FirstName = string.Empty, StatusId = 0 };
        AllBorrowers.Add(stub);
        SelectedBorrower = stub;
        StatusMessage = null;
        AddFocusSequence++;
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedBorrower is null) return;
        try
        {
            await _borrowerService.DeleteAsync(SelectedBorrower.BorrowerId);
            AllBorrowers.Remove(SelectedBorrower);
            SelectedBorrower = null;
            StatusMessage = null;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("loan history"))
        {
            StatusMessage = Resources.ManageBorrowers_DeleteBlocked;
            Log.Error(ex, "Delete borrower blocked by FK constraint");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            Log.Error(ex, "ManageBorrowersViewModel: DeleteAsync failed");
        }
    }

    [RelayCommand]
    private void Close() => CloseWindow?.Invoke();
}
