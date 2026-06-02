using System;
using BookDB.Models;

namespace BookDB.Desktop.ViewModels;

/// <summary>
/// Pure mapping from a startup stage (and its optional sub-progress) to an overall
/// 0–100 percentage for the splash progress bar. Extracted as a static function so it
/// can be unit-tested without a UI or a specific culture.
/// </summary>
public static class SplashProgressMath
{
    // Cumulative weight boundaries per stage (start, end) on the 0–100 scale.
    // Each stage snaps to its start on entry; ApplyingMigrations fills its span by current/total.
    private const double InitializingEnd = 5.0;
    private const double MigrationsEnd = 55.0;
    private const double LoadingLibraryEnd = 85.0;
    private const double RestoringSessionEnd = 98.0;

    public static double ToPercent(StartupStage stage, int current, int total)
    {
        return stage switch
        {
            StartupStage.Initializing => 0.0,
            StartupStage.ApplyingMigrations => MigrationsPercent(current, total),
            StartupStage.LoadingLibrary => MigrationsEnd,
            StartupStage.RestoringSession => LoadingLibraryEnd,
            StartupStage.Finishing => 100.0,
            _ => 0.0
        };
    }

    private static double MigrationsPercent(int current, int total)
    {
        // No pending scripts (nothing to migrate) → snap straight to the end of the migration span.
        if (total <= 0)
            return MigrationsEnd;

        var fraction = Math.Clamp((double)current / total, 0.0, 1.0);
        return InitializingEnd + (MigrationsEnd - InitializingEnd) * fraction;
    }
}
