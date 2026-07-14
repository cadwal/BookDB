namespace BookDB.Models;

/// <summary>
/// Steps of a print-list PDF generation, reported as a typed <see cref="ProgressUpdate{TStep}"/> so the
/// generation carries no localization dependency. Two steps only — QuestPDF renders the document as a single
/// opaque call, so there is no honest finer granularity. The Desktop layer maps each step to a status string
/// for the print dialog.
/// </summary>
public enum PrintProgressStep
{
    Querying,
    // Reports Current = total book count.
    GeneratingPdf,
}
