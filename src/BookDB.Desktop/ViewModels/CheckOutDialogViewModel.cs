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
    private IReadOnlyList<Borrower> _allBorrowers = [];

    public Action<bool?>? CloseDialog { get; set; }

    // AsyncPopulator lets Avalonia manage the items collection — avoids the
    // ArgumentOutOfRangeException caused by calling Clear() while the dropdown
    // SelectionModel still holds an index into the old list. The populator filters an in-memory snapshot
    // loaded once at open: a per-keystroke remote query would complete after later keystrokes and reset the
    // box to the stale (shorter) search text, dropping characters on a high-latency backend.
    public Func<string, CancellationToken, Task<IEnumerable<object?>?>> BorrowerPopulator { get; }

    [ObservableProperty]
    private DateTimeOffset? _dueDate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    public CheckOutDialogViewModel(ILoanService loanService, IBorrowerService borrowerService)
    {
        _loanService = loanService;
        _borrowerService = borrowerService;
        BorrowerPopulator = PopulateBorrowersAsync;
    }

    // The dropdown only suggests; it is intentionally not bound to a SelectedItem. Picking a row just fills
    // the text box (via ValueMemberBinding), so the typed text is the single source of truth — which avoids
    // the AutoCompleteBox feedback loop where auto-selecting the "add new" suggestion (its value equals the
    // input) and then clearing that stale selection on the next keystroke reset the box to the prior text.
    private Task<IEnumerable<object?>?> PopulateBorrowersAsync(string text, CancellationToken ct)
    {
        if (text.Length < 1) return Task.FromResult<IEnumerable<object?>?>(null);

        var matches = _allBorrowers
            .Where(b =>
                (b.FirstName + " " + b.LastName).Contains(text, StringComparison.OrdinalIgnoreCase) ||
                (b.LastName + ", " + b.FirstName).Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(b => b.LastName).ThenBy(b => b.FirstName)
            .Take(20)
            .ToList();

        bool exactMatch = matches.Any(b =>
            string.Equals((b.FirstName + " " + b.LastName).Trim(), text, StringComparison.OrdinalIgnoreCase));

        var suggestions = new List<IBorrowerSuggestion>();
        foreach (var b in matches)
            suggestions.Add(new ExistingBorrowerSuggestion(b));
        if (!exactMatch)
            suggestions.Add(new NewBorrowerSuggestion(text));

        return Task.FromResult<IEnumerable<object?>?>(suggestions.Cast<object?>());
    }

    public async Task InitializeAsync(int bookId)
    {
        _bookId = bookId;
        DueDate = DateTimeOffset.Now;
        SearchText = string.Empty;
        StatusMessage = null;
        _allBorrowers = await _borrowerService.GetAllAsync();
    }

    private bool CanConfirm() => !string.IsNullOrWhiteSpace(SearchText);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        var name = SearchText.Trim();
        if (name.Length == 0) return;
        try
        {
            // Text is the source of truth: an exact (case-insensitive) name match reuses that borrower,
            // anything else is created as a new one.
            var existing = _allBorrowers.FirstOrDefault(b =>
                string.Equals((b.FirstName + " " + b.LastName).Trim(), name, StringComparison.OrdinalIgnoreCase));
            var borrowerId = existing?.BorrowerId
                ?? (await _borrowerService.CreateAsync(name)).BorrowerId;

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
