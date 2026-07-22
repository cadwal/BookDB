using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using BookDB.Data.DbContexts;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Models.Entities;
using Microsoft.EntityFrameworkCore;
using ScottPlot.Avalonia;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// The rebuilt statistics window: the growth line and per-year bars render, the five horizontal-bar cards render,
/// the full-tables expander holds one grid per chart (growth, per-year, and the five breakdowns), and the card
/// grid reflows to a single column when the window is narrow. All of it runs under the binding-error gate (RunUi).
/// </summary>
public class StatisticsWindowLayoutTests : HeadlessTest
{
    [Fact]
    public async Task WideWindow_RendersEveryChartAndTwoColumnCards()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedAsync(host, ct);

            var vm = host.Resolve<StatisticsWindowViewModel>();
            Assert.True(await vm.TryRefreshAsync());
            var window = new StatisticsWindow { DataContext = vm, Width = 800 };
            window.Show();
            Ui.Pump();
            try
            {
                var plots = window.Descendants<AvaPlot>();
                // Growth line + per-year bars + five horizontal-bar cards.
                Assert.Equal(7, plots.Count);

                var scatters = plots.Count(p => p.Plot.GetPlottables().OfType<ScottPlot.Plottables.Scatter>().Any());
                var barPlots = plots.Count(p => p.Plot.GetPlottables().OfType<ScottPlot.Plottables.BarPlot>().Any());
                Assert.True(scatters >= 1, "the growth line should render a scatter");
                Assert.True(barPlots >= 5, "the per-year chart and the populated cards should render bars");

                // Two-column card grid at full width.
                var cards = window.Descendants<UniformGrid>().Single();
                Assert.Equal(2, cards.Columns);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task NarrowWindow_CollapsesCardsToOneColumn()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedAsync(host, ct);

            var vm = host.Resolve<StatisticsWindowViewModel>();
            Assert.True(await vm.TryRefreshAsync());
            var window = new StatisticsWindow { DataContext = vm, Width = 480 };
            window.Show();
            Ui.Pump();
            try
            {
                var cards = window.Descendants<UniformGrid>().Single();
                Assert.Equal(1, cards.Columns);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task FullTablesExpander_HoldsATableForEveryChart()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedAsync(host, ct);

            var vm = host.Resolve<StatisticsWindowViewModel>();
            Assert.True(await vm.TryRefreshAsync());
            var window = new StatisticsWindow { DataContext = vm, Width = 800 };
            window.Show();
            Ui.Pump();
            try
            {
                // Collapsed by default: the grids aren't realized yet.
                Assert.Empty(window.Descendants<DataGrid>());

                var expander = window.Descendants<Expander>().Single();
                expander.IsExpanded = true;
                Ui.Pump();

                // One grid per chart: library growth, books-per-year, formats, collections, languages,
                // top authors, published years.
                Assert.Equal(7, window.Descendants<DataGrid>().Count);
                Assert.Contains(window.Descendants<DataGrid>(),
                    g => ReferenceEquals(g.ItemsSource, vm.FormatBreakdown) && vm.FormatBreakdown.Count > 0);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>A handful of books with formats, languages, a collection, and an author so every card has bars.</summary>
    private static async Task SeedAsync(TestHost host, CancellationToken ct)
    {
        var withAuthor = await SeedData.AddBookAsync(host, "Layout One", new[] { "Ada Author" }, ct);
        var two = await SeedData.AddBookAsync(host, "Layout Two", ct);
        var shelf = await SeedData.AddCollectionAsync(host, "Layout Shelf", ct);

        var factory = host.Resolve<IDbContextFactory<BookDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        var hardcover = new Format { Name = "Layout Hardcover" };
        var paperback = new Format { Name = "Layout Paperback" };
        var english = new Language { Name = "Layout English" };
        db.Formats.AddRange(hardcover, paperback);
        db.Languages.Add(english);
        await db.SaveChangesAsync(ct);

        foreach (var (bookId, format) in new[] { (withAuthor.BookId, hardcover), (two.BookId, paperback) })
        {
            var book = await db.Books.AsTracking().SingleAsync(b => b.BookId == bookId, ct);
            book.FormatId = format.FormatId;
            book.LanguageId = english.LanguageId;
            book.PubDate = "2001";
            book.CollectionId = shelf.CollectionId;
        }
        await db.SaveChangesAsync(ct);
    }
}
