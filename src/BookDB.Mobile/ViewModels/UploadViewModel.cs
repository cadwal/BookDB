using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Contracts;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Services;
using BookDB.Mobile.Staging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Mobile.ViewModels;

/// <summary>
/// The books going across, one row each, updating as the computer says what it made of them. A run that is
/// cut short is not a loss: whatever was not confirmed is still staged, and Try again picks up exactly
/// those.
/// </summary>
public sealed partial class UploadViewModel : PageViewModel, IDisposable
{
    private readonly IStagingStore _staging;
    private readonly IUploadService _upload;
    private readonly INavigator _navigator;
    private readonly Dictionary<string, UploadRowViewModel> _rows = new(StringComparer.Ordinal);

    private CancellationTokenSource? _run;

    public UploadViewModel(IStagingStore staging, IUploadService upload, INavigator navigator)
    {
        _staging = staging;
        _upload = upload;
        _navigator = navigator;
    }

    /// <summary>Leaving the screen stops the run. Nothing is lost by that — a book is only ever dropped from
    /// the tray once the computer has confirmed it.</summary>
    public void Dispose()
    {
        _run?.Cancel();
        _run?.Dispose();
        _run = null;
    }

    public override string Title => Resources.Upload_Title;

    public ObservableCollection<UploadRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string? _summary;

    /// <summary>Only for a run that stopped with books still staged; a finished one has nothing to retry.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _canRetry;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        // Rows are seeded from what is staged, so the reader sees the whole batch before the computer has
        // said anything about any of it. A retry re-seeds from what is left.
        Rows.Clear();
        _rows.Clear();
        foreach (var item in _staging.Items)
        {
            var row = new UploadRowViewModel(item);
            _rows[item.ClientItemId] = row;
            Rows.Add(row);
        }

        Summary = string.Format(CultureInfo.CurrentCulture, Resources.Upload_Running, Rows.Count);
        IsRunning = true;
        CanRetry = false;

        _run?.Dispose();
        _run = new CancellationTokenSource();

        UploadResult result;
        try
        {
            result = await _upload.RunAsync(Apply, _run.Token);
        }
        finally
        {
            IsRunning = false;
        }

        Summary = result switch
        {
            UploadResult.Completed => Resources.Upload_Completed,
            UploadResult.NothingStaged => Resources.Upload_NothingStaged,
            UploadResult.Offline => Resources.Upload_Offline,
            UploadResult.Refused => Resources.Status_RevokedExplanation,
            _ => Resources.Upload_Interrupted,
        };

        CanRetry = result is UploadResult.Offline or UploadResult.Interrupted && _staging.Items.Count > 0;
    }

    private bool CanRun() => !IsRunning;

    [RelayCommand]
    private void Finish() => _navigator.ShowHub();

    private void Apply(BatchItemStatus status)
    {
        if (_rows.TryGetValue(status.ClientItemId, out var row))
            row.Apply(status);
    }
}
