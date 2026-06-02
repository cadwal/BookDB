using System.Collections.Generic;
using BookDB.Desktop.ViewModels;
using BookDB.Models;
using Xunit;

namespace BookDB.Desktop.Tests;

/// <summary>
/// Verifies the startup progress channel and the stage→percent mapping that drives the
/// splash screen progress bar. Assertions are purely numeric/structural so they are safe
/// regardless of the host machine's UI culture.
/// </summary>
public class StartupProgressTests
{
    [Fact]
    public void Reporter_Report_RaisesProgressChangedOnceWithGivenValues()
    {
        var reporter = new StartupProgressReporter();
        var received = new List<StartupProgressReport>();
        reporter.ProgressChanged += received.Add;

        reporter.Report(StartupStage.ApplyingMigrations, 2, 5);

        var report = Assert.Single(received);
        Assert.Equal(StartupStage.ApplyingMigrations, report.Stage);
        Assert.Equal(2, report.Current);
        Assert.Equal(5, report.Total);
    }

    [Fact]
    public void Reporter_Report_DefaultsCurrentAndTotalToZero()
    {
        var reporter = new StartupProgressReporter();
        StartupProgressReport? captured = null;
        reporter.ProgressChanged += r => captured = r;

        reporter.Report(StartupStage.Initializing);

        Assert.NotNull(captured);
        Assert.Equal(0, captured!.Value.Current);
        Assert.Equal(0, captured.Value.Total);
    }

    [Theory]
    [InlineData(StartupStage.Initializing, 0, 0, 0.0)]
    [InlineData(StartupStage.ApplyingMigrations, 0, 0, 55.0)]   // nothing to migrate → snap to span end
    [InlineData(StartupStage.ApplyingMigrations, 0, 2, 5.0)]    // start of migration span
    [InlineData(StartupStage.ApplyingMigrations, 1, 2, 30.0)]   // halfway through migrations
    [InlineData(StartupStage.ApplyingMigrations, 2, 2, 55.0)]   // all scripts applied
    [InlineData(StartupStage.LoadingLibrary, 0, 0, 55.0)]
    [InlineData(StartupStage.RestoringSession, 0, 0, 85.0)]
    [InlineData(StartupStage.Finishing, 0, 0, 100.0)]
    public void ToPercent_MapsStageToExpectedPercentage(
        StartupStage stage, int current, int total, double expected)
    {
        Assert.Equal(expected, SplashProgressMath.ToPercent(stage, current, total));
    }

    [Fact]
    public void ToPercent_IsMonotonicNonDecreasingAcrossStages()
    {
        var sequence = new[]
        {
            SplashProgressMath.ToPercent(StartupStage.Initializing, 0, 0),
            SplashProgressMath.ToPercent(StartupStage.ApplyingMigrations, 0, 1),
            SplashProgressMath.ToPercent(StartupStage.ApplyingMigrations, 1, 1),
            SplashProgressMath.ToPercent(StartupStage.LoadingLibrary, 0, 0),
            SplashProgressMath.ToPercent(StartupStage.RestoringSession, 0, 0),
            SplashProgressMath.ToPercent(StartupStage.Finishing, 0, 0),
        };

        for (var i = 1; i < sequence.Length; i++)
            Assert.True(sequence[i] >= sequence[i - 1], $"Percent decreased at index {i}");
    }
}
