using System;
using System.IO;

namespace BookDB.Desktop.Services.UpdateCheck;

/// <summary>How this copy of BookDB was installed — determines both where to check for a newer version
/// and which upgrade hint to show the user.</summary>
public enum InstallChannel
{
    /// <summary>Portable zip, standalone AppImage, installer, or unknown — checked against, and pointed at,
    /// the GitHub release.</summary>
    GitHub,

    /// <summary>Windows winget package — checked via winget and upgraded with <c>winget upgrade</c>.</summary>
    Winget,

    /// <summary>Linux AppImage managed by the AM/AppMan package manager — upgraded with <c>am -u bookdb</c>.</summary>
    AppMan,
}

public interface IInstallChannelProvider
{
    InstallChannel Current { get; }
}

public sealed class InstallChannelProvider : IInstallChannelProvider
{
    public InstallChannel Current => Detect(
        Environment.ProcessPath,
        Environment.GetEnvironmentVariable("APPIMAGE"),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        File.Exists);

    // Pure so it can be unit-tested without touching the real process/filesystem.
    internal static InstallChannel Detect(
        string? processPath, string? appImagePath, string localAppData, Func<string, bool> fileExists)
    {
        // Winget (Windows): the executable runs from the winget package folder (or its Links shim).
        if (!string.IsNullOrEmpty(processPath) && !string.IsNullOrEmpty(localAppData))
        {
            var packages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
            var links = Path.Combine(localAppData, "Microsoft", "WinGet", "Links");
            if (processPath.StartsWith(packages, StringComparison.OrdinalIgnoreCase) ||
                processPath.StartsWith(links, StringComparison.OrdinalIgnoreCase))
                return InstallChannel.Winget;
        }

        // AppMan (Linux): an AM-managed AppImage keeps an "AM-updater" script beside the AppImage —
        // a reliable marker regardless of AM's install root (/opt for `am`, ~/.local for `appman`).
        if (!string.IsNullOrEmpty(appImagePath))
        {
            var dir = Path.GetDirectoryName(appImagePath);
            if (!string.IsNullOrEmpty(dir) && fileExists(Path.Combine(dir, "AM-updater")))
                return InstallChannel.AppMan;
        }

        return InstallChannel.GitHub;
    }
}
