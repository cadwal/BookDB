using System;
using BookDB.Desktop.Services;
using BookDB.Models;
using Xunit;

namespace BookDB.Desktop.Tests.Services;

public sealed class BackupProgressLocalizerTests
{
    // Every step must map to a non-empty status string, so a newly added BackupProgressStep can never reach the
    // progress window unlocalized (or throw the unmapped-step guard).
    [Fact]
    public void ToDisplayString_MapsEveryStepToNonEmptyText()
    {
        foreach (var step in Enum.GetValues<BackupProgressStep>())
        {
            var text = BackupProgressLocalizer.ToDisplayString(new ProgressUpdate<BackupProgressStep>(step, 1, 2));
            Assert.False(string.IsNullOrWhiteSpace(text), $"Step {step} has no localized status text.");
        }
    }

    // The counted step formats Current/Total into its status string.
    [Fact]
    public void ToDisplayString_CoverImagesCount_FormatsCounts()
    {
        var text = BackupProgressLocalizer.ToDisplayString(
            new ProgressUpdate<BackupProgressStep>(BackupProgressStep.ExportingCoverImagesCount, 3, 7));

        Assert.Contains("3", text);
        Assert.Contains("7", text);
    }

    // A null sink yields no adapter; a real sink receives the localized string.
    [Fact]
    public void Localizing_NullSink_ReturnsNull()
        => Assert.Null(BackupProgressLocalizer.Localizing(null));

    [Fact]
    public void Localizing_ForwardsLocalizedTextToSink()
    {
        string? reported = null;
        var adapter = BackupProgressLocalizer.Localizing(new DelegateProgress(s => reported = s));

        adapter!.Report(new ProgressUpdate<BackupProgressStep>(BackupProgressStep.CreatingArchive));

        Assert.Equal(BackupProgressLocalizer.ToDisplayString(
            new ProgressUpdate<BackupProgressStep>(BackupProgressStep.CreatingArchive)), reported);
    }

    private sealed class DelegateProgress : IProgress<string>
    {
        private readonly Action<string> _onReport;
        public DelegateProgress(Action<string> onReport) => _onReport = onReport;
        public void Report(string value) => _onReport(value);
    }
}
