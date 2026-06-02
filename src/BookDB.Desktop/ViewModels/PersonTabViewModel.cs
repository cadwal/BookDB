using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Messages;
using BookDB.Desktop.Services;
using BookDB.Logic.Helpers;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;

namespace BookDB.Desktop.ViewModels;

public partial class PersonTabViewModel : ObservableObject
{
    protected ILookupManagementService Service { get; }
    protected ILookupService LookupService { get; }
    protected IWindowService WindowService { get; }
    private readonly IMessenger _messenger;
    private bool _hasAuthorRole;

    public ObservableCollection<PersonRow> Persons { get; } = [];
    public ObservableCollection<SuspectedDuplicatePair> SuspectedDuplicates { get; } = [];
    public ObservableCollection<CleanupProposalRow> CleanupProposals { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredPersons))]
    private string _filterText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(IsEditorVisible))]
    [NotifyPropertyChangedFor(nameof(IsPlaceholderVisible))]
    [NotifyPropertyChangedFor(nameof(IsBioSectionVisible))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(MergePersonCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenDataCleanupCommand))]
    [NotifyCanExecuteChangedFor(nameof(FilterToAuthorCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveBioCommand))]
    private PersonRow? _selectedPerson;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _editDisplayName = string.Empty;

    [ObservableProperty]
    private string _editSortName = string.Empty;

    [ObservableProperty]
    private string _suggestedSortName = string.Empty;

    [ObservableProperty]
    private bool _hasSortNameSuggestion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private int _usedByCount;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private int _addFocusSequence;

    [ObservableProperty]
    private string? _editBio;

    [ObservableProperty]
    private string? _editBirthDate;

    [ObservableProperty]
    private string? _editBirthPlace;

    [ObservableProperty]
    private string? _editDeathDate;

    [ObservableProperty]
    private string? _editDeathPlace;

    [ObservableProperty]
    private string? _editWebsite;

    [ObservableProperty]
    private bool _isScanningDuplicates;

    // --- Merge panel state (State C) ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorVisible))]
    [NotifyPropertyChangedFor(nameof(IsMergePanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsPlaceholderVisible))]
    [NotifyCanExecuteChangedFor(nameof(OpenDataCleanupCommand))]
    private bool _isMergePanelOpen;

    [ObservableProperty]
    private PersonRow? _mergeSource;

    [ObservableProperty]
    private PersonRow? _mergeTarget;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmMergeCommand))]
    [NotifyPropertyChangedFor(nameof(IsSourceCanonical))]
    [NotifyPropertyChangedFor(nameof(IsTargetCanonical))]
    private PersonRow? _canonicalPerson;

    public bool IsSourceCanonical => CanonicalPerson is not null && MergeSource is not null && CanonicalPerson.PersonId == MergeSource.PersonId;
    public bool IsTargetCanonical => CanonicalPerson is not null && MergeTarget is not null && CanonicalPerson.PersonId == MergeTarget.PersonId;

    // --- Cleanup panel state (State D) ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorVisible))]
    [NotifyPropertyChangedFor(nameof(IsCleanupPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsPlaceholderVisible))]
    private bool _isCleanupPanelOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCleanupCommand))]
    private int _checkedProposalCount;

    public bool HasSelection => SelectedPerson is not null;
    public bool IsPlaceholderVisible => !HasSelection && !IsMergePanelOpen && !IsCleanupPanelOpen;
    public bool IsEditorVisible => HasSelection && !IsMergePanelOpen && !IsCleanupPanelOpen;
    public bool IsMergePanelVisible => IsMergePanelOpen && !IsCleanupPanelOpen;
    public bool IsCleanupPanelVisible => IsCleanupPanelOpen;
    public bool HasSuspectedDuplicates => SuspectedDuplicates.Count > 0;
    public bool HasCheckedProposals => CheckedProposalCount > 0;
    public bool HasNoCleanup => IsCleanupPanelOpen && CleanupProposals.Count == 0;
    public bool IsBioSectionVisible => SelectedPerson is { PersonId: > 0 };

    public IEnumerable<PersonRow> FilteredPersons =>
        string.IsNullOrWhiteSpace(FilterText)
            ? Persons
            : Persons.Where(p =>
                (p.DisplayName?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) == true) ||
                (p.SortName?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) == true));

    public PersonTabViewModel(
        ILookupManagementService service,
        ILookupService lookupService,
        IWindowService windowService,
        IMessenger messenger)
    {
        Service = service;
        LookupService = lookupService;
        WindowService = windowService;
        _messenger = messenger;
    }

    public virtual async Task LoadAsync()
    {
        try
        {
            Persons.Clear();
            var items = await LookupService.GetAllAsync<Person>();
            foreach (var p in items.OrderBy(p => p.SortName, StringComparer.OrdinalIgnoreCase))
                Persons.Add(new PersonRow(p.PersonId, p.DisplayName, p.SortName));
            OnPropertyChanged(nameof(FilteredPersons));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PersonTabViewModel: LoadAsync failed");
        }
        await LoadSuspectedDuplicatesAsync();
    }

    private async Task LoadSuspectedDuplicatesAsync()
    {
        try
        {
            IsScanningDuplicates = true;
            var snapshot = Persons.ToList();
            List<SuspectedDuplicatePair> pairs;
            if (snapshot.Count > 500)
            {
                pairs = await Task.Run(() => ScanPairs(snapshot));
            }
            else
            {
                pairs = ScanPairs(snapshot);
            }
            SuspectedDuplicates.Clear();
            foreach (var p in pairs) SuspectedDuplicates.Add(p);
            OnPropertyChanged(nameof(HasSuspectedDuplicates));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PersonTabViewModel: LoadSuspectedDuplicatesAsync failed");
        }
        finally
        {
            IsScanningDuplicates = false;
        }
    }

    private static List<SuspectedDuplicatePair> ScanPairs(List<PersonRow> snapshot)
    {
        var result = new List<SuspectedDuplicatePair>();
        for (int i = 0; i < snapshot.Count; i++)
        {
            for (int j = i + 1; j < snapshot.Count; j++)
            {
                var a = snapshot[i];
                var b = snapshot[j];
                if (string.Equals(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (StringSimilarityHelper.IsSuspectedDuplicate(a.DisplayName, b.DisplayName))
                    result.Add(new SuspectedDuplicatePair(a, b));
            }
        }
        return result;
    }

    partial void OnSelectedPersonChanged(PersonRow? value)
    {
        StatusMessage = null;
        if (value is null)
        {
            EditDisplayName = string.Empty;
            EditSortName = string.Empty;
            SuggestedSortName = string.Empty;
            HasSortNameSuggestion = false;
            UsedByCount = 0;
            _hasAuthorRole = false;
            FilterToAuthorCommand.NotifyCanExecuteChanged();
            EditBio = null;
            EditBirthDate = null;
            EditBirthPlace = null;
            EditDeathDate = null;
            EditDeathPlace = null;
            EditWebsite = null;
            return;
        }
        EditDisplayName = value.DisplayName;
        EditSortName = value.SortName;
        SuggestedSortName = string.Empty;
        HasSortNameSuggestion = false;
        _ = LoadPersonUsageAsync(value);
        _ = LoadPersonBioAsync(value);
        _ = LoadPersonAuthorRoleAsync(value);
    }

    partial void OnEditDisplayNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            SuggestedSortName = string.Empty;
            HasSortNameSuggestion = false;
            return;
        }
        var suggestion = PersonNameHelper.DeriveSortName(value);
        SuggestedSortName = suggestion;
        HasSortNameSuggestion = !string.Equals(suggestion, EditSortName, StringComparison.Ordinal);
    }

    partial void OnEditSortNameChanged(string value)
    {
        if (!string.IsNullOrEmpty(SuggestedSortName))
            HasSortNameSuggestion = !string.Equals(SuggestedSortName, value, StringComparison.Ordinal);
    }

    private async Task LoadPersonUsageAsync(PersonRow row)
    {
        try
        {
            var count = row.PersonId > 0 ? await Service.GetPersonBookContributionCountAsync(row.PersonId) : 0;
            if (!ReferenceEquals(SelectedPerson, row)) return;
            UsedByCount = count;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PersonTabViewModel: usage count failed for {Id}", row.PersonId);
            UsedByCount = 0;
        }
    }

    private async Task LoadPersonBioAsync(PersonRow row)
    {
        try
        {
            if (row.PersonId <= 0) return;
            var bio = await Service.GetPersonBioAsync(row.PersonId);
            if (bio is null) return;
            if (!ReferenceEquals(SelectedPerson, row)) return;
            row.Bio        = bio.Bio;
            row.BirthDate  = bio.BirthDate;
            row.BirthPlace = bio.BirthPlace;
            row.DeathDate  = bio.DeathDate;
            row.DeathPlace = bio.DeathPlace;
            row.Website    = bio.Website;
            EditBio        = bio.Bio;
            EditBirthDate  = bio.BirthDate;
            EditBirthPlace = bio.BirthPlace;
            EditDeathDate  = bio.DeathDate;
            EditDeathPlace = bio.DeathPlace;
            EditWebsite    = bio.Website;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PersonTabViewModel: bio load failed for {Id}", row.PersonId);
            // bio properties remain null — section stays hidden
        }
    }

    // --- Add / Save / Cancel / Delete ---

    [RelayCommand]
    private void Add()
    {
        var transient = new PersonRow(0, string.Empty, string.Empty);
        Persons.Add(transient);
        SelectedPerson = transient;
        EditDisplayName = string.Empty;
        EditSortName = string.Empty;
        AddFocusSequence++;
    }

    private bool CanSave() => SelectedPerson is not null && !string.IsNullOrWhiteSpace(EditDisplayName);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (SelectedPerson is null) return;
        var display = EditDisplayName?.Trim() ?? string.Empty;
        var sort = string.IsNullOrWhiteSpace(EditSortName) ? display : EditSortName.Trim();
        if (string.IsNullOrWhiteSpace(display))
        {
            StatusMessage = Resources.ManageLookups_ErrorEmptyName;
            return;
        }
        try
        {
            if (SelectedPerson.PersonId == 0)
            {
                var newId = await Service.AddPersonAsync(display, sort);
                await LoadAsync();
                SelectedPerson = Persons.FirstOrDefault(p => p.PersonId == newId);
            }
            else
            {
                await Service.UpdatePersonAsync(SelectedPerson.PersonId, display, sort);
                SelectedPerson.DisplayName = display;
                SelectedPerson.SortName = sort;
                var snap = Persons.OrderBy(p => p.SortName, StringComparer.OrdinalIgnoreCase).ToList();
                Persons.Clear();
                foreach (var p in snap) Persons.Add(p);
                OnPropertyChanged(nameof(FilteredPersons));
                await LoadSuspectedDuplicatesAsync();
            }
            HasSortNameSuggestion = false;
            StatusMessage = null;
            _messenger.Send(new LookupsChangedMessage());
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.ManageLookups_ErrorSaveFailed;
            Log.Error(ex, "PersonTabViewModel: save failed");
        }
    }

    private bool CanSaveBio() => SelectedPerson is { PersonId: > 0 };

    [RelayCommand(CanExecute = nameof(CanSaveBio))]
    private async Task SaveBioAsync()
    {
        if (SelectedPerson is null || SelectedPerson.PersonId <= 0) return;
        try
        {
            await Service.UpdatePersonBioAsync(
                SelectedPerson.PersonId,
                EditBio, EditBirthDate, EditBirthPlace,
                EditDeathDate, EditDeathPlace, EditWebsite);
            SelectedPerson.Bio        = EditBio;
            SelectedPerson.BirthDate  = EditBirthDate;
            SelectedPerson.BirthPlace = EditBirthPlace;
            SelectedPerson.DeathDate  = EditDeathDate;
            SelectedPerson.DeathPlace = EditDeathPlace;
            SelectedPerson.Website    = EditWebsite;
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.Person_ErrorBioSaveFailed;
            Log.Error(ex, "PersonTabViewModel: bio save failed for {Id}", SelectedPerson.PersonId);
        }
    }

    private bool CanCancel() => SelectedPerson is not null;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (SelectedPerson is null) return;
        if (SelectedPerson.PersonId == 0)
        {
            Persons.Remove(SelectedPerson);
            SelectedPerson = null;
        }
        else
        {
            EditDisplayName = SelectedPerson.DisplayName;
            EditSortName = SelectedPerson.SortName;
            HasSortNameSuggestion = false;
        }
        StatusMessage = null;
    }

    private bool CanDelete() => SelectedPerson is { PersonId: > 0 } && UsedByCount == 0;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        if (SelectedPerson is null || SelectedPerson.PersonId == 0) return;
        var confirmMessage = string.Format(Resources.ManageLookups_DeleteConfirm, SelectedPerson.DisplayName);
        var confirmed = await WindowService.ShowDeleteConfirmationAsync(confirmMessage);
        if (confirmed != true) return;
        try
        {
            await Service.DeletePersonAsync(SelectedPerson.PersonId);
            Persons.Remove(SelectedPerson);
            SelectedPerson = null;
            OnPropertyChanged(nameof(FilteredPersons));
            await LoadSuspectedDuplicatesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.ManageLookups_ErrorDeleteFailed;
            Log.Error(ex, "PersonTabViewModel: delete failed");
        }
    }

    [RelayCommand]
    private void AcceptSortNameSuggestion()
    {
        if (string.IsNullOrEmpty(SuggestedSortName)) return;
        EditSortName = SuggestedSortName;
        HasSortNameSuggestion = false;
    }

    private async Task LoadPersonAuthorRoleAsync(PersonRow row)
    {
        try
        {
            if (row.PersonId <= 0)
            {
                _hasAuthorRole = false;
                FilterToAuthorCommand.NotifyCanExecuteChanged();
                return;
            }
            var hasRole = await Service.PersonHasAuthorRoleAsync(row.PersonId);
            if (!ReferenceEquals(SelectedPerson, row)) return;
            _hasAuthorRole = hasRole;
            FilterToAuthorCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PersonTabViewModel: author role check failed for {Id}", row.PersonId);
            _hasAuthorRole = false;
            FilterToAuthorCommand.NotifyCanExecuteChanged();
        }
    }

    // --- Filter to author ---

    private bool CanFilterToAuthor() => SelectedPerson is { PersonId: > 0 } && _hasAuthorRole;

    [RelayCommand(CanExecute = nameof(CanFilterToAuthor))]
    private void FilterToAuthor()
    {
        if (SelectedPerson is null) return;
        _messenger.Send(new FilterToAuthorMessage(SelectedPerson.PersonId));
    }

    // --- Merge flow ---

    private bool CanMergePerson() => SelectedPerson is { PersonId: > 0 } && !IsMergePanelOpen && !IsCleanupPanelOpen;

    [RelayCommand(CanExecute = nameof(CanMergePerson))]
    private async Task MergePersonAsync()
    {
        if (SelectedPerson is null || SelectedPerson.PersonId == 0) return;
        var source = SelectedPerson;
        var candidates = Persons
            .Where(p => p.PersonId != source.PersonId && p.PersonId > 0)
            .Select(p => new LookupEntryRow(p.PersonId, p.DisplayName))
            .ToList();
        var targetId = await WindowService.ShowMergeTargetPickerAsync(
            source.DisplayName,
            source.PersonId,
            candidates);
        if (targetId is null) return;
        var target = Persons.FirstOrDefault(p => p.PersonId == targetId.Value);
        if (target is null) return;
        MergeSource = source;
        MergeTarget = target;
        CanonicalPerson = null;
        IsMergePanelOpen = true;
    }

    [RelayCommand]
    private void SelectDuplicatePair(SuspectedDuplicatePair? pair)
    {
        if (pair is null) return;
        MergeSource = pair.Left;
        MergeTarget = pair.Right;
        CanonicalPerson = null;
        IsMergePanelOpen = true;
        IsCleanupPanelOpen = false;
    }

    [RelayCommand]
    private void SetAsCanonical(string? which)
    {
        if (which == "source") CanonicalPerson = MergeSource;
        else if (which == "target") CanonicalPerson = MergeTarget;
    }

    private bool CanConfirmMerge() => CanonicalPerson is not null && MergeSource is not null && MergeTarget is not null;

    [RelayCommand(CanExecute = nameof(CanConfirmMerge))]
    private async Task ConfirmMergeAsync()
    {
        if (CanonicalPerson is null || MergeSource is null || MergeTarget is null) return;
        var canonical = CanonicalPerson;
        var source = canonical.PersonId == MergeSource.PersonId ? MergeTarget : MergeSource;
        var target = canonical;
        try
        {
            await Service.MergePersonsAsync(source.PersonId, target.PersonId);
            IsMergePanelOpen = false;
            MergeSource = null;
            MergeTarget = null;
            CanonicalPerson = null;
            SelectedPerson = null;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.ManageLookups_ErrorMergeFailed;
            Log.Error(ex, "PersonTabViewModel: merge failed");
        }
    }

    [RelayCommand]
    private void CancelMerge()
    {
        IsMergePanelOpen = false;
        MergeSource = null;
        MergeTarget = null;
        CanonicalPerson = null;
    }

    // --- Data cleanup flow ---

    private bool CanOpenDataCleanup() => !IsMergePanelOpen && !IsCleanupPanelOpen;

    [RelayCommand(CanExecute = nameof(CanOpenDataCleanup))]
    private async Task OpenDataCleanupAsync()
    {
        try
        {
            var (renames, splits) = await Service.ScanPersonNameCleanupAsync();
            PopulateCleanupProposals(renames, splits);
            IsCleanupPanelOpen = true;
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.ManageLookups_ErrorCleanupScanFailed;
            Log.Error(ex, "PersonTabViewModel: cleanup scan failed");
        }
    }

    private void PopulateCleanupProposals(
        IReadOnlyList<CleanupProposal> renames,
        IReadOnlyList<SplitProposal> splits)
    {
        foreach (var row in CleanupProposals) row.PropertyChanged -= OnProposalPropertyChanged;
        CleanupProposals.Clear();

        // Populate rename rows
        foreach (var p in renames)
        {
            var row = new CleanupProposalRow
            {
                PersonId = p.PersonId,
                CurrentDisplayName = p.CurrentDisplayName,
                SuggestedSortName = p.SuggestedSortName,
                ApplyChecked = true
            };
            row.ProposedDisplayName = p.ProposedDisplayName; // set after construction (ObservableProperty)
            row.PropertyChanged += OnProposalPropertyChanged;
            CleanupProposals.Add(row);
        }

        // Populate split sub-rows (N rows per SplitProposal)
        foreach (var sp in splits)
        {
            var groupId = $"split:{sp.PersonId}";
            foreach (var fragment in sp.Fragments)
            {
                var row = new CleanupProposalRow
                {
                    PersonId = sp.PersonId,
                    CurrentDisplayName = sp.CurrentDisplayName,
                    SplitGroupId = groupId,
                    SuggestedSortName = fragment.SuggestedSortName,
                    ApplyChecked = true
                };
                row.ProposedDisplayName = fragment.ProposedDisplayName;
                row.PropertyChanged += OnProposalPropertyChanged;
                CleanupProposals.Add(row);
            }
        }

        RecountCheckedProposals();
        OnPropertyChanged(nameof(HasNoCleanup));
    }

    private void OnProposalPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CleanupProposalRow.ApplyChecked))
            RecountCheckedProposals();
    }

    private void RecountCheckedProposals()
    {
        CheckedProposalCount = CleanupProposals.Count(r => r.ApplyChecked);
        OnPropertyChanged(nameof(HasCheckedProposals));
    }

    private bool CanApplyCleanup() => CheckedProposalCount > 0;

    [RelayCommand(CanExecute = nameof(CanApplyCleanup))]
    private async Task ApplyCleanupAsync()
    {
        var checkedRows = CleanupProposals.Where(r => r.ApplyChecked).ToList();
        if (checkedRows.Count == 0) return;
        try
        {
            // Partition into rename rows and split groups
            var renameRows = checkedRows.Where(r => !r.IsSplitRow).ToList();
            var splitRows = checkedRows.Where(r => r.IsSplitRow).ToList();

            // Apply renames
            if (renameRows.Count > 0)
            {
                var toApply = renameRows
                    .Select(r => new CleanupProposal(r.PersonId, r.CurrentDisplayName, r.ProposedDisplayName, r.SuggestedSortName))
                    .ToList();
                await Service.ApplyPersonNameCleanupAsync(toApply);
            }

            // Apply splits — reconstruct SplitProposal objects grouped by SplitGroupId
            if (splitRows.Count > 0)
            {
                var splitProposals = splitRows
                    .GroupBy(r => r.SplitGroupId!)
                    .Select(g =>
                    {
                        var firstRow = g.First();
                        var fragments = g
                            .Select(r => new SplitFragment(r.ProposedDisplayName, r.SuggestedSortName))
                            .ToList();
                        return new SplitProposal(firstRow.PersonId, firstRow.CurrentDisplayName, fragments);
                    })
                    .ToList();
                await Service.ApplySplitProposalAsync(splitProposals);
            }

            // Refresh: reload person list + re-run scan
            await LoadAsync();
            var (renames, splits) = await Service.ScanPersonNameCleanupAsync();
            PopulateCleanupProposals(renames, splits);
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.ManageLookups_ErrorApplyCleanupFailed;
            Log.Error(ex, "PersonTabViewModel: apply cleanup failed");
        }
    }

    [RelayCommand]
    private void CloseDataCleanup()
    {
        foreach (var r in CleanupProposals) r.PropertyChanged -= OnProposalPropertyChanged;
        CleanupProposals.Clear();
        IsCleanupPanelOpen = false;
        CheckedProposalCount = 0;
    }

    [RelayCommand]
    private void SelectAllCleanup()
    {
        foreach (var row in CleanupProposals) row.ApplyChecked = true;
        RecountCheckedProposals();
    }

    [RelayCommand]
    private void DeselectAllCleanup()
    {
        foreach (var row in CleanupProposals) row.ApplyChecked = false;
        RecountCheckedProposals();
    }
}
