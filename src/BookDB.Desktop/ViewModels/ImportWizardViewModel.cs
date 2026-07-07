using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BookDB.Desktop.Helpers;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Messages;
using BookDB.Desktop.Services;
using BookDB.Logic.Import;
using BookDB.Logic.Messages;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BookDB.Desktop.ViewModels;

/// <summary>
/// Wizard shell ViewModel — owns step navigation and delegates to per-step VMs.
/// Steps: 0=FileSelect, 1=Preview, 2=Confirm, 3=Progress, 4=Report
/// </summary>
public sealed partial class ImportWizardViewModel : ObservableObject
{
    private readonly IImportService _importService;
    private readonly IFilePickerService _filePicker;
    private readonly ILookupService _lookupService;
    private readonly ILookupManagementService _lookupManagement;
    private readonly IMessenger _messenger;
    private CancellationTokenSource? _importCts;

    // Set by WindowService to close the dialog
    public Action<bool?>? CloseDialog { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStepView))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(ShowCancelButton))]
    [NotifyPropertyChangedFor(nameof(ShowBackButton))]
    [NotifyPropertyChangedFor(nameof(ShowNextButton))]
    [NotifyPropertyChangedFor(nameof(ShowImportButton))]
    [NotifyPropertyChangedFor(nameof(ShowCloseButton))]
    [NotifyPropertyChangedFor(nameof(StepTitle))]
    [NotifyPropertyChangedFor(nameof(StepIndicator))]
    private int _currentStepIndex = 0;

    // Step ViewModels
    public ImportStep1ViewModel Step1 { get; } = new();
    public ImportStep2ViewModel Step2 { get; } = new();
    public ImportStep3ViewModel Step3 { get; } = new();
    public ImportStep4ViewModel Step4 { get; } = new();
    public ImportStep5ViewModel Step5 { get; } = new();

    /// <summary>Current step VM — bound to ContentControl in the wizard window.</summary>
    public object CurrentStepView => CurrentStepIndex switch
    {
        0 => Step1,
        1 => Step2,
        2 => Step3,
        3 => Step4,
        4 => Step5,
        _ => Step1
    };

    public bool CanGoBack => CurrentStepIndex > 0 && CurrentStepIndex < 3;
    public bool CanGoNext => CurrentStepIndex < 2 && IsCurrentStepValid();
    public bool ShowCancelButton => CurrentStepIndex < 3;
    public bool ShowBackButton => CanGoBack;
    public bool ShowNextButton => CurrentStepIndex < 2;
    public bool ShowImportButton => CurrentStepIndex == 2;
    public bool ShowCloseButton => CurrentStepIndex == 4;

    public string StepTitle => CurrentStepIndex switch
    {
        0 => Resources.Import_StepTitle_FileSelect,
        1 => Resources.Import_StepTitle_Preview,
        2 => Resources.Import_StepTitle_Confirm,
        3 => Resources.Import_StepTitle_Progress,
        4 => Resources.Import_StepTitle_Complete,
        _ => Resources.Import_StepTitle_Default
    };

    private const int TotalSteps = 5;

    public string StepIndicator =>
        string.Format(Resources.Import_StepIndicator, CurrentStepIndex + 1, TotalSteps);

    public ImportWizardViewModel(
        IImportService importService,
        IFilePickerService filePicker,
        ILookupService lookupService,
        ILookupManagementService lookupManagement,
        IMessenger messenger)
    {
        _importService = importService;
        _filePicker = filePicker;
        _lookupService = lookupService;
        _lookupManagement = lookupManagement;
        _messenger = messenger;

        // Delegate commands to step VMs so AXAML can bind without parent traversal
        Step1.PickFileCommand = PickFileCommand;
        Step1.PickFolderCommand = PickFolderCommand;
        Step1.CreateCollectionCommand = CreateCollectionCommand;
        Step4.CancelImportCommand = CancelImportCommand;

        Step1.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanGoNext));
            NextCommand.NotifyCanExecuteChanged();
            CreateCollectionCommand.NotifyCanExecuteChanged();
        };

        // Step4 subscribes to ImportProgressMessage
        _messenger.Register<ImportStep4ViewModel, ImportProgressMessage>(Step4, (recipient, msg) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                recipient.ProgressPercent = msg.Total > 0 ? (double)msg.Processed / msg.Total * 100.0 : 0;
                recipient.ProcessedCount = msg.Processed;
                recipient.TotalCount = msg.Total;
                recipient.CurrentTitle = msg.CurrentTitle.Length > 60
                    ? msg.CurrentTitle[..57] + "..."
                    : msg.CurrentTitle;
            });
        });
    }

    public async Task InitializeAsync()
    {
        await LoadCollectionsAsync();
    }

    /// <summary>(Re)load the collection list; optionally select the collection with <paramref name="selectId"/>.</summary>
    private async Task LoadCollectionsAsync(int? selectId = null)
    {
        var collections = await _lookupService.GetCollectionsAsync();
        Step1.AvailableCollections.Clear();
        foreach (var col in collections)
            Step1.AvailableCollections.Add(col);

        if (selectId is int id)
        {
            var match = Step1.AvailableCollections.FirstOrDefault(c => c.CollectionId == id);
            if (match is not null)
                Step1.SelectedCollection = match;
        }
    }

    private bool CanCreateCollection => !string.IsNullOrWhiteSpace(Step1.NewCollectionName);

    [RelayCommand(CanExecute = nameof(CanCreateCollection))]
    private async Task CreateCollectionAsync()
    {
        var name = Step1.NewCollectionName.Trim();
        try
        {
            var id = await _lookupManagement.AddCollectionAsync(name);
            await LoadCollectionsAsync(id);
            Step1.NewCollectionName = string.Empty;
            Step1.ErrorMessage = null;
        }
        catch (Exception)
        {
            // Most commonly a duplicate name; keep the typed text so the user can adjust it.
            Step1.ErrorMessage = Localization.Resources.Import_Step1_CollectionCreateFailed;
        }
    }

    private bool IsCurrentStepValid() => CurrentStepIndex switch
    {
        0 => Step1.IsValid,
        1 => true,
        2 => true,
        _ => false
    };

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        if (CurrentStepIndex == 0)
        {
            // Step 1 → 2: run preview
            Step2.IsLoading = true;
            CurrentStepIndex = 1;
            try
            {
                var preview = await _importService.PreviewAsync(
                    Step1.FilePath,
                    Step1.SelectedCollectionId!.Value);

                Step2.Preview = preview;
                Step2.TotalRecords = preview.TotalRecords;
                Step2.DuplicateIsbnCount = preview.DuplicateIsbnCount;
                Step2.RecordsWithCovers = preview.RecordsWithCovers;
                Step2.RecordsWithIsbn = preview.RecordsWithIsbn;
                Step2.RecordsWithoutIsbn = preview.RecordsWithoutIsbn;
                Step2.Warnings = preview.Warnings;
                Step2.SampleRows.Clear();
                foreach (var row in preview.SampleRows)
                    Step2.SampleRows.Add(row);
            }
            catch (Exception ex)
            {
                Step2.IsLoading = false;
                // Return to step 1 on failure
                CurrentStepIndex = 0;
                var displayMessage = ex.Message.Split('\n', 2)[0].Trim();
                Step1.ErrorMessage = string.Format(Localization.Resources.Import_Step1_PreviewFailed, displayMessage);
                return;
            }
            Step2.IsLoading = false;

            // Populate step 3 summary
            Step3.Summary = string.Format(Resources.Import_Step3_Summary,
                Step2.TotalRecords,
                Step2.RecordsWithIsbn,
                Step2.RecordsWithoutIsbn,
                Step2.DuplicateIsbnCount,
                Step2.RecordsWithCovers);
        }
        else if (CurrentStepIndex == 1)
        {
            // Step 2 → 3: confirm
            CurrentStepIndex = 2;
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStepIndex > 0 && CurrentStepIndex < 3)
        {
            CurrentStepIndex--;
            OnPropertyChanged(nameof(CanGoNext));
            NextCommand.NotifyCanExecuteChanged();
            BackCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseDialog?.Invoke(false);
    }

    [RelayCommand]
    private async Task StartImportAsync()
    {
        CurrentStepIndex = 3;
        Step4.ProgressPercent = 0;
        Step4.ProcessedCount = 0;
        Step4.TotalCount = Step2.TotalRecords;
        Step4.IsCancelling = false;

        _importCts = new CancellationTokenSource();

        var progress = new Progress<ImportProgress>(p =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                Step4.ProgressPercent = p.Total > 0 ? (double)p.Processed / p.Total * 100.0 : 0;
                Step4.ProcessedCount = p.Processed;
                Step4.TotalCount = p.Total;
                Step4.CurrentTitle = p.CurrentTitle.Length > 60 ? p.CurrentTitle[..57] + "..." : p.CurrentTitle;
            });
        });

        Func<string, CancellationToken, Task<ImportDuplicateResolution>> askCallback = async (title, ct) =>
            await Dispatcher.UIThread.InvokeAsync(() =>
                AppDialogs.ShowDuplicateResolutionDialogAsync(
                    Localization.Resources.Import_Ask_Title,
                    string.Format(Localization.Resources.Import_Ask_Body, title)));

        ImportResult result;
        try
        {
            result = await Task.Run(async () => await _importService.ImportAsync(
                Step1.FilePath,
                Step1.SelectedCollectionId!.Value,
                progress,
                askCallback,
                _importCts.Token));
        }
        catch (OperationCanceledException)
        {
            result = new ImportResult(
                Step4.ProcessedCount, 0, 0, 0, true, System.Array.Empty<string>());
        }
        catch (Exception ex)
        {
            result = new ImportResult(
                0, 0, 0, 0, false,
                new[] { ex.Message });
        }

        Step5.Result = result;
        Step5.Imported = result.Imported;
        Step5.Updated = result.Updated;
        Step5.Skipped = result.Skipped;
        Step5.FlaggedNoIsbn = result.FlaggedNoIsbn;
        Step5.WasCancelled = result.WasCancelled;
        Step5.Errors = result.Errors;

        CurrentStepIndex = 4;
    }

    [RelayCommand]
    private void CancelImport()
    {
        Step4.IsCancelling = true;
        _importCts?.Cancel();
    }

    [RelayCommand]
    private void Close()
    {
        CloseDialog?.Invoke(true);
    }

    [RelayCommand]
    private async Task PickFileAsync()
    {
        var path = await _filePicker.PickFileAsync(Localization.Resources.FilePicker_SelectReaderwareBackup, new[] { "*.zip" });
        if (path is not null)
        {
            Step1.FilePath = path;
            Step1.ErrorMessage = null;
        }
    }

    [RelayCommand]
    private async Task PickFolderAsync()
    {
        var path = await _filePicker.PickFolderAsync(Localization.Resources.FilePicker_SelectReaderwareFolder);
        if (path is not null)
        {
            Step1.FilePath = path;
            Step1.ErrorMessage = null;
        }
    }
}

