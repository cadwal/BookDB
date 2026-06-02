using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Desktop.Localization;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BookDB.Desktop.ViewModels;

// ── Suggestion types ──────────────────────────────────────────────────────────

public interface IBorrowerSuggestion
{
    string DisplayText { get; }
    string ValueText  { get; }
}

public sealed record ExistingBorrowerSuggestion(Borrower Borrower) : IBorrowerSuggestion
{
    public string DisplayText => (Borrower.FirstName + " " + Borrower.LastName).Trim();
    public string ValueText   => DisplayText;
}

public sealed record NewBorrowerSuggestion(string InputName) : IBorrowerSuggestion
{
    public string DisplayText => string.Format(Resources.CheckOut_NewBorrower_Format, InputName);
    public string ValueText   => InputName;
}

// ── ViewModel ─────────────────────────────────────────────────────────────────

public partial class CheckOutDialogViewModel : ObservableObject
{
    private readonly ILoanService _loanService;
    private readonly IBorrowerService _borrowerService;

    private int _bookId;

    public Action<bool?>? CloseDialog { get; set; }

    // AsyncPopulator lets Avalonia manage the items collection — avoids the
    // ArgumentOutOfRangeException caused by calling Clear() while the dropdown
    // SelectionModel still holds an index into the old list.
    public Func<string, CancellationToken, Task<IEnumerable<object?>?>> BorrowerPopulator { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private IBorrowerSuggestion? _selectedBorrower;

    [ObservableProperty]
    private DateTimeOffset? _dueDate;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    public CheckOutDialogViewModel(ILoanService loanService, IBorrowerService borrowerService)
    {
        _loanService = loanService;
        _borrowerService = borrowerService;
        BorrowerPopulator = PopulateBorrowersAsync;
    }

    partial void OnSearchTextChanged(string value)
    {
        // When the user edits the text after having made a selection, clear the stale selection.
        if (SelectedBorrower is not null &&
            !string.Equals(value, SelectedBorrower.ValueText, StringComparison.OrdinalIgnoreCase))
        {
            SelectedBorrower = null;
        }
    }

    private async Task<IEnumerable<object?>?> PopulateBorrowersAsync(string text, CancellationToken ct)
    {
        if (text.Length < 1) return null;
        try
        {
            var results = await _borrowerService.SearchAsync(text);
            if (ct.IsCancellationRequested) return null;

            bool exactMatch = results.Any(b =>
                string.Equals((b.FirstName + " " + b.LastName).Trim(), text, StringComparison.OrdinalIgnoreCase));

            var suggestions = new List<IBorrowerSuggestion>();
            foreach (var b in results)
                suggestions.Add(new ExistingBorrowerSuggestion(b));
            if (!exactMatch)
                suggestions.Add(new NewBorrowerSuggestion(text));

            return suggestions.Cast<object?>();
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to populate borrower suggestions for text '{Text}'", text);
            return null;
        }
    }

    public async Task InitializeAsync(int bookId)
    {
        _bookId = bookId;
        SelectedBorrower = null;
        DueDate = DateTimeOffset.Now;
        SearchText = string.Empty;
        StatusMessage = null;
        await Task.CompletedTask;
    }

    private bool CanConfirm() => SelectedBorrower is not null;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        if (SelectedBorrower is null) return;
        try
        {
            int borrowerId;
            if (SelectedBorrower is NewBorrowerSuggestion newSug)
            {
                var created = await _borrowerService.CreateAsync(newSug.InputName, statusId: 0);
                borrowerId = created.BorrowerId;
            }
            else
            {
                borrowerId = ((ExistingBorrowerSuggestion)SelectedBorrower).Borrower.BorrowerId;
            }

            var dueDate = DueDate?.LocalDateTime;
            await _loanService.CheckOutAsync(_bookId, borrowerId, dueDate);
            CloseDialog?.Invoke(true);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            Log.Error(ex, "CheckOut failed");
        }
    }

    [RelayCommand]
    private void Cancel() => CloseDialog?.Invoke(false);
}
