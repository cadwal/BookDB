using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Data.DbContexts;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Statistics content over a seeded library: the totals, every breakdown table, and the per-year chart draw
/// real counts from the database, render in the window, and follow the data when it changes (Refresh).
/// </summary>
public class StatisticsFlowTests : HeadlessTest
{
    [Fact]
    public async Task SeededLibrary_ShowsRealCountsInEveryBreakdown()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedLibraryAsync(host, ct);

            var vm = host.Resolve<StatisticsWindowViewModel>();
            Assert.True(await vm.TryRefreshAsync());
            var window = new StatisticsWindow { DataContext = vm };
            window.Show();
            Ui.Pump();

            Assert.Equal(3, vm.TotalBooks);
            Assert.Contains(window.Descendants<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "3");

            // Each breakdown draws from its own field, with counts ordered largest-first and shown as "N (P%)".
            Assert.Equal(("Stats Hardcover", 2, "2 (66.7%)"), Row(vm.FormatBreakdown, 0));
            Assert.Equal(("Stats Paperback", 1, "1 (33.3%)"), Row(vm.FormatBreakdown, 1));
            Assert.Equal(("Stats Elvish", 2, "2 (66.7%)"), Row(vm.LanguageBreakdown, 0));
            Assert.Equal(("Stats Klingon", 1, "1 (33.3%)"), Row(vm.LanguageBreakdown, 1));
            Assert.Contains(vm.CollectionBreakdown, r => r.Label == "Stats Shelf" && r.Count == 2);

            // The published-year table only counts books that carry a date, ordered by year.
            Assert.Equal(new[] { ("1990", 2), ("2001", 1) },
                vm.PublishedYearBreakdown.Select(r => (r.Label, r.Count)));

            // The chart groups by the year the books were added — all three were added today, so one bar of 3.
            var point = Assert.Single(vm.BooksPerYear);
            Assert.Equal(3, point.Count);

            // The breakdown rows render in the window's tables, not just on the VM.
            Assert.Contains(window.Descendants<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == "Stats Hardcover");
            Assert.Contains(window.Descendants<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == "2 (66.7%)");
            Assert.Contains(window.Descendants<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "1990");

            // Refresh follows the data: a fourth book shifts the total and the format shares.
            await SeedData.AddBookAsync(host, "Stats Four", ct);
            await Ui.ClickAsync(window.ButtonFor(vm.RefreshCommand));
            Assert.Equal(4, vm.TotalBooks);
            Assert.Contains(vm.FormatBreakdown, r => r.Label == "Stats Hardcover" && r.Count == 2 && r.CountText == "2 (50.0%)");
            window.Close();
        });
    }

    // ─── Plumbing ────────────────────────────────────────────────────────────

    private static (string Label, int Count, string CountText) Row(
        System.Collections.ObjectModel.ObservableCollection<BreakdownRowDisplay> rows, int index) =>
        (rows[index].Label, rows[index].Count, rows[index].CountText);

    /// <summary>Three books through the Logic write path (so Added is real), then the fields the breakdowns
    /// group by — two share a format/language/year/collection, the third holds its own value in each.</summary>
    private static async Task SeedLibraryAsync(TestHost host, CancellationToken ct)
    {
        var one = await SeedData.AddBookAsync(host, "Stats One", ct);
        var two = await SeedData.AddBookAsync(host, "Stats Two", ct);
        var three = await SeedData.AddBookAsync(host, "Stats Three", ct);
        var shelf = await SeedData.AddCollectionAsync(host, "Stats Shelf", ct);

        var factory = host.Resolve<IDbContextFactory<BookDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        var hardcover = new Format { Name = "Stats Hardcover" };
        var paperback = new Format { Name = "Stats Paperback" };
        var elvish = new Language { Name = "Stats Elvish" };
        var klingon = new Language { Name = "Stats Klingon" };
        db.Formats.AddRange(hardcover, paperback);
        db.Languages.AddRange(elvish, klingon);
        await db.SaveChangesAsync(ct);

        foreach (var (bookId, format, language, pubDate, collectionId) in new[]
        {
            (one.BookId, hardcover, elvish, "1990", (int?)shelf.CollectionId),
            (two.BookId, hardcover, elvish, "1990", shelf.CollectionId),
            (three.BookId, paperback, klingon, "2001", null),
        })
        {
            // The context defaults to NoTracking — opt in so the field updates actually save.
            var book = await db.Books.AsTracking().SingleAsync(b => b.BookId == bookId, ct);
            book.FormatId = format.FormatId;
            book.LanguageId = language.LanguageId;
            book.PubDate = pubDate;
            book.CollectionId = collectionId;
        }
        await db.SaveChangesAsync(ct);
    }
}
