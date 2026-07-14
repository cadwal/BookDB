using System;
using BookDB.Desktop.Services;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Desktop.ViewModels;

public sealed partial class RestoreTargetViewModel
{
    public string Body { get; }

    // Set by the show path before the dialog opens; receives the chosen restore target.
    public Action<RestoreTargetChoice>? CloseDialog { get; set; }

    public RestoreTargetViewModel(string archivedServerDescription)
    {
        Body = string.Format(Localization.Resources.RestoreTarget_Body, archivedServerDescription);
    }

    [RelayCommand]
    private void ChooseArchived() => CloseDialog?.Invoke(RestoreTargetChoice.Archived);

    [RelayCommand]
    private void ChooseCurrent() => CloseDialog?.Invoke(RestoreTargetChoice.Current);

    [RelayCommand]
    private void Cancel() => CloseDialog?.Invoke(RestoreTargetChoice.Cancel);
}
