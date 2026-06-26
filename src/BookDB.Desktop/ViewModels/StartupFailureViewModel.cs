using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Desktop.ViewModels;

public enum StartupFailureOutcome { Proceed, OpenSettings, Quit }

/// <summary>
/// Shown before the main window when the remote database cannot be reached at startup: names the specific
/// failure and offers Retry / Open settings / Quit — never a silent fallback. Retry re-probes in place; after a
/// few failures it dampens (the button disables) so a down server can't drive an endless retry loop.
/// </summary>
public sealed partial class StartupFailureViewModel : ObservableObject
{
    public const int MaxRetries = 3;

    private readonly Func<CancellationToken, Task<ConnectionProbeResult>> _connect;

    [ObservableProperty]
    private string _message;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _failedAttempts;

    public StartupFailureOutcome? Outcome { get; private set; }
    public Action? CloseDialog { get; set; }

    public bool CanRetry => !IsBusy && FailedAttempts < MaxRetries;

    /// <summary>True once Retry has been dampened, so the view can nudge the user toward settings or quitting.</summary>
    public bool RetriesExhausted => FailedAttempts >= MaxRetries;

    public StartupFailureViewModel(
        ConnectionProbeResult initialResult,
        Func<CancellationToken, Task<ConnectionProbeResult>> connect)
    {
        _connect = connect;
        FailedAttempts = 1; // the startup attempt that opened this dialog already failed once
        Message = Describe(initialResult);
    }

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private async Task RetryAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _connect(CancellationToken.None);
            if (result.IsSuccess)
            {
                Outcome = StartupFailureOutcome.Proceed;
                CloseDialog?.Invoke();
                return;
            }

            FailedAttempts++;
            Message = Describe(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        Outcome = StartupFailureOutcome.OpenSettings;
        CloseDialog?.Invoke();
    }

    [RelayCommand]
    private void Quit()
    {
        Outcome = StartupFailureOutcome.Quit;
        CloseDialog?.Invoke();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRetry));
        RetryCommand.NotifyCanExecuteChanged();
    }

    partial void OnFailedAttemptsChanged(int value)
    {
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(RetriesExhausted));
        RetryCommand.NotifyCanExecuteChanged();
    }

    private static string Describe(ConnectionProbeResult result) =>
        ConnectionErrorText.Describe(result.Status, result.ErrorDetail);
}
