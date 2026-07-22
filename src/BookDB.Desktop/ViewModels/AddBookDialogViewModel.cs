using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Desktop.Services;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BookDB.Desktop.ViewModels;

/// <summary>
/// The guided flow's manual stage (reached from the identify dialog and the Lookup Wizard's
/// manual hatch): title plus the shared author row-editor, ISBN carried over from identify,
/// year, and the collection default. "Save &amp; open editor" hands the saved book to the full
/// editor for anything beyond the quick fields.
/// </summary>
public partial class AddBookDialogViewModel : ObservableObject
{
    private readonly IBookService _bookService;
    private readonly ILookupService _lookupService;
    private readonly IWindowService _windowService;

    // Callback to close the dialog window with a result
    public Action<bool>? CloseDialog { get; set; }

    private int? _defaultCollectionId;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAndOpenEditorCommand))]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _isbn = string.Empty;

    [ObservableProperty]
    private string _year = string.Empty;

    [ObservableProperty]
    private int? _selectedCollectionId;

    public ObservableCollection<CollectionItem> Collections { get; } = [];

    /// <summary>Shared type-ahead provider for the author rows; snapshot loads in <see cref="InitializeAsync"/>.</summary>
    public PersonSuggestionProvider PersonSuggestions { get; } = new();

    /// <summary>Editable author rows — reuse-or-create by name on save, existing-vs-new visible per row.</summary>
    public ObservableCollection<PersonSuggestionRowViewModel> AuthorRows { get; } = [];

    private bool CanSave => !string.IsNullOrWhiteSpace(Title);

    public AddBookDialogViewModel(
        IBookService bookService,
        ILookupService lookupService,
        IWindowService windowService)
    {
        _bookService = bookService;
        _lookupService = lookupService;
        _windowService = windowService;
    }

    public async Task InitializeAsync()
    {
        try
        {
            var collections = await _lookupService.GetCollectionsAsync();
            Collections.Clear();
            foreach (var c in collections)
                Collections.Add(new CollectionItem(c.CollectionId, c.Name));

            SelectedCollectionId = _defaultCollectionId ?? Collections.FirstOrDefault()?.Id;

            PersonSuggestions.LoadSnapshot(await _bookService.GetPeopleAsync());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load data for Add Book dialog");
        }
    }

    [RelayCommand]
    private void AddAuthorRow()
        => AuthorRows.Add(new PersonSuggestionRowViewModel(PersonSuggestions));

    [RelayCommand]
    private void RemoveAuthorRow(PersonSuggestionRowViewModel row)
        => AuthorRows.Remove(row);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => SaveAndCloseAsync(openEditor: false);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAndOpenEditorAsync() => SaveAndCloseAsync(openEditor: true);

    private async Task SaveAndCloseAsync(bool openEditor)
    {
        int savedBookId;
        try
        {
            var book = new Book
            {
                Title = Title,
                Isbn = string.IsNullOrWhiteSpace(Isbn) ? null : Isbn,
                PubDate = string.IsNullOrWhiteSpace(Year) ? null : Year,
                CollectionId = SelectedCollectionId,
            };

            var authorNames = AuthorRows
                .Select(r => r.NameToPersist)
                .Where(name => name.Length > 0)
                .ToArray();
            var savedBook = await _bookService.AddBookWithContributorsAsync(book, authorNames);
            savedBookId = savedBook.BookId;

            CloseDialog?.Invoke(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save new book");
            return;
        }

        if (openEditor)
        {
            try
            {
                await _windowService.OpenFullDetailsWindowAsync(savedBookId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open the editor for book {BookId} after manual add", savedBookId);
            }
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseDialog?.Invoke(false);
    }

    public void Reset(int? defaultCollectionId = null)
    {
        _defaultCollectionId = defaultCollectionId;
        Title = string.Empty;
        Isbn = string.Empty;
        Year = string.Empty;
        AuthorRows.Clear();
        AuthorRows.Add(new PersonSuggestionRowViewModel(PersonSuggestions));
    }
}
