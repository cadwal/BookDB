using System;
using BookDB.Data.Interfaces;
using BookDB.Logic.Services;
using Serilog;

namespace BookDB.Desktop.Helpers;

public static class ConnectionLossReporting
{
    /// <summary>
    /// When <paramref name="ex"/> is a dropped remote connection, reports it to the shared status-bar health
    /// indicator (the monitor then retries in the background) and returns true so the caller can skip its
    /// operation-specific error handling. Ordinary errors return false and are left to the caller.
    /// </summary>
    public static bool ReportIfConnectionLoss(
        this IConnectionHealthMonitor monitor, IConnectionFailureClassifier classifier, Exception ex)
    {
        if (!classifier.IsConnectionLoss(ex))
            return false;
        // Breadcrumb (Warning so it reaches the file sink): records the exact exception that flipped the status
        // bar to reconnecting, so an unexpected trip — e.g. a slow connect during print — can be diagnosed.
        Log.Warning(
            "Remote connection loss reported to health monitor ({ExceptionType} / inner {InnerType}): {Detail}",
            ex.GetType().Name, ex.InnerException?.GetType().Name ?? "none", ex.Message);
        monitor.ReportConnectionFailure();
        return true;
    }
}