// ─── Step ViewModels ─────────────────────────────────────────────────────────

public sealed partial class ImportStep1ViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _filePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyPropertyChangedFor(nameof(SelectedCollectionId))]
    private Collection? _selectedCollection;

    [ObservableProperty]
    private string? _errorMessage;

    public int? SelectedCollectionId => SelectedCollection?.CollectionId;

    public ObservableCollection<Collection> AvailableCollections { get; } = [];

    [ObservableProperty]
    private string _newCollectionName = string.Empty;

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(FilePath) && SelectedCollection is not null;

    // Commands delegated from ImportWizardViewModel (set after construction)
    public IRelayCommand? PickFileCommand { get; set; }
    public IRelayCommand? PickFolderCommand { get; set; }
    public IRelayCommand? CreateCollectionCommand { get; set; }
}

public sealed partial class ImportStep2ViewModel : ObservableObject
{
    [ObservableProperty] private ImportPreview? _preview;
    [ObservableProperty] private int _totalRecords;
    [ObservableProperty] private int _duplicateIsbnCount;
    [ObservableProperty] private int _recordsWithCovers;
    [ObservableProperty] private int _recordsWithIsbn;
    [ObservableProperty] private int _recordsWithoutIsbn;
    [ObservableProperty] private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarnings))]
    [NotifyPropertyChangedFor(nameof(WarningHeader))]
    private System.Collections.Generic.IReadOnlyList<string>? _warnings;

    public bool HasWarnings => Warnings is { Count: > 0 };

    public string WarningHeader =>
        string.Format(Localization.Resources.Import_Step2_WarningsHeader, Warnings?.Count ?? 0);

    public ObservableCollection<ImportSampleRow> SampleRows { get; } = [];
}

public sealed partial class ImportStep3ViewModel : ObservableObject
{
    [ObservableProperty] private string _summary = string.Empty;
}

public sealed partial class ImportStep4ViewModel : ObservableObject
{
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private int _processedCount;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private string _currentTitle = string.Empty;
    [ObservableProperty] private bool _isCancelling;

    // Command delegated from ImportWizardViewModel (set after construction)
    public IRelayCommand? CancelImportCommand { get; set; }
}

public sealed partial class ImportStep5ViewModel : ObservableObject
{
    [ObservableProperty] private ImportResult? _result;
    [ObservableProperty] private int _imported;
    [ObservableProperty] private int _updated;
    [ObservableProperty] private int _skipped;
    [ObservableProperty] private int _flaggedNoIsbn;
    [ObservableProperty] private bool _wasCancelled;
    [ObservableProperty] private System.Collections.Generic.IReadOnlyList<string>? _errors;
}
