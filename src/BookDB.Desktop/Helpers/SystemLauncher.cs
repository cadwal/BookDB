using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BookDB.Desktop.Helpers;

/// <summary>
/// Opens a file, folder, or URL in the OS default handler.
/// On Windows we use the shell association (<c>UseShellExecute</c>). On Linux that relies on a
/// shell-open handler that may be absent (e.g. headless or WSL sessions), where .NET otherwise
/// falls back to exec'ing the target directly and fails — so we invoke <c>xdg-open</c> explicitly.
/// macOS uses <c>open</c>. Callers are responsible for catching exceptions.
/// </summary>
public static class SystemLauncher
{
    public static void Open(string target)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            return;
        }

        // ArgumentList quotes the target safely (paths may contain spaces).
        var opener = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open";
        var psi = new ProcessStartInfo { FileName = opener, UseShellExecute = false };
        psi.ArgumentList.Add(target);
        Process.Start(psi);
    }
}
