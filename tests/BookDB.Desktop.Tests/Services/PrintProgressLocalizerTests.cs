using System;
using BookDB.Desktop.Services;
using BookDB.Models;
using Xunit;

namespace BookDB.Desktop.Tests.Services;

public sealed class PrintProgressLocalizerTests
{
    // Every step must map to a non-empty status string, so a newly added PrintProgressStep can never reach
    // the print dialog's status line unlocalized (or throw the unmapped-step guard).
    [Fact]
    public void ToDisplayString_MapsEveryStepToNonEmptyText()
    {
        foreach (var step in Enum.GetValues<PrintProgressStep>())
        {
            var text = PrintProgressLocalizer.ToDisplayString(new ProgressUpdate<PrintProgressStep>(step, 1, 2));
            Assert.False(string.IsNullOrWhiteSpace(text), $"Step {step} has no localized status text.");
        }
    }

    // The counted step formats the book count into its status string.
    [Fact]
    public void ToDisplayString_GeneratingPdf_FormatsCount()
    {
        var text = PrintProgressLocalizer.ToDisplayString(
            new ProgressUpdate<PrintProgressStep>(PrintProgressStep.GeneratingPdf, 42));

        Assert.Contains("42", text);
    }
}
