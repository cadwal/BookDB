using System;
using BookDB.Desktop.Localization;
using BookDB.Models;

namespace BookDB.Desktop.Services;

/// <summary>
/// Maps a typed <see cref="PrintProgressStep"/> update to its localized status string for the print dialog's
/// status line. PDF generation runs in the Logic layer emitting steps; localization stays here in the Desktop
/// layer.
/// </summary>
public static class PrintProgressLocalizer
{
    public static string ToDisplayString(ProgressUpdate<PrintProgressStep> update) => update.Step switch
    {
        PrintProgressStep.Querying => Resources.Print_Status_Querying,
        PrintProgressStep.GeneratingPdf => string.Format(Resources.Print_Status_GeneratingPdf, update.Current),
        _ => throw new ArgumentOutOfRangeException(
            nameof(update), update.Step, "Unmapped print progress step."),
    };
}
