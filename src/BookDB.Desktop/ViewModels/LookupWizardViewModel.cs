using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using BookDB.Models;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BookDB.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Lookup Wizard dialog — handles ISBN entry, file drop, and lookup orchestration.
/// All lookups (single or multiple ISBNs) now go through the batch queue window for a unified UX.
/// </summary>
public sealed partial class LookupWizardViewModel : ObservableObject
{
    private readonly IWindowService _windowService;
    private readonly IFilePickerService _filePicker;

    /// <summary>
    /// Set by WindowService after construction to close the dialog with a result.
    /// </summary>
    public Action<bool?>? CloseDialog { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsbnCount))]
    private string _isbnText = string.Empty;

    [ObservableProperty]
    private bool _isLookupRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    private string? _errorMessage;

    public bool ShowError => ErrorMessage is not null;

    public int IsbnCount => ParseIsbns().Count;

    public LookupWizardViewModel(
        IWindowService windowService,
        IFilePickerService filePicker)
    {
        _windowService = windowService;
        _filePicker = filePicker;
    }

    private List<string> ParseIsbns()
    {
        if (string.IsNullOrWhiteSpace(IsbnText))
            return [];

        return [.. IsbnText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line))];
    }

    [RelayCommand]
    private async Task StartLookupAsync()
    {
        ErrorMessage = null;
        var isbns = ParseIsbns();
        if (isbns.Count == 0)
        {
            ErrorMessage = Resources.LookupWizard_Error_EnterIsbn;
            return;
        }

        // All lookups (single or multiple ISBNs) go through the batch queue window.
        // This unifies the code path and gives consistent UX (progress bar, covers, state reset).
        CloseDialog?.Invoke(true);
        await _windowService.StartBatchAsync(isbns);
    }

    [RelayCommand]
    private async Task BrowseFileAsync()
    {
        try
        {
            var path = await _filePicker.PickFileAsync(Localization.Resources.FilePicker_OpenIsbnList, new[] { ".txt", ".csv" });
            if (path is null) return;
            var lines = await File.ReadAllLinesAsync(path);
            IsbnText = string.Join("\n", ExtractIsbnLines(path, lines));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to browse for ISBN file");
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseDialog?.Invoke(false);
    }

    [RelayCommand]
    private void ManualEntry()
    {
        // Pass the first normalized ISBN as a prefill so the user doesn't have to retype it
        var isbns = ParseIsbns();
        var prefillIsbn = isbns.Count > 0 ? IsbnNormalizer.Normalize(isbns[0]) : null;
        CloseDialog?.Invoke(false);
        _ = _windowService.ShowAddBookDialogAsync(prefillIsbn: prefillIsbn);
    }

    /// <summary>
    /// Called from TextFileDropBehavior when a .txt or .csv file is dropped onto the wizard.
    /// Uses async file I/O so the UI thread is not blocked during file reads.
    /// </summary>
    [RelayCommand]
    private async Task HandleFileDropAsync(string filePath)
    {
        if (!File.Exists(filePath)) return;
        try
        {
            var lines = await File.ReadAllLinesAsync(filePath);
            IsbnText = string.Join("\n", ExtractIsbnLines(filePath, lines));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load ISBNs from dropped file {Path}", filePath);
        }
    }

    // For CSV files, skip the first non-empty line if it contains no digits (i.e. a header row).
    private static IEnumerable<string> ExtractIsbnLines(string filePath, string[] lines)
    {
        var trimmed = lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase)
            && trimmed.Count > 0
            && !trimmed[0].Any(char.IsDigit))
        {
            trimmed = trimmed.Skip(1).ToList();
        }
        return trimmed;
    }
}
