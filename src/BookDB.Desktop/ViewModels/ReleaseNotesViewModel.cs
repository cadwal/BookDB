using System;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Desktop.ViewModels;

public sealed partial class ReleaseNotesViewModel
{
    public string Title { get; }
    public string Markdown { get; }

    // Set by the show path before the window opens.
    public Action? CloseWindow { get; set; }

    public ReleaseNotesViewModel(string version, string markdown)
    {
        Title = string.Format(Localization.Resources.ReleaseNotes_WindowTitle, version);
        Markdown = markdown;
    }

    [RelayCommand]
    private void Close() => CloseWindow?.Invoke();
}
