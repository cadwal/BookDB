using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using BookDB.Desktop.Localization;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Desktop.ViewModels;

public enum ConnectChoice { Quit, ConnectAnyway }

public enum RelativeTimeUnit { Seconds, Minutes, Hours, Days }

/// <summary>A coarse "time ago" bucket, computed without any culture so the unit words can be localized separately.</summary>
public readonly record struct RelativeTime(long Value, RelativeTimeUnit Unit)
{
    public static RelativeTime FromAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;
        if (age.TotalMinutes < 1)
            return new RelativeTime((long)age.TotalSeconds, RelativeTimeUnit.Seconds);
        if (age.TotalHours < 1)
            return new RelativeTime((long)age.TotalMinutes, RelativeTimeUnit.Minutes);
        if (age.TotalDays < 1)
            return new RelativeTime((long)age.TotalHours, RelativeTimeUnit.Hours);
        return new RelativeTime((long)age.TotalDays, RelativeTimeUnit.Days);
    }
}

/// <summary>
/// Startup block when another live client is already on the shared database: names the other host,
/// how long ago it was seen, and its version. "Connect anyway" stays disabled for a few seconds so the block is
/// not clicked through by reflex; the default action is Quit.
/// </summary>
public sealed partial class ConnectDialogViewModel : ObservableObject
{
    public const int CountdownStartSeconds = 3;

    private readonly TimeProvider _clock;
    private ITimer? _timer;

    [ObservableProperty]
    private int _countdownSeconds = CountdownStartSeconds;

    public string Hostname { get; }
    public string Version { get; }
    public string LastSeenText { get; }
    public string BodyText { get; }

    public ConnectChoice? Result { get; private set; }
    public Action? CloseDialog { get; set; }

    public bool CanConnectAnyway => CountdownSeconds <= 0;

    public string ConnectAnywayText => CanConnectAnyway
        ? Resources.ConnectDialog_ConnectAnyway
        : string.Format(CultureInfo.CurrentCulture, Resources.ConnectDialog_ConnectAnyway_Waiting, CountdownSeconds);

    public ConnectDialogViewModel(IReadOnlyList<ClientSession> activeSessions, TimeProvider clock)
    {
        _clock = clock;
        // More than one other client is rare; surface the most recently seen of them.
        var session = activeSessions.OrderByDescending(s => s.LastSeenAt).First();
        Hostname = session.Hostname;
        Version = session.AppVersion;
        LastSeenText = FormatAge(_clock.GetUtcNow().UtcDateTime - session.LastSeenAt);
        BodyText = string.Format(CultureInfo.CurrentCulture, Resources.ConnectDialog_Body, Hostname, LastSeenText, Version);
    }

    public void StartCountdown()
    {
        _timer = _clock.CreateTimer(
            _ => Dispatcher.UIThread.Post(Tick), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>One countdown step; the timer fires it each second, but tests drive it directly.</summary>
    public void Tick()
    {
        if (CountdownSeconds > 0)
            CountdownSeconds--;
        if (CountdownSeconds <= 0)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    partial void OnCountdownSecondsChanged(int value)
    {
        OnPropertyChanged(nameof(CanConnectAnyway));
        OnPropertyChanged(nameof(ConnectAnywayText));
        ConnectAnywayCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Quit()
    {
        Result = ConnectChoice.Quit;
        Close();
    }

    [RelayCommand(CanExecute = nameof(CanConnectAnyway))]
    private void ConnectAnyway()
    {
        Result = ConnectChoice.ConnectAnyway;
        Close();
    }

    private void Close()
    {
        _timer?.Dispose();
        _timer = null;
        CloseDialog?.Invoke();
    }

    private static string FormatAge(TimeSpan age)
    {
        var relative = RelativeTime.FromAge(age);
        var format = relative.Unit switch
        {
            RelativeTimeUnit.Seconds => Resources.ConnectDialog_Age_Seconds,
            RelativeTimeUnit.Minutes => Resources.ConnectDialog_Age_Minutes,
            RelativeTimeUnit.Hours => Resources.ConnectDialog_Age_Hours,
            _ => Resources.ConnectDialog_Age_Days,
        };
        return string.Format(CultureInfo.CurrentCulture, format, relative.Value);
    }
}
