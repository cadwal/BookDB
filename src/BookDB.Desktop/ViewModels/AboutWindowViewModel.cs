using System;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Desktop.ViewModels;

public sealed partial class AboutWindowViewModel
{
    public string VersionText { get; }
    public string Copyright { get; }

    // Set by the show path before the window opens.
    public Action? CloseWindow { get; set; }

    public AboutWindowViewModel()
    {
        var assembly = typeof(AboutWindowViewModel).Assembly;
        var version = assembly.GetName().Version;
        VersionText = version != null
            ? string.Format(Localization.Resources.About_Version, $"{version.Major}.{version.Minor}.{version.Build}")
            : Localization.Resources.About_VersionUnknown;
        Copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
            ?? Localization.Resources.About_Copyright;
    }

    [RelayCommand]
    private void Close() => CloseWindow?.Invoke();
}
