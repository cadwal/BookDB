using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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

    // A merge launched from the cleanup panel's suspected-duplicate list should return to that panel when
    // aborted, not to the person list. Tracks that entry point across the merge commands the two paths share.
    private bool _returnToCleanupAfterMerge;

    // --- Cleanup panel state (State D) ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorVisible))]
    [NotifyPropertyChangedFor(nameof(IsCleanupPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsCleanupProposalsVisible))]
    [NotifyPropertyChangedFor(nameof(IsIgnoredListVisible))]
    [NotifyPropertyChangedFor(nameof(IsPlaceholderVisible))]
    // IsMergePanelVisible reads !IsCleanupPanelOpen, so closing cleanup to open the merge panel (from a duplicate
    // row) must re-evaluate it — otherwise both panels read hidden and the pane goes blank.
    [NotifyPropertyChangedFor(nameof(IsMergePanelVisible))]
    // CanOpenDataCleanup depends on this, so closing the panel must re-enable the entry button — otherwise a
    // CanExecute re-evaluation while the panel was open (e.g. selecting a person) leaves it stuck disabled.
    [NotifyCanExecuteChangedFor(nameof(OpenDataCleanupCommand))]
    private bool _isCleanupPanelOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCleanupCommand))]
    private int _checkedProposalCount;

    /// <summary>Rename/split proposals suppressed by a persisted ignore that currently re-derive identically.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IgnoredCount))]
    [NotifyPropertyChangedFor(nameof(HasIgnored))]
    [NotifyPropertyChangedFor(nameof(IgnoredCountText))]
    private int _nameIgnoredCount;

    /// <summary>Suspected-duplicate pairs currently suppressed by a persisted duplicate ignore.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IgnoredCount))]
    [NotifyPropertyChangedFor(nameof(HasIgnored))]
    [NotifyPropertyChangedFor(nameof(IgnoredCountText))]
    private int _duplicateIgnoredCount;

    public int IgnoredCount => NameIgnoredCount + DuplicateIgnoredCount;

    public string IgnoredCountText =>
        string.Format(CultureInfo.CurrentCulture, Resources.Person_Cleanup_IgnoredCount, IgnoredCount);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCleanupProposalsVisible))]
    [NotifyPropertyChangedFor(nameof(IsIgnoredListVisible))]
    private bool _isIgnoredListOpen;

    public ObservableCollection<IgnoredProposalRow> IgnoredProposals { get; } = [];

    // Count of pending cleanup work (renames + splits + suspected duplicates), scanned on load so the entry
    // point can advertise there's something to do without the panel being open.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCleanupWork))]
    [NotifyPropertyChangedFor(nameof(CleanupBadgeText))]
    private int _cleanupPendingCount;

    public bool HasCleanupWork => CleanupPendingCount > 0;
    public string CleanupBadgeText => CleanupPendingCount.ToString(CultureInfo.CurrentCulture);

    public bool HasSelection => SelectedPerson is not null;
    public bool IsPlaceholderVisible => !HasSelection && !IsMergePanelOpen && !IsCleanupPanelOpen;
    public bool IsEditorVisible => HasSelection && !IsMergePanelOpen && !IsCleanupPanelOpen;
    public bool IsMergePanelVisible => IsMergePanelOpen && !IsCleanupPanelOpen;
    public bool IsCleanupPanelVisible => IsCleanupPanelOpen;
    public bool IsCleanupProposalsVisible => IsCleanupPanelOpen && !IsIgnoredListOpen;
    public bool IsIgnoredListVisible => IsCleanupPanelOpen && IsIgnoredListOpen;
    public bool HasSuspectedDuplicates => SuspectedDuplicates.Count > 0;
    public bool HasCheckedProposals => CheckedProposalCount > 0;
    public bool HasIgnored => IgnoredCount > 0;
    public bool HasNoCleanup => IsCleanupPanelOpen && CleanupProposals.Count == 0 && SuspectedDuplicates.Count == 0;
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
        await RefreshCleanupPendingCountAsync();
    }

    /// <summary>Scans for pending cleanup work so the entry point's badge reflects it without opening the panel.</summary>
    private async Task RefreshCleanupPendingCountAsync()
    {
        try
        {
            var (renames, splits, _) = await Service.ScanPersonNameCleanupAsync();
            CleanupPendingCount = renames.Count + splits.Count + SuspectedDuplicates.Count;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PersonTabViewModel: cleanup pending-count scan failed");
        }
    }

    private async Task LoadSuspectedDuplicatesAsync()
    {
        try
        {
            IsScanningDuplicates = true;
            var ignores = await Service.GetDuplicateIgnoresAsync();
            var snapshot = Persons.ToList();
            var (pairs, suppressed) = snapshot.Count > 500
                ? await Task.Run(() => ScanPairs(snapshot, ignores))
                : ScanPairs(snapshot, ignores);
            SuspectedDuplicates.Clear();
            foreach (var p in pairs) SuspectedDuplicates.Add(p);
            DuplicateIgnoredCount = suppressed;
            OnPropertyChanged(nameof(HasSuspectedDuplicates));
            OnPropertyChanged(nameof(HasNoCleanup));
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

    private static (List<SuspectedDuplicatePair> Pairs, int Suppressed) ScanPairs(
        List<PersonRow> snapshot, IReadOnlySet<(int AnchorPersonId, string Fingerprint)> ignores)
    {
        var result = new List<SuspectedDuplicatePair>();
        var suppressed = 0;
        for (int i = 0; i < snapshot.Count; i++)
        {
            for (int j = i + 1; j < snapshot.Count; j++)
            {
                var a = snapshot[i];
                var b = snapshot[j];
                if (string.Equals(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!StringSimilarityHelper.IsSuspectedDuplicate(a.DisplayName, b.DisplayName))
                    continue;
                // Mirror AddDuplicateIgnoreAsync: lower id anchors. The fingerprint carries the other person's id
                // (so two same-named people can't alias onto one ignore) plus name (so a rename resurfaces the
                // pair). Also match the pre-3.1 bare-name form so existing dismissals survive the upgrade.
                var anchorId = Math.Min(a.PersonId, b.PersonId);
                var otherId = Math.Max(a.PersonId, b.PersonId);
                var otherName = a.PersonId < b.PersonId ? b.DisplayName : a.DisplayName;
                if (ignores.Contains((anchorId, LookupManagementService.DuplicateFingerprint(otherId, otherName)))
                    || ignores.Contains((anchorId, otherName)))
                {
                    suppressed++;
                    continue;
                }
                result.Add(new SuspectedDuplicatePair(a, b));
            }
        }
        return (result, suppressed);
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
        _returnToCleanupAfterMerge = false;
        IsMergePanelOpen = true;
    }

    [RelayCommand]
    private void SelectDuplicatePair(SuspectedDuplicatePair? pair)
    {
        if (pair is null) return;
        MergeSource = pair.Left;
        MergeTarget = pair.Right;
        CanonicalPerson = null;
        _returnToCleanupAfterMerge = true;
        // Close cleanup before opening merge so IsMergePanelVisible (which reads !IsCleanupPanelOpen) settles true.
        IsCleanupPanelOpen = false;
        IsMergePanelOpen = true;
    }

    [RelayCommand]
    private async Task IgnoreDuplicateAsync(SuspectedDuplicatePair? pair)
    {
        if (pair is null) return;
        try
        {
            await Service.AddDuplicateIgnoreAsync(
                pair.Left.PersonId, pair.Left.DisplayName, pair.Right.PersonId, pair.Right.DisplayName);
            await LoadSuspectedDuplicatesAsync();
            await RefreshCleanupPendingCountAsync();
            OnPropertyChanged(nameof(HasNoCleanup));
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.Person_Cleanup_ErrorIgnoreFailed;
            Log.Error(ex, "PersonTabViewModel: ignore duplicate failed");
        }
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
            _returnToCleanupAfterMerge = false;
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
        // A duplicate fix launched from cleanup returns to the cleanup panel it came from; its proposal and
        // duplicate lists are untouched by an abort, so no rescan is needed.
        if (_returnToCleanupAfterMerge)
        {
            _returnToCleanupAfterMerge = false;
            IsCleanupPanelOpen = true;
        }
    }

    // --- Data cleanup flow ---

    private bool CanOpenDataCleanup() => !IsMergePanelOpen && !IsCleanupPanelOpen;

    [RelayCommand(CanExecute = nameof(CanOpenDataCleanup))]
    private async Task OpenDataCleanupAsync()
    {
        try
        {
            await RescanCleanupAsync();
            IsIgnoredListOpen = false;
            IsCleanupPanelOpen = true;
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.ManageLookups_ErrorCleanupScanFailed;
            Log.Error(ex, "PersonTabViewModel: cleanup scan failed");
        }
    }

    /// <summary>Re-runs the scan and refreshes the proposal rows and the ignored count — the single seam every
    /// cleanup mutation (apply, ignore, un-ignore) funnels through so the panel stays live.</summary>
    private async Task RescanCleanupAsync()
    {
        var (renames, splits, ignoredCount) = await Service.ScanPersonNameCleanupAsync();
        PopulateCleanupProposals(renames, splits);
        NameIgnoredCount = ignoredCount;
        CleanupPendingCount = renames.Count + splits.Count + SuspectedDuplicates.Count;
        OnPropertyChanged(nameof(HasNoCleanup));
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
                CurrentSortName = p.CurrentSortName,
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
            var first = true;
            foreach (var fragment in sp.Fragments)
            {
                var row = new CleanupProposalRow
                {
                    PersonId = sp.PersonId,
                    CurrentDisplayName = sp.CurrentDisplayName,
                    SplitGroupId = groupId,
                    IsSplitContinuation = !first,
                    SuggestedSortName = fragment.SuggestedSortName,
                    ApplyChecked = true
                };
                first = false;
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
                    .Select(r => new CleanupProposal(r.PersonId, r.CurrentDisplayName, r.CurrentSortName, r.ProposedDisplayName, r.SuggestedSortName))
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
            await RescanCleanupAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.ManageLookups_ErrorApplyCleanupFailed;
            Log.Error(ex, "PersonTabViewModel: apply cleanup failed");
        }
    }

    [RelayCommand]
    private async Task IgnoreProposalAsync(CleanupProposalRow? row)
    {
        if (row is null) return;
        try
        {
            if (row.IsSplitRow)
            {
                // Ignoring one fragment ignores the whole split — the fragments share a SplitGroupId.
                var fragments = CleanupProposals
                    .Where(r => r.SplitGroupId == row.SplitGroupId)
                    .Select(r => new SplitFragment(r.ProposedDisplayName, r.SuggestedSortName))
                    .ToList();
                await Service.AddCleanupIgnoreAsync(new SplitProposal(row.PersonId, row.CurrentDisplayName, fragments));
            }
            else
            {
                await Service.AddCleanupIgnoreAsync(new CleanupProposal(
                    row.PersonId, row.CurrentDisplayName, row.CurrentSortName, row.ProposedDisplayName, row.SuggestedSortName));
            }
            await RescanCleanupAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.Person_Cleanup_ErrorIgnoreFailed;
            Log.Error(ex, "PersonTabViewModel: ignore proposal failed");
        }
    }

    [RelayCommand]
    private async Task ViewIgnoredAsync()
    {
        try
        {
            await LoadIgnoredAsync();
            IsIgnoredListOpen = true;
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.Person_Cleanup_ErrorIgnoreFailed;
            Log.Error(ex, "PersonTabViewModel: load ignored failed");
        }
    }

    private async Task LoadIgnoredAsync()
    {
        IgnoredProposals.Clear();
        foreach (var r in await Service.GetCleanupIgnoresAsync())
        {
            var (person, change) = DescribeIgnored(r);
            IgnoredProposals.Add(new IgnoredProposalRow(r.IgnoreId, person, KindLabel(r.Kind), change));
        }
    }

    private static string KindLabel(string kind) => kind switch
    {
        CleanupIgnoreKind.Rename => Resources.Person_Cleanup_Kind_Rename,
        CleanupIgnoreKind.Split => Resources.Person_Cleanup_Kind_Split,
        CleanupIgnoreKind.Duplicate => Resources.Person_Cleanup_Kind_Duplicate,
        _ => kind,
    };

    // The stored ProposedContent is a machine fingerprint (name|sort, or fragment;fragment for splits, or the
    // paired name for duplicates). Turn it into what the ignored-list columns should read: the pair shown
    // whole for duplicates, and the raw "display|sort" pipes hidden for renames/splits.
    private static (string Person, string Change) DescribeIgnored(CleanupIgnoreRow r) => r.Kind switch
    {
        CleanupIgnoreKind.Duplicate => ($"{r.PersonDisplayName} / {FormatDuplicateContent(r.ProposedContent)}", string.Empty),
        CleanupIgnoreKind.Rename => (r.PersonDisplayName, FormatRenameContent(r.ProposedContent)),
        CleanupIgnoreKind.Split => (r.PersonDisplayName, FormatSplitContent(r.ProposedContent)),
        _ => (r.PersonDisplayName, r.ProposedContent),
    };

    private static string FormatRenameContent(string fingerprint)
    {
        // Fingerprint is "currentSortName|proposedDisplay|suggestedSort" — only the proposed pair is shown.
        var parts = fingerprint.Split('|');
        return parts.Length == 3 ? $"{parts[1]} ({parts[2]})" : fingerprint;
    }

    private static string FormatSplitContent(string fingerprint) =>
        string.Join("; ", fingerprint.Split(';').Select(f => f.Split('|')[0]));

    private static string FormatDuplicateContent(string fingerprint)
    {
        // New fingerprint is "otherId|otherName" — show only the name. Pre-3.1 rows are the bare name (no leading
        // id), so leave them untouched; guard the split on an actual numeric id so a name containing '|' survives.
        var pipe = fingerprint.IndexOf('|');
        return pipe > 0 && int.TryParse(fingerprint.AsSpan(0, pipe), out _) ? fingerprint[(pipe + 1)..] : fingerprint;
    }

    [RelayCommand]
    private void CloseIgnoredList() => IsIgnoredListOpen = false;

    [RelayCommand]
    private async Task UnignoreAsync(IgnoredProposalRow? row)
    {
        if (row is null) return;
        try
        {
            await Service.RemoveCleanupIgnoreAsync(row.IgnoreId);
            await LoadIgnoredAsync();
            // The un-ignored proposal reappears in the list and the ignored count drops. Re-scan both the name
            // proposals and the duplicate pairs, since the ignore could have been either kind.
            await LoadSuspectedDuplicatesAsync();
            await RescanCleanupAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.Person_Cleanup_ErrorUnignoreFailed;
            Log.Error(ex, "PersonTabViewModel: un-ignore failed");
        }
    }

    [RelayCommand]
    private void CloseDataCleanup()
    {
        foreach (var r in CleanupProposals) r.PropertyChanged -= OnProposalPropertyChanged;
        CleanupProposals.Clear();
        IgnoredProposals.Clear();
        IsCleanupPanelOpen = false;
        IsIgnoredListOpen = false;
        CheckedProposalCount = 0;
        NameIgnoredCount = 0;
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
