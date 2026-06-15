using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Avalonia.Platform;
using BookDB.Models.Interfaces;
using Serilog;

namespace BookDB.Desktop.Services;

/// <summary>
/// Creates and inspects OS launcher entries for the running app. Windows: Start Menu / desktop
/// `.lnk` files via the IShellLink COM interface. Linux: a `.desktop` entry in
/// ~/.local/share/applications. Pinning to the Windows taskbar / Start is intentionally not
/// attempted — Windows exposes no API for it; the user pins the created shortcut manually.
/// </summary>
public sealed class ShortcutService : IShortcutService
{
    private const string AppName = "BookDB";

    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsLinux => OperatingSystem.IsLinux();
    public bool IsSupported => IsWindows || IsLinux;

    // ---------------------------------------------------------------- Windows: create

    public ShortcutResult CreateStartMenuShortcut()
    {
        if (!OperatingSystem.IsWindows()) return new ShortcutResult(ShortcutStatus.NotSupported);
        return CreateWindowsShortcut(StartMenuLnkPath());
    }

    public ShortcutResult CreateDesktopShortcut()
    {
        if (!OperatingSystem.IsWindows()) return new ShortcutResult(ShortcutStatus.NotSupported);
        return CreateWindowsShortcut(DesktopLnkPath());
    }

    [SupportedOSPlatform("windows")]
    private static ShortcutResult CreateWindowsShortcut(string lnkPath)
    {
        try
        {
            var (target, unstable) = ResolveWindowsTarget();
            if (string.IsNullOrEmpty(target) || !File.Exists(target))
                return new ShortcutResult(ShortcutStatus.Failed, Error: "Executable path not found.");

            var link = (IShellLinkW)new ShellLink();
            link.SetPath(target);
            var workingDir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(workingDir)) link.SetWorkingDirectory(workingDir);
            link.SetIconLocation(WriteWindowsIcon() ?? target, 0);
            ((IPersistFile)link).Save(lnkPath, true);

            return new ShortcutResult(
                unstable ? ShortcutStatus.CreatedWithWingetWarning : ShortcutStatus.Created,
                lnkPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ShortcutService: failed to create Windows shortcut at {Path}", lnkPath);
            return new ShortcutResult(ShortcutStatus.Failed, Error: ex.Message);
        }
    }

    // ---------------------------------------------------------------- Windows: state

    public ShortcutState GetStartMenuShortcutState()
    {
        if (!OperatingSystem.IsWindows()) return ShortcutState.NotSupported;
        return GetWindowsShortcutState(StartMenuLnkPath());
    }

    public ShortcutState GetDesktopShortcutState()
    {
        if (!OperatingSystem.IsWindows()) return ShortcutState.NotSupported;
        return GetWindowsShortcutState(DesktopLnkPath());
    }

    [SupportedOSPlatform("windows")]
    private static ShortcutState GetWindowsShortcutState(string lnkPath)
    {
        try
        {
            if (!File.Exists(lnkPath)) return ShortcutState.Missing;
            var existing = ReadLnkTarget(lnkPath);
            var (current, _) = ResolveWindowsTarget();
            return PathsEqual(existing, current) ? ShortcutState.UpToDate : ShortcutState.Mismatch;
        }
        catch (Exception ex)
        {
            Log.Warning("ShortcutService: could not read shortcut {Path}: {Error}", lnkPath, ex.Message);
            return ShortcutState.Mismatch; // exists but unreadable — treat as not current
        }
    }

    [SupportedOSPlatform("windows")]
    private static string StartMenuLnkPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");

    [SupportedOSPlatform("windows")]
    private static string DesktopLnkPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

    /// <summary>
    /// Prefer winget's stable "bookdb" launcher symlink — it survives package updates, and
    /// launching a shortcut through it works (verified on v1.1.0) even though the terminal
    /// `bookdb` alias does not. Explorer cannot extract an icon from the symlink, which is why
    /// the shortcut icon comes from <see cref="WriteWindowsIcon"/> instead of the target. If the
    /// symlink isn't present and we are running from a versioned winget package folder, fall back
    /// to the executable but flag it as unstable so the UI can suggest an administrator reinstall.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static (string target, bool unstable) ResolveWindowsTarget()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var shim = Path.Combine(local, "Microsoft", "WinGet", "Links", "bookdb.exe");
        if (File.Exists(shim)) return (shim, false);

