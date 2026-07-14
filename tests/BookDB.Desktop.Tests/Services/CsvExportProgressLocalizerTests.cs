using System;
using BookDB.Desktop.Services;
using BookDB.Models;
using Xunit;

namespace BookDB.Desktop.Tests.Services;

public sealed class CsvExportProgressLocalizerTests
{
    // Every step must map to a non-empty status string, so a newly added CsvExportProgressStep can never reach
    // the progress window unlocalized (or throw the unmapped-step guard).
    [Fact]
    public void ToDisplayString_MapsEveryStepToNonEmptyText()
    {
        foreach (var step in Enum.GetValues<CsvExportProgressStep>())
        {
            var text = CsvExportProgressLocalizer.ToDisplayString(new ProgressUpdate<CsvExportProgressStep>(step, 1, 2));
            Assert.False(string.IsNullOrWhiteSpace(text), $"Step {step} has no localized status text.");
        }
    }

    // The counted steps format their counts into the status string.
    [Fact]
    public void ToDisplayString_WritingBooks_FormatsCount()
    {
        var text = CsvExportProgressLocalizer.ToDisplayString(
            new ProgressUpdate<CsvExportProgressStep>(CsvExportProgressStep.WritingBooks, 42));

        Assert.Contains("42", text);
    }

    [Fact]
    public void ToDisplayString_WritingRow_FormatsCurrentAndTotal()
    {
        var text = CsvExportProgressLocalizer.ToDisplayString(
            new ProgressUpdate<CsvExportProgressStep>(CsvExportProgressStep.WritingRow, 3, 7));

        Assert.Contains("3", text);
        Assert.Contains("7", text);
    }

    // A null sink yields no adapter; a real sink receives the localized string.
    [Fact]
    public void Localizing_NullSink_ReturnsNull()
        => Assert.Null(CsvExportProgressLocalizer.Localizing(null));

    [Fact]
    public void Localizing_ForwardsLocalizedTextToSink()
    {
        string? reported = null;
        var adapter = CsvExportProgressLocalizer.Localizing(new DelegateProgress(s => reported = s));

        adapter!.Report(new ProgressUpdate<CsvExportProgressStep>(CsvExportProgressStep.Querying));

        Assert.Equal(CsvExportProgressLocalizer.ToDisplayString(
            new ProgressUpdate<CsvExportProgressStep>(CsvExportProgressStep.Querying)), reported);
    }

    private sealed class DelegateProgress : IProgress<string>
    {
        private readonly Action<string> _onReport;
        public DelegateProgress(Action<string> onReport) => _onReport = onReport;
        public void Report(string value) => _onReport(value);
    }
}
