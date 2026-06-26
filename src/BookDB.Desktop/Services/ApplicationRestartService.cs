using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using BookDB.Desktop.Helpers;
using BookDB.Desktop.Localization;
using Serilog;

namespace BookDB.Desktop.Services;

/// <inheritdoc />
public sealed class ApplicationRestartService : IApplicationRestartService
{
    public async Task<bool> ConfirmRestartAsync(string message)
    {
        var result = await AppDialogs.ShowConfirmDialogAsync(Resources.Settings_RestartConfirm_Title, message);
        return result == true;
    }

    public void Restart()
    {
        // Start the replacement first; it waits on the instance lock until this process releases it on exit.
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = SingleInstanceGate.RelaunchArgument,
                    UseShellExecute = false,
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ApplicationRestartService: failed to spawn the replacement process");
            }
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            Environment.Exit(0);
    }
}
