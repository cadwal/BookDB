namespace BookDB.Models.Interfaces;

/// <summary>Outcome of a shortcut / launcher-entry creation attempt.</summary>
public enum ShortcutStatus
{
    Created,

    /// <summary>
    /// Created, but the shortcut points at a versioned winget package path (no stable launcher
    /// was found), so it may stop working after a winget update.
    /// </summary>
    CreatedWithWingetWarning,

    Failed,
    NotSupported,
}

/// <summary>Current state of an existing (or absent) shortcut, for display beside the create button.</summary>
public enum ShortcutState
{
    NotSupported,

    /// <summary>No shortcut exists at the expected location.</summary>
    Missing,

    /// <summary>A shortcut exists and points at the launcher this app would create now.</summary>
    UpToDate,

    /// <summary>A shortcut exists but points somewhere else (e.g. an old install location).</summary>
    Mismatch,
}

/// <summary>Result of creating a shortcut. <see cref="Path"/> is the file written on success.</summary>
public sealed record ShortcutResult(ShortcutStatus Status, string? Path = null, string? Error = null);

/// <summary>
/// Creates and inspects OS launcher entries for the running application: Start Menu and desktop
/// shortcuts on Windows, an application-menu (.desktop) entry on Linux. Unsupported elsewhere.
/// </summary>
public interface IShortcutService
{
    bool IsWindows { get; }
    bool IsLinux { get; }
    bool IsSupported { get; }

    /// <summary>Windows: create a Start Menu shortcut for the current executable.</summary>
    ShortcutResult CreateStartMenuShortcut();

    /// <summary>Windows: create a desktop shortcut for the current executable.</summary>
    ShortcutResult CreateDesktopShortcut();

    /// <summary>Linux: create an application-menu (.desktop) entry for the current executable.</summary>
    ShortcutResult CreateApplicationMenuEntry();

    /// <summary>Windows: current state of the Start Menu shortcut.</summary>
    ShortcutState GetStartMenuShortcutState();

    /// <summary>Windows: current state of the desktop shortcut.</summary>
    ShortcutState GetDesktopShortcutState();

    /// <summary>Linux: current state of the application-menu entry.</summary>
    ShortcutState GetApplicationMenuEntryState();
}
