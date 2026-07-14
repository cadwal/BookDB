using System;
using BookDB.Desktop.Localization;
using BookDB.Models;

namespace BookDB.Desktop.Services;

/// <summary>
/// Maps a typed <see cref="CsvExportProgressStep"/> update to its localized status string for the progress
/// window. The export runs in the Logic layer emitting steps; localization stays here in the Desktop layer.
/// </summary>
public static class CsvExportProgressLocalizer
{
    public static string ToDisplayString(ProgressUpdate<CsvExportProgressStep> update) => update.Step switch
    {
        CsvExportProgressStep.Querying => Resources.Export_Status_Querying,
        CsvExportProgressStep.WritingBooks => string.Format(Resources.Export_Status_WritingBooks, update.Current),
        CsvExportProgressStep.WritingRow => string.Format(Resources.Export_Status_WritingRow, update.Current, update.Total),
        _ => throw new ArgumentOutOfRangeException(
            nameof(update), update.Step, "Unmapped CSV export progress step."),
    };

    /// <summary>
    /// Wraps a string progress sink (the progress window) so the export can report typed steps; each step is
    /// localized here before it reaches the window. Returns null when there is no sink.
    /// </summary>
    public static IProgress<ProgressUpdate<CsvExportProgressStep>>? Localizing(IProgress<string>? sink)
        => sink is null ? null : new StepSink(sink);

    private sealed class StepSink : IProgress<ProgressUpdate<CsvExportProgressStep>>
    {
        private readonly IProgress<string> _sink;
        public StepSink(IProgress<string> sink) => _sink = sink;
        public void Report(ProgressUpdate<CsvExportProgressStep> value) => _sink.Report(ToDisplayString(value));
    }
}
