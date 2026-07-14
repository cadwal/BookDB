using System;
using System.Reflection;

namespace BookDB.Desktop.Helpers;

/// <summary>
/// Disposes Avalonia's shared DBus connection while the dispatcher is still alive. Left to the runtime, the
/// connection tears down after the dispatcher has stopped; Tmds.DBus then delivers the disconnect to its
/// observers with a synchronous dispatcher Send, the dead dispatcher cancels the operation, and the resulting
/// TaskCanceledException escapes an async void as a process-crash banner no handler can suppress
/// (AvaloniaUI/Avalonia#19523, deterministic on Wayland sessions). Disposing on the lifetime's Exit event
/// delivers those notifications inline on the live UI thread instead. DBusHelper is internal, so this
/// reflects; on a future Avalonia where the member moved, it degrades to a no-op and the harmless banner
/// returns.
/// </summary>
internal static class DBusShutdown
{
    public static void DisposeDefaultConnection()
    {
        if (!OperatingSystem.IsLinux())
            return;
        try
        {
            var helper = Type.GetType("Avalonia.FreeDesktop.DBusHelper, Avalonia.FreeDesktop");
            // Read the backing field, not the DefaultConnection property — the getter lazily creates a
            // connection when none exists. The field name's misspelling is Avalonia's; it must match.
            var field = helper?.GetField("s_defaultConntection", BindingFlags.NonPublic | BindingFlags.Static);
            (field?.GetValue(null) as IDisposable)?.Dispose();
        }
        catch
        {
            // Best-effort: shutdown must never fail over DBus cleanup.
        }
    }
}
