using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BookDB.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Desktop.ViewModels;

public sealed partial class MergeTargetPickerViewModel : ObservableObject
{
    public Action<int?>? CloseDialog { get; set; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyPropertyChangedFor(nameof(ConfirmHint))]
    private LookupEntryRow? _selectedTarget;

    public ObservableCollection<LookupEntryRow> Candidates { get; } = [];

    public string SourceName { get; private set; } = string.Empty;

    public string ConfirmHint =>
        SelectedTarget is null
            ? string.Empty
            : string.Format(Resources.ManageLookups_MergePickerConfirmHint, SourceName, SelectedTarget.Name);

    public void Initialize(string sourceName, IReadOnlyList<LookupEntryRow> candidates, int sourceId)
    {
        SourceName = sourceName;
        Title = string.Format(Resources.ManageLookups_MergePickerTitle, sourceName);
        Candidates.Clear();
        foreach (var c in candidates.Where(c => c.Id != sourceId).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            Candidates.Add(c);
        SelectedTarget = null;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ConfirmHint));
    }

    private bool CanConfirm() => SelectedTarget is not null;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => CloseDialog?.Invoke(SelectedTarget!.Id);

    [RelayCommand]
    private void Cancel() => CloseDialog?.Invoke(null);
}
