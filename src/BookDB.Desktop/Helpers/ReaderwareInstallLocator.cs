using System;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace BookDB.Desktop.Helpers;

/// <summary>
/// Resolves a sensible default for the Readerware tool folder (which ships the HSQLDB + Java runtime
/// used to read a live Readerware database). On Windows it reads Readerware's actual install location
/// from the uninstall registry — so a non-default drive is found automatically — and falls back to the
/// standard install path. macOS/Linux use a conventional path.
/// </summary>
public static class ReaderwareInstallLocator
{
    private const string WindowsFallback = @"C:\Program Files\Readerware 4";

    // Resolved once; registry probing is cheap but there is no need to repeat it.
    private static readonly Lazy<string> Default = new(Resolve);

    /// <summary>The default tool folder for this machine.</summary>
    public static string DefaultToolPath => Default.Value;

    private static string Resolve()
    {
        if (OperatingSystem.IsWindows())
            return FindWindowsInstall() ?? WindowsFallback;
        if (OperatingSystem.IsMacOS())
            return "/Applications/Readerware 4.app/Contents";
        return "/opt/readerware4";
    }

    [SupportedOSPlatform("windows")]
    private static string? FindWindowsInstall()
    {
        // Scan the standard uninstall registries (64-bit, 32-bit, and per-user) for a Readerware entry
        // and use its recorded InstallLocation.
        (RegistryHive Hive, RegistryView View)[] roots =
        [
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Default),
        ];

        foreach (var (hive, view) in roots)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null)
                    continue;

                foreach (var subName in uninstall.GetSubKeyNames())
                {
                    using var app = uninstall.OpenSubKey(subName);
                    if (app?.GetValue("DisplayName") is not string name ||
                        !name.StartsWith("Readerware", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (app.GetValue("InstallLocation") is string loc &&
                        !string.IsNullOrWhiteSpace(loc))
                    {
                        var path = loc.Trim().TrimEnd('\\');
                        if (Directory.Exists(path))
                            return path;
                    }
                }
            }
            catch
            {
                // Registry not readable in this view — try the next.
            }
        }

        return null;
    }
}
