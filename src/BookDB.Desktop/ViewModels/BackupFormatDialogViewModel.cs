using System;
using System.Threading.Tasks;
using BookDB.Desktop.Localization;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Desktop.ViewModels;

/// <summary>
/// Format + destination picker for a manual backup. Mirrors the auto-backup logic: the file format is always
/// offered, but on a backend that can't do a file backup the choice falls back to the CSV archive (with an
/// always-visible note). The destination defaults to the configured auto-backup folder and can be changed
/// in-place, so the backup never has to pop a separate folder dialog. The format identifiers match the
/// <c>AutoBackup.Format</c> config values so a single default flows through.
/// </summary>
public partial class BackupFormatDialogViewModel : ObservableObject
{
    public const string SqliteFormat = "SQLite";
    public const string CsvFormat = "CsvArchive";

    private readonly bool _supportsFileBackup;
    private readonly IFilePickerService _filePicker;

    [ObservableProperty]
    private bool _sqliteSelected;

    [ObservableProperty]
    private bool _csvSelected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _destinationFolder;

    /// <summary>Always shown on a backend that can't do a file backup, so the constraint is clear the moment the
    /// dialog opens — not only once the file format is picked.</summary>
    public bool ShowRemoteFallbackNote => !_supportsFileBackup;

    /// <summary>The chosen format, or null when the dialog was cancelled.</summary>
    public string? Result { get; private set; }

    public Action<bool>? CloseDialog { get; set; }

    public BackupFormatDialogViewModel(bool supportsFileBackup, string configDefault, string defaultFolder, IFilePickerService filePicker)
    {
        _supportsFileBackup = supportsFileBackup;
        _filePicker = filePicker;
        _destinationFolder = defaultFolder;
        // Preselect CSV when the backend has no file backup; otherwise honour the configured default.
        if (!supportsFileBackup || configDefault == CsvFormat)
            CsvSelected = true;
        else
            SqliteSelected = true;
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var folder = await _filePicker.PickFolderAsync(Resources.FilePicker_ChooseBackupDestination);
        if (!string.IsNullOrEmpty(folder))
            DestinationFolder = folder;
    }

    private bool CanConfirm() => !string.IsNullOrWhiteSpace(DestinationFolder);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        Result = SqliteSelected ? SqliteFormat : CsvFormat;
        CloseDialog?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseDialog?.Invoke(false);
}
