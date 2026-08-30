using System;
using System.Globalization;
using BookDB.Contracts;
using BookDB.Mobile.Localization;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Mobile.ViewModels;

/// <summary>One book in the browse list: the cover the desktop already shrank, the title, and whatever of
/// author and year there is to say underneath.</summary>
public sealed partial class BrowseRowViewModel : ViewModelBase
{
    private readonly Action<int, string> _onOpen;

    public BrowseRowViewModel(BookSummary book, Action<int, string> onOpen)
    {
        Book = book;
        _onOpen = onOpen;
    }

    public BookSummary Book { get; }

    public int BookId => Book.BookId;

    public string Title => string.IsNullOrWhiteSpace(Book.Title) ? Resources.Browse_Untitled : Book.Title;

    public byte[]? Thumbnail => Book.Thumbnail;

    /// <summary>Author and year, whichever of them the book has; empty when it has neither.</summary>
    public string Subline => (string.IsNullOrWhiteSpace(Book.Authors), Book.Year) switch
    {
        (false, { } year) => string.Format(
            CultureInfo.CurrentCulture, Resources.Browse_AuthorsAndYear, Book.Authors, year),
        (false, null) => Book.Authors!,
        (true, { } year) => year.ToString(CultureInfo.CurrentCulture),
        _ => string.Empty,
    };

    public bool HasSubline => Subline.Length > 0;

    /// <summary>The row's title travels with the id so the detail bar reads correctly before the detail
    /// itself has arrived.</summary>
    [RelayCommand]
    private void Open() => _onOpen(BookId, Title);
}
