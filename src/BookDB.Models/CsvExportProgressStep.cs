namespace BookDB.Models;

/// <summary>
/// Steps of a CSV export, reported as a typed <see cref="ProgressUpdate{TStep}"/> so the export carries no
/// localization dependency. The Desktop layer maps each step to a status string for the progress window.
/// </summary>
public enum CsvExportProgressStep
{
    Querying,
    // Reports Current = total book count.
    WritingBooks,
    // Reports Current/Total = row written so far / total.
    WritingRow,
}
