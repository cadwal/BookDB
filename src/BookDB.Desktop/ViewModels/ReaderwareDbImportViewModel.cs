using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BookDB.Desktop.Localization;
using BookDB.Logic.Import;
using BookDB.Logic.Services;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BookDB.Desktop.ViewModels;

/// <summary>
/// Drives the "Import from Readerware database" dialog: pick a live <c>.rw4</c> folder, convert it to a
/// backup folder via <see cref="IReaderwareDbExportService"/> (off the UI thread, with a progress log
/// and cancellation), then hand the resulting folder back so the Import Wizard can open on it.
/// </summary>
public sealed partial class ReaderwareDbImportViewModel : ObservableObject
{
    private readonly IReaderwareDbExportService _exportService;
    private readonly IFilePickerService _filePicker;
    private readonly ISettingsService _settingsService;

    private CancellationTokenSource? _cts;

    /// <summary>Closes the dialog. Carries the output folder on success, or null on cancel/close.</summary>
    public Action<string?>? CloseDialog { get; set; }

    /// <summary>The backup folder produced by a successful conversion.</summary>
    public string OutputDirectory { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    private string _databasePath = string.Empty;

    [ObservableProperty]
    private string _toolPath = SettingsImportTabViewModel.DefaultReaderwareToolPath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    [NotifyCanExecuteChangedFor(nameof(PickDatabaseFolderCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isComplete;

    [ObservableProperty]
    private int _exportedTableCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    private string _logText = string.Empty;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public ReaderwareDbImportViewModel(
        IReaderwareDbExportService exportService,
        IFilePickerService filePicker,
        ISettingsService settingsService)
    {
        _exportService = exportService;
        _filePicker = filePicker;
        _settingsService = settingsService;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            ToolPath = await _settingsService.GetAsync("Import.ReaderwareToolPath", ct)
                ?? SettingsImportTabViewModel.DefaultReaderwareToolPath;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ReaderwareDbImportViewModel: InitializeAsync failed");
        }
    }

    private bool CanConvert => !IsRunning && !string.IsNullOrWhiteSpace(DatabasePath);

    private bool CanPick => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanPick))]
    private async Task PickDatabaseFolderAsync()
    {
        var path = await _filePicker.PickFolderAsync(Resources.FilePicker_ChooseReaderwareDatabase);
        if (!string.IsNullOrEmpty(path))
        {
            DatabasePath = path;
            ErrorMessage = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConvert))]
    private async Task ConvertAsync()
    {
        ErrorMessage = null;
        IsComplete = false;
        LogText = string.Empty;
        IsRunning = true;
        _cts = new CancellationTokenSource();

        var outputDir = Path.Combine(Path.GetTempPath(), $"bookdb_rwimport_{Guid.NewGuid():N}");
        var progress = new Progress<string>(line =>
            Dispatcher.UIThread.Post(() => LogText += line + Environment.NewLine));

        ReaderwareExportResult result;
        try
        {
            result = await Task.Run(() => _exportService.ExportAsync(
                DatabasePath, outputDir, ToolPath, progress, _cts.Token));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ReaderwareDbImportViewModel: ConvertAsync failed");
            ErrorMessage = Resources.ReaderwareImport_Error_ProcessFailed;
            return;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsRunning = false;
        }

        if (result.Success)
        {
            OutputDirectory = result.OutputDirectory;
            ExportedTableCount = result.ExportedTables.Count;
            IsComplete = true;
        }
        else if (result.Failure != ReaderwareExportFailure.Cancelled)
        {
            ErrorMessage = MapError(result.Failure);
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void Continue()
        => CloseDialog?.Invoke(string.IsNullOrEmpty(OutputDirectory) ? null : OutputDirectory);

    [RelayCommand]
    private void Close() => CloseDialog?.Invoke(null);

    private static string MapError(ReaderwareExportFailure failure) => failure switch
    {
        ReaderwareExportFailure.ToolPathInvalid => Resources.ReaderwareImport_Error_ToolPathInvalid,
        ReaderwareExportFailure.DatabaseInvalid => Resources.ReaderwareImport_Error_DatabaseInvalid,
        ReaderwareExportFailure.MainTableMissing => Resources.ReaderwareImport_Error_MainTableMissing,
        _ => Resources.ReaderwareImport_Error_ProcessFailed,
    };
}
