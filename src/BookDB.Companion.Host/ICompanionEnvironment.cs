using BookDB.Contracts;

namespace BookDB.Companion.Host;

/// <summary>
/// What the desktop tells a phone about itself at handshake. Read per call rather than captured at start:
/// the user can switch library or change the capture settings without the host restarting.
/// </summary>
public interface ICompanionEnvironment
{
    string AppVersion { get; }
    string LibraryName { get; }

    /// <summary>Stable across address changes, so a phone can tell "same desktop, new IP" from "another desktop".</summary>
    string InstanceId { get; }

    CaptureSettings Capture { get; }
}
