using Avalonia.Controls;

namespace BookDB.Desktop.Helpers;

internal static class WindowActivator
{
    /// <summary>
    /// Restores and brings <paramref name="window"/> to the foreground in response to a second launch.
    /// The brief Topmost toggle nudges Windows past its foreground-stealing guard so the window actually
    /// surfaces rather than only flashing in the taskbar. Must run on the UI thread.
    /// </summary>
    public static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Show();
        window.Activate();

        window.Topmost = true;
        window.Topmost = false;
    }
}
