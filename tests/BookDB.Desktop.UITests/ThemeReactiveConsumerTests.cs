using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using BookDB.Desktop.Converters;
using BookDB.Desktop.Theming;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// With the theme applying at runtime, the surfaces must actually follow it. View bindings flipped to
/// DynamicResource recolour on their own; the imperative consumers (status-badge converter, loan-status brush) that
/// DynamicResource can't reach re-resolve on the ThemeAppliedMessage. Each test restores the Default flavour — the
/// headless app is shared across the session.
/// </summary>
public class ThemeReactiveConsumerTests : HeadlessTest
{
    [Fact]
    public async Task ToolbarBackground_FollowsTheFlavour_ViaDynamicResource()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var list = host.Resolve<BookListViewModel>();
            var view = new BookListView { DataContext = list };
            var window = view.Host();
            await list.LoadBooksAsync(ct);
            Ui.Pump();
            try
            {
                // The 36px toolbar strip is backed by BrushBackgroundAlt (Default #f5f5f5).
                var toolbar = view.Descendants<Border>().First(b => b.Height == 36);
                Assert.Equal(Color.Parse("#f5f5f5"), (toolbar.Background as ISolidColorBrush)?.Color);

                ThemeApplier.Apply(ThemeFlavour.Vibrant);
                Ui.Pump();

                Assert.Equal(Color.Parse("#eff4fb"), (toolbar.Background as ISolidColorBrush)?.Color);
            }
            finally
            {
                ThemeApplier.Apply(ThemeFlavour.Default);
                window.Close();
            }
        });
    }

    [Fact]
    public async Task StatusBadgeConverter_ResolvesTheFlavoursBrush()
    {
        await RunUi(() =>
        {
            try
            {
                ThemeApplier.Apply(ThemeFlavour.Default);
                var def = (ISolidColorBrush)StatusBadgeColorConverter.Instance
                    .Convert("Reading", typeof(IBrush), null, CultureInfo.InvariantCulture)!;
                Assert.Equal(Color.Parse("#1565c0"), def.Color);

                ThemeApplier.Apply(ThemeFlavour.Vibrant);
                var vib = (ISolidColorBrush)StatusBadgeColorConverter.Instance
                    .Convert("Reading", typeof(IBrush), null, CultureInfo.InvariantCulture)!;
                Assert.Equal(Color.Parse("#2563eb"), vib.Color);
            }
            finally
            {
                ThemeApplier.Apply(ThemeFlavour.Default);
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ApplyingAFlavour_ReraisesEachRowsStatusBadgeBinding()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedData.AddBookAsync(host, "Badge Book", ct);
            var list = host.Resolve<BookListViewModel>();
            await list.LoadBooksAsync(ct);
            var row = list.Books.Single();

            var raised = false;
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(BookRowViewModel.StatusDisplay))
                    raised = true;
            };
            try
            {
                // Whole chain: applier → ThemeAppliedMessage → BookListViewModel.Receive → row refresh.
                ThemeApplier.Apply(ThemeFlavour.Vibrant);
                Assert.True(raised);
            }
            finally
            {
                ThemeApplier.Apply(ThemeFlavour.Default);
            }
        });
    }

    [Fact]
    public async Task LoanStatusForeground_FollowsTheFlavour()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var book = await SeedData.AddBookAsync(host, "Loaned Out", ct);
            var borrower = await SeedData.AddBorrowerAsync(host, "Ada", "Lovelace", ct);
            await host.Resolve<ILoanService>().CheckOutAsync(book.BookId, borrower.BorrowerId, dueDate: null, ct);

            var detail = host.Resolve<BookDetailViewModel>();
            await detail.LoadBookAsync(book.BookId);
            try
            {
                // Active (not overdue) loan → BrushTextSecondary (Default #595959).
                Assert.Equal(Color.Parse("#595959"), (detail.LoanStatusForeground as ISolidColorBrush)?.Color);

                ThemeApplier.Apply(ThemeFlavour.HighContrast);

                Assert.Equal(Color.Parse("#1a1a1a"), (detail.LoanStatusForeground as ISolidColorBrush)?.Color);
            }
            finally
            {
                ThemeApplier.Apply(ThemeFlavour.Default);
            }
        });
    }
}
