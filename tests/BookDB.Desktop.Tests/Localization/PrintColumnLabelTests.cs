using System.Globalization;
using BookDB.Desktop.Localization;
using BookDB.Logic.Services;
using Xunit;

namespace BookDB.Desktop.Tests.Localization;

public sealed class PrintColumnLabelTests
{
    // Every column the print dialog offers must resolve a Print_Column_* header label, so no PDF header can
    // silently fall back to the raw column key. Invariant culture reads the neutral resources; the locale
    // files are kept in sync by ResourceKeySyncTests.
    [Fact]
    public void EveryOfferedColumn_ResolvesAHeaderLabel()
    {
        IPrintService printService = new PrintService(null!);
        foreach (var col in printService.AllColumnNames)
        {
            var label = Resources.ResourceManager.GetString("Print_Column_" + col, CultureInfo.InvariantCulture);
            Assert.False(string.IsNullOrWhiteSpace(label), $"Missing Print_Column_{col} resource.");
        }
    }
}
