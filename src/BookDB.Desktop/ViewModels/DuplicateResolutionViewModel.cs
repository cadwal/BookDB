using System;
using BookDB.Logic.Import;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Desktop.ViewModels;

public sealed partial class DuplicateResolutionViewModel
{
    public string Title { get; }
    public string Body { get; }

    // Set by the show path before the dialog opens; receives the chosen resolution.
    public Action<ImportDuplicateResolution>? CloseDialog { get; set; }

    public DuplicateResolutionViewModel(string title, string body)
    {
        Title = title;
        Body = body;
    }

    [RelayCommand]
    private void Overwrite() => CloseDialog?.Invoke(ImportDuplicateResolution.Overwrite);

    [RelayCommand]
    private void OverwriteAll() => CloseDialog?.Invoke(ImportDuplicateResolution.OverwriteAll);

    [RelayCommand]
    private void Skip() => CloseDialog?.Invoke(ImportDuplicateResolution.Skip);

    [RelayCommand]
    private void SkipAll() => CloseDialog?.Invoke(ImportDuplicateResolution.SkipAll);

    [RelayCommand]
    private void CancelImport() => CloseDialog?.Invoke(ImportDuplicateResolution.CancelImport);
}
