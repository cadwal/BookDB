using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BookDB.Desktop.Localization;
using BookDB.Logic.Services;
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

    /// <summary>Closes the dialog. Set by <c>WindowService</c>.</summary>
    public Action? CloseDialog { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(OptimizeAndRepairCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string _logText = string.Empty;

    public MaintenanceViewModel(IDatabaseMaintenanceService maintenanceService)
        => _maintenanceService = maintenanceService;

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
        try
        {
            var result = await Task.Run(
                () => _maintenanceService.OptimizeAndRepairAsync(CancellationToken.None, progress));

            if (result.Success)
            {
                AppendLine(Resources.Maintenance_RepairDone);
                if (!string.IsNullOrEmpty(result.SafetyBackupPath))
                    AppendLine(string.Format(Resources.Maintenance_SafetyBackupSaved, result.SafetyBackupPath));
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
