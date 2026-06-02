using System;
using System.Threading.Tasks;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BookDB.Desktop.ViewModels;

/// <summary>
/// Editor sub-ViewModel for BorrowerManagementViewModel.
/// Exposes all Borrower fields as ObservableProperties + Save/Discard commands.
/// Constructed directly by ManageBorrowersViewModel — not registered in DI.
/// </summary>
public partial class BorrowerEditorViewModel : ObservableObject
{
    private readonly IBorrowerService _borrowerService;
    private int _editingBorrowerId;
    private Borrower? _originalBorrower;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _firstName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _lastName = string.Empty;

    [ObservableProperty]
    private string? _organization;

    [ObservableProperty]
    private string? _phone1;

    [ObservableProperty]
    private string? _phone2;

    [ObservableProperty]
    private string? _email;

    [ObservableProperty]
    private string? _fax;

    [ObservableProperty]
    private string? _address1;

    [ObservableProperty]
    private string? _address2;

    [ObservableProperty]
    private string? _city;

    [ObservableProperty]
    private string? _state;

    [ObservableProperty]
    private string? _postalCode;

    [ObservableProperty]
    private string? _country;

    [ObservableProperty]
    private int _borrowerStatusId;

    [ObservableProperty]
    private string? _statusMessage;

    public bool HasBorrower => _editingBorrowerId > 0;

    /// <summary>Invoked after a successful save, passing the saved BorrowerId so the parent VM can refresh.</summary>
    public Func<int, Task>? BorrowerSaved { get; set; }

    public BorrowerEditorViewModel(IBorrowerService borrowerService)
    {
        _borrowerService = borrowerService;
    }

    public void LoadBorrower(Borrower b)
    {
        _originalBorrower = b;
        _editingBorrowerId = b.BorrowerId;
        FirstName = b.FirstName ?? string.Empty;
        LastName = b.LastName ?? string.Empty;
        Organization = b.Organization;
        Phone1 = b.Phone1;
        Phone2 = b.Phone2;
        Email = b.Email;
        Fax = b.Fax;
        Address1 = b.Address1;
        Address2 = b.Address2;
        City = b.City;
        State = b.State;
        PostalCode = b.PostalCode;
        Country = b.Country;
        BorrowerStatusId = b.StatusId;
        StatusMessage = null;
        OnPropertyChanged(nameof(HasBorrower));
    }

    public void Clear()
    {
        _editingBorrowerId = 0;
        FirstName = string.Empty;
        LastName = string.Empty;
        Organization = null;
        Phone1 = null;
        Phone2 = null;
        Email = null;
        Fax = null;
        Address1 = null;
        Address2 = null;
        City = null;
        State = null;
        PostalCode = null;
        Country = null;
        BorrowerStatusId = 0;
        StatusMessage = null;
        OnPropertyChanged(nameof(HasBorrower));
    }

    private bool CanSave() =>
        !string.IsNullOrWhiteSpace(FirstName) || !string.IsNullOrWhiteSpace(LastName);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        try
        {
            var borrower = new Borrower
            {
                BorrowerId = _editingBorrowerId,
                FirstName = FirstName,
                LastName = LastName,
                Organization = Organization,
                Phone1 = Phone1,
                Phone2 = Phone2,
                Email = Email,
                Fax = Fax,
                Address1 = Address1,
                Address2 = Address2,
                City = City,
                State = State,
                PostalCode = PostalCode,
                Country = Country,
                StatusId = BorrowerStatusId,
            };
            await _borrowerService.SaveAsync(borrower);
            if (BorrowerSaved is not null)
                await BorrowerSaved(borrower.BorrowerId);
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            Log.Error(ex, "BorrowerEditorViewModel: save failed");
        }
    }

    [RelayCommand]
    private void CancelChanges()
    {
        if (_originalBorrower is not null)
            LoadBorrower(_originalBorrower);
        else
            StatusMessage = null;
    }
}
