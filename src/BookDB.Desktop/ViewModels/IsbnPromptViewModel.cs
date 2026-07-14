using System;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Desktop.ViewModels;

public sealed partial class IsbnPromptViewModel
{
    public IsbnPromptViewModel(string bookTitle)
    {
        Body = string.Format(Localization.Resources.Recatalog_NoIsbn_BodyForBook, bookTitle);
    }

    /// <summary>Names the book being re-cataloged — in a bulk run this is what tells the prompts apart.</summary>
    public string Body { get; }

    public string Isbn { get; set; } = string.Empty;

    // Set by the show path before the dialog opens; receives the entered ISBN, or null when dismissed
    // or left empty.
    public Action<string?>? CloseDialog { get; set; }

    [RelayCommand]
    private void LookUp()
    {
        var isbn = Isbn.Trim();
        CloseDialog?.Invoke(isbn.Length == 0 ? null : isbn);
    }

    [RelayCommand]
    private void Cancel() => CloseDialog?.Invoke(null);
}
