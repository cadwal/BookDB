using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using BookDB.Desktop.Messages;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using CommunityToolkit.Mvvm.Messaging;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// The cover-grid alternate view of the book list: a persisted toggle switches the list DataGrid for a
/// tile grid over the same Books collection, selection feeds the shared SelectedBooks, double-click opens
/// the book in edit mode (as the list does), and tile covers decode on demand only in grid mode.
/// </summary>
public class CoverGridViewTests : HeadlessTest
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M8AAAMBAQDJ/pLvAAAAAElFTkSuQmCC");

    private static (BookListView View, Window Window) ShowList(BookListViewModel vm)
    {
        var view = new BookListView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 640 };
        window.Show();
        Ui.Pump();
        return (view, window);
    }

    [Fact]
    public Task TogglingToGrid_ShowsTheGrid_HidesTheList_AndRendersEveryBook() => RunUi(async () =>
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = TestHost.Create();
        await SeedData.AddBookAsync(host, "Dune", ct);
        await SeedData.AddBookAsync(host, "Ubik", ct);
        await SeedData.AddBookAsync(host, "Neuromancer", ct);

        var vm = host.Resolve<BookListViewModel>();
        await vm.LoadBooksAsync(ct);
        var (view, window) = ShowList(vm);

        var dataGrid = view.Find<DataGrid>("BooksGrid");
        var grid = view.Find<ListBox>("BooksGridView");

        Assert.True(dataGrid.IsVisible);
        Assert.False(grid.IsVisible);

        vm.ShowGridViewCommand.Execute(null);
        Ui.Pump();

        Assert.False(dataGrid.IsVisible);
        Assert.True(grid.IsVisible);
        // Grid rides the same collection as the list — every book renders, filter/search reflect for free.
        Assert.Equal(vm.Books.Count, grid.ItemCount);
        Assert.Equal(3, grid.ItemCount);

        window.Close();
    });

    [Fact]
    public Task TogglingGridView_Persists_AndSelectionFeedsSelectedBooks() => RunUi(async () =>
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = TestHost.Create();
        await SeedData.AddBookAsync(host, "Foundation", ct);
        await SeedData.AddBookAsync(host, "Hyperion", ct);

        var vm = host.Resolve<BookListViewModel>();
        await vm.LoadBooksAsync(ct);
        var (view, window) = ShowList(vm);

        vm.ShowGridViewCommand.Execute(null);

        var settings = host.Resolve<ISettingsService>();
        string? mode = null;
        for (int i = 0; i < 25 && mode != "Grid"; i++)
        {
            Ui.Pump();
            mode = await settings.GetAsync("BookList_ViewMode", ct);
            if (mode != "Grid") await Task.Delay(20, ct);
        }
        Assert.Equal("Grid", mode);

        var grid = view.Find<ListBox>("BooksGridView");
        grid.SelectedIndex = 0;
        Ui.Pump();
        Assert.Single(vm.SelectedBooks);
        Assert.Equal(vm.Books[0].BookId, vm.SelectedBooks[0].BookId);

        window.Close();
    });

    [Fact]
    public Task DoubleClickingATile_OpensTheBookInEditMode() => RunUi(async () =>
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = TestHost.Create();
        await SeedData.AddBookAsync(host, "Snow Crash", ct);

        var vm = host.Resolve<BookListViewModel>();
        await vm.LoadBooksAsync(ct);
        vm.IsGridView = true;
        var (view, window) = ShowList(vm);

        var grid = view.Find<ListBox>("BooksGridView");
        grid.SelectedIndex = 0;
        Ui.Pump();

        BookSelectedMessage? opened = null;
        var recipient = new object();
        WeakReferenceMessenger.Default.Register<BookSelectedMessage>(
            recipient, (_, m) => { if (m.OpenInEditMode) opened = m; });
        try
        {
            var container = grid.ContainerFromIndex(0)!;
            window.DoubleClick(container);
            Ui.Pump();
        }
        finally
        {
            WeakReferenceMessenger.Default.Unregister<BookSelectedMessage>(recipient);
        }

        Assert.NotNull(opened);
        Assert.Equal(vm.Books[0].BookId, opened!.Value);

        window.Close();
    });

    [Fact]
    public Task ScrollingTheGridToTheBottom_LoadsTheNextPage() => RunUi(async () =>
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = TestHost.Create();
        for (int i = 1; i <= 150; i++)
            await SeedData.AddBookAsync(host, $"Book {i:D3}", ct);

        var vm = host.Resolve<BookListViewModel>();
        await vm.LoadBooksAsync(ct);
        Assert.Equal(100, vm.Books.Count);   // first page only
        Assert.False(vm.IsAllLoaded);

        vm.IsGridView = true;
        var (view, window) = ShowList(vm);
        var grid = view.Find<ListBox>("BooksGridView");
        Ui.Pump();

        // Scroll to the bottom — the grid must page in the rest, like the list does.
        var scrollViewer = grid.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(scrollViewer.Extent.Height > scrollViewer.Viewport.Height, "grid content should be scrollable");
        scrollViewer.Offset = new Vector(0, scrollViewer.Extent.Height);
        Ui.Pump();

        await Ui.PumpUntil(() => vm.Books.Count > 100, ct);
        Assert.True(vm.Books.Count > 100);

        window.Close();
    });

    [Fact]
    public Task GridTiles_DecodeCovers_OnlyInGridMode() => RunUi(async () =>
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = TestHost.Create();
        var book = await SeedData.AddBookAsync(host, "Illustrated", ct);
        await host.Resolve<IBookImageService>().SavePrimaryBookImageAsync(book.BookId, OnePixelPng, ct);

        var vm = host.Resolve<BookListViewModel>();
        await vm.LoadBooksAsync(ct);
        var row = vm.Books.Single(b => b.BookId == book.BookId);
        Assert.Null(row.GridThumbnail);   // list mode: no tile decode

        vm.IsGridView = true;             // switching to grid decodes tile covers on demand
        await Ui.PumpUntil(() => row.GridThumbnail is not null, ct);
        Assert.NotNull(row.GridThumbnail);
    });

    /// <summary>
    /// The grid shipped with no context menu at all: the menu was declared inline on the DataGrid, so every
    /// existing test reached it via the list and none could see it missing from the second view. Both views
    /// must share the one menu instance — that is what keeps their twelve actions from drifting apart.
    /// </summary>
    [Fact]
    public Task BothViews_ShareTheOneContextMenu() => RunUi(async () =>
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = TestHost.Create();
        await SeedData.AddBookAsync(host, "Dune", ct);

        var vm = host.Resolve<BookListViewModel>();
        await vm.LoadBooksAsync(ct);
        var (view, window) = ShowList(vm);

        var listMenu = view.Find<DataGrid>("BooksGrid").ContextMenu;
        var gridMenu = view.Find<ListBox>("BooksGridView").ContextMenu;

        Assert.NotNull(listMenu);
        Assert.NotNull(gridMenu);
        Assert.Same(listMenu, gridMenu);
        Assert.NotEmpty(listMenu!.Items.OfType<MenuItem>());

        window.Close();
    });
}