        var exe = Environment.ProcessPath ?? string.Empty;
        var wingetPackages = Path.Combine(local, "Microsoft", "WinGet", "Packages");
        var unstable = exe.StartsWith(wingetPackages, StringComparison.OrdinalIgnoreCase);
        return (exe, unstable);
    }

    /// <summary>Extracts the bundled .ico to %APPDATA%\BookDB\bookdb.ico and returns its path —
    /// a location that stays valid across winget updates, unlike the versioned package folder.
    /// Returns null on failure so the caller can fall back to the target executable.</summary>
    [SupportedOSPlatform("windows")]
    private static string? WriteWindowsIcon()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
            Directory.CreateDirectory(dir);
            var iconPath = Path.Combine(dir, "bookdb.ico");
            using var src = AssetLoader.Open(new Uri("avares://BookDB.Desktop/Assets/book.ico"));
            using var dst = File.Create(iconPath);
            src.CopyTo(dst);
            return iconPath;
        }
        catch (Exception ex)
        {
            Log.Warning("ShortcutService: could not extract Windows icon ({Error}); using the target executable", ex.Message);
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string ReadLnkTarget(string lnkPath)
    {
        var link = (IShellLinkW)new ShellLink();
        ((IPersistFile)link).Load(lnkPath, 0); // STGM_READ
        var sb = new StringBuilder(260);
        link.GetPath(sb, sb.Capacity, IntPtr.Zero, 4 /* SLGP_RAWPATH */);
        return sb.ToString();
    }

    // ---------------------------------------------------------------- Linux: create

    public ShortcutResult CreateApplicationMenuEntry()
    {
        if (!OperatingSystem.IsLinux()) return new ShortcutResult(ShortcutStatus.NotSupported);
        try
        {
            var desktopPath = LinuxDesktopPath();
            Directory.CreateDirectory(Path.GetDirectoryName(desktopPath)!);

            var exec = CurrentLinuxExec();
            if (string.IsNullOrEmpty(exec) || !File.Exists(exec))
                return new ShortcutResult(ShortcutStatus.Failed, Error: "Executable path not found.");

            var icon = WriteLinuxIcon();
            var desktop =
                "[Desktop Entry]\n" +
                "Type=Application\n" +
                $"Name={AppName}\n" +
                "Comment=Personal book catalog\n" +
                $"Exec=\"{exec}\"\n" +
                $"Icon={icon}\n" +
                "Categories=Office;Database;\n" +
                "Terminal=false\n";

            File.WriteAllText(desktopPath, desktop);

            // Best-effort: make it executable and refresh the menu database; neither is fatal.
            TryRun("chmod", $"+x \"{desktopPath}\"");
            TryRun("update-desktop-database", $"\"{Path.GetDirectoryName(desktopPath)}\"");

            return new ShortcutResult(ShortcutStatus.Created, desktopPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ShortcutService: failed to create Linux application entry");
            return new ShortcutResult(ShortcutStatus.Failed, Error: ex.Message);
        }
    }

    // ---------------------------------------------------------------- Linux: state

    public ShortcutState GetApplicationMenuEntryState()
    {
        if (!OperatingSystem.IsLinux()) return ShortcutState.NotSupported;
        try
        {
            var path = LinuxDesktopPath();
            if (!File.Exists(path)) return ShortcutState.Missing;

            var execLine = File.ReadLines(path)
                .FirstOrDefault(l => l.StartsWith("Exec=", StringComparison.Ordinal));
            if (execLine is null) return ShortcutState.Mismatch;

            var existing = ParseDesktopExec(execLine);
            return PathsEqual(existing, CurrentLinuxExec()) ? ShortcutState.UpToDate : ShortcutState.Mismatch;
        }
        catch (Exception ex)
        {
            Log.Warning("ShortcutService: could not read .desktop entry: {Error}", ex.Message);
            return ShortcutState.Mismatch;
        }
    }

    private static string LinuxDesktopPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "applications", "bookdb.desktop");

    /// <summary>An AppImage sets $APPIMAGE to the real .AppImage path; ProcessPath would be the mount.</summary>
    private static string CurrentLinuxExec()
    {
        var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
        return string.IsNullOrEmpty(appImage) ? Environment.ProcessPath ?? string.Empty : appImage;
    }

    private static string ParseDesktopExec(string execLine)
    {
        var value = execLine["Exec=".Length..].Trim();
        if (value.StartsWith('"'))
        {
            var end = value.IndexOf('"', 1);
            return end > 0 ? value[1..end] : value.Trim('"');
        }
        var space = value.IndexOf(' ');
        return space > 0 ? value[..space] : value;
    }

    /// <summary>Extracts the bundled icon to ~/.local/share/icons/bookdb.png; returns its path
    /// (or the bare name "bookdb" as a fallback so the entry still validates).</summary>
    private static string WriteLinuxIcon()
    {
        try
        {
            var iconsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share", "icons");
            Directory.CreateDirectory(iconsDir);
            var iconPath = Path.Combine(iconsDir, "bookdb.png");
            using var src = AssetLoader.Open(new Uri("avares://BookDB.Desktop/Assets/book.png"));
            using var dst = File.Create(iconPath);
            src.CopyTo(dst);
            return iconPath;
        }
        catch (Exception ex)
        {
            Log.Warning("ShortcutService: could not extract Linux icon ({Error}); using bare icon name", ex.Message);
            return "bookdb";
        }
    }

    // ---------------------------------------------------------------- helpers

    private static bool PathsEqual(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), cmp); }
        catch { return string.Equals(a, b, cmp); }
    }

    private static void TryRun(string fileName, string arguments)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            p?.WaitForExit(5000);
        }
        catch { /* best-effort: tool may be absent (e.g. update-desktop-database) — ignore */ }
    }

    // ---------------------------------------------------------------- COM interop (Windows)

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport,
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport,
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
