using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BookDB.Desktop.Localization;
using BookDB.Logic.Services;
using BookDB.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BookDB.Desktop.ViewModels;

/// <summary>
/// Drives the Tools → Maintenance dialog. "Run check" reports the database integrity (read-only); "Optimize &amp;
/// repair" takes a safety backup and runs safe optimizations. Both run off the UI thread and stream localized
/// progress lines (mapped from <see cref="MaintenanceStep"/> via <see cref="MaintenanceText"/>) into the log.
/// </summary>
public sealed partial class MaintenanceViewModel : ObservableObject
{
    private readonly IDatabaseMaintenanceService _maintenanceService;

    /// <summary>The "Move library" section hosted in this dialog's second tab.</summary>
    public MoveLibraryViewModel MoveLibrary { get; }

    /// <summary>Closes the dialog. Set by <c>WindowService</c>.</summary>
    public Action? CloseDialog { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(OptimizeAndRepairCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string _logText = string.Empty;

    public MaintenanceViewModel(IDatabaseMaintenanceService maintenanceService, MoveLibraryViewModel moveLibrary)
    {
        _maintenanceService = maintenanceService;
        MoveLibrary = moveLibrary;
    }

    private bool CanRun => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunCheckAsync()
    {
        IsRunning = true;
        var progress = StepProgress();
        try
        {
            var result = await Task.Run(
                () => _maintenanceService.CheckIntegrityAsync(CancellationToken.None, progress));

            AppendLine(MaintenanceText.Describe(result.Status));

            if (result.Status == MaintenanceCheckStatus.IntegrityFailed)
            {
                foreach (var message in result.IntegrityMessages)
                    AppendLine("  • " + message);
                AppendLine(Resources.Maintenance_AdviseRestore);
            }
            else if (result.Status == MaintenanceCheckStatus.ForeignKeyViolations)
            {
                foreach (var violation in result.ForeignKeyViolations)
                    AppendLine("  • " + violation);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "MaintenanceViewModel: RunCheckAsync failed");
            AppendLine(Resources.Maintenance_RepairFailed);
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task OptimizeAndRepairAsync()
    {
        IsRunning = true;
        var progress = StepProgress();
        // Show the backup location in step order (right after "creating a safety backup…"), not last.
        var backupReport = new Progress<string>(path => Dispatcher.UIThread.Post(
            () => AppendLine(string.Format(Resources.Maintenance_SafetyBackupSaved, path))));
        try
        {
            var result = await Task.Run(
                () => _maintenanceService.OptimizeAndRepairAsync(CancellationToken.None, progress, backupReport));

            if (result.Success)
            {
                AppendLine(Resources.Maintenance_RepairDone);
            }
            else
            {
                AppendLine(Resources.Maintenance_RepairFailed);
                if (!string.IsNullOrEmpty(result.ErrorMessage))
                    AppendLine("  " + result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "MaintenanceViewModel: OptimizeAndRepairAsync failed");
            AppendLine(Resources.Maintenance_RepairFailed);
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void Close() => CloseDialog?.Invoke();

    private IProgress<MaintenanceStep> StepProgress()
        => new Progress<MaintenanceStep>(step =>
            Dispatcher.UIThread.Post(() => AppendLine(MaintenanceText.Describe(step))));

    private void AppendLine(string line) => LogText += line + Environment.NewLine;
}
