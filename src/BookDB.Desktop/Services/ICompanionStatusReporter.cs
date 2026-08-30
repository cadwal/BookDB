using System;

namespace BookDB.Desktop.Services;

/// <summary>
/// What the main window needs to know about the companion, and no more: whether the phone can reach this
/// computer at all, and how many devices are on its list. The companion is a listener with no window of its
/// own, so without this the only place it is ever mentioned is a row in Settings — and a listener that is
/// silently off, or silently failed to take its port, looks exactly like one that is on.
/// </summary>
public interface ICompanionStatusReporter
{
    CompanionHostStatus Status { get; }

    /// <summary>Devices paired with this computer. Zero with the host running means it is listening for a
    /// first phone.</summary>
    int PairedDeviceCount { get; }

    /// <summary>Raised when the host is started, stopped or restarted, and when a device is paired or
    /// removed — on whatever thread did it, so a UI subscriber marshals for itself.</summary>
    event EventHandler? StatusChanged;
}
