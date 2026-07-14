using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Help;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// WindowService tracks every non-modal secondary window in one list, from which reuse/dedup, the Window
/// menu, and shutdown all derive. These tests drive the public open methods and read the Window-menu entries
/// on MainWindowViewModel: opening the same book twice reuses its window (no second entry), distinct books
/// get their own, the Help window is a singleton that re-targets its tab on reuse, an entry's title tracks
/// its window's title, and CloseAllSecondaryWindows clears the whole set.
/// </summary>
public class WindowServiceRegistryTests : HeadlessTest
{
    [Fact]
    public async Task FullDetails_DedupsPerBook_AndCloseAllClearsThem()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var ws = host.Resolve<IWindowService>();
            var mainVm = host.Resolve<MainWindowViewModel>();

            var alpha = await SeedData.AddBookAsync(host, "Alpha", ct);
            var beta = await SeedData.AddBookAsync(host, "Beta", ct);

            await ws.OpenFullDetailsWindowAsync(alpha.BookId);
            Ui.Pump();
            Assert.Single(WindowEntries(mainVm));

            // Same book again reuses the open window rather than listing a second entry.
            await ws.OpenFullDetailsWindowAsync(alpha.BookId);
            Ui.Pump();
            Assert.Single(WindowEntries(mainVm));

            // A different book opens its own window.
            await ws.OpenFullDetailsWindowAsync(beta.BookId);
            Ui.Pump();
            Assert.Equal(2, WindowEntries(mainVm).Count);
            Assert.All(WindowEntries(mainVm), e => Assert.Equal(WindowCategory.BookEdit, e.Category));

            ws.CloseAllSecondaryWindows();
            Ui.Pump();
            Assert.Empty(WindowEntries(mainVm));
        });
    }

    [Fact]
    public async Task Help_IsASingleton_RetargetsTabOnReuse_AndEntryTracksTitle()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var main = host.Resolve<MainWindow>();
            await ((MainWindowViewModel)main.DataContext!).InitializeAsync();
            main.Show();
            Ui.Pump();
            var ws = host.Resolve<IWindowService>();
            var mainVm = host.Resolve<MainWindowViewModel>();

            ws.OpenHelpWindow(HelpTab.KeyboardShortcuts);
            Ui.Pump();
            var help = main.OwnedWindows.OfType<HelpWindow>().Single();
            Assert.Single(WindowEntries(mainVm));

            // Reuse: no second window, and the request re-targets the tab of the existing one.
            ws.OpenHelpWindow(HelpTab.RemoteDatabases);
            Ui.Pump();
            Assert.Single(main.OwnedWindows.OfType<HelpWindow>());
            Assert.Equal((int)HelpTab.RemoteDatabases, ((HelpWindowViewModel)help.DataContext!).SelectedTabIndex);
            Assert.Single(WindowEntries(mainVm));

            // The menu entry mirrors later title changes on its window.
            help.Title = "Custom Help Title";
            Ui.Pump();
            Assert.Equal("Custom Help Title", WindowEntries(mainVm).Single().Title);

            help.Close();
            Ui.Pump();
            Assert.Empty(WindowEntries(mainVm));
            main.Close();
        });
    }

    [Fact]
    public async Task WindowMenu_RealizesEntriesThroughCompiledBindings()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var main = host.Resolve<MainWindow>();
            await ((MainWindowViewModel)main.DataContext!).InitializeAsync();
            main.Show();
            Ui.Pump();
            var ws = host.Resolve<IWindowService>();
            var mainVm = host.Resolve<MainWindowViewModel>();

            // One BookEdit + one Utility window so the menu holds both groups and the separator between them.
            var book = await SeedData.AddBookAsync(host, "Menu Subject", ct);
            await ws.OpenFullDetailsWindowAsync(book.BookId);
            ws.OpenHelpWindow(HelpTab.KeyboardShortcuts);
            Ui.Pump();

            var windowMenu = main.Descendants<MenuItem>().Single(i => i.Name == "WindowMenuParent");
            windowMenu.IsSubMenuOpen = true;
            Ui.Pump();

            // Each menu item realizes through the compiled Header/Tag bindings — the two live windows show
            // their titles, and the group separator carries its Tag.
            var realized = windowMenu.Items.OfType<OpenWindowEntry>()
                .Select(e => windowMenu.ContainerFromItem(e) as MenuItem)
                .Where(m => m is not null)
                .Select(m => m!)
                .ToList();
            foreach (var title in WindowEntries(mainVm).Select(e => e.Title))
                Assert.Contains(realized, m => Equals(m.Header, title));
            Assert.Contains(realized, m => Equals(m.Tag, "separator"));

            main.Close();
        });
    }

    [Fact]
    public async Task UnsavedChangesDialog_SkipsAHiddenSecondaryWindow_WhenPickingItsOwner()
    {
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var main = host.Resolve<MainWindow>();
            await ((MainWindowViewModel)main.DataContext!).InitializeAsync();
            main.Show();
            Ui.Pump();
            var ws = host.Resolve<IWindowService>();

            // Minimize-to-status-bar Hide()s the batch window but keeps it registered — the owner pick
            // must skip it, or ShowDialog throws on the non-visible owner.
            ws.OpenBatchQueueWindow();
            Ui.Pump();
            var batch = main.OwnedWindows.OfType<BatchQueueWindow>().Single();
            batch.Hide();
            Ui.Pump();

            var pending = ws.ShowUnsavedChangesDialogAsync("Some Book");
            Ui.Pump();

            var dialog = main.OwnedWindows.OfType<MessageDialog>().Single();
            Assert.True(dialog.IsVisible);

            dialog.Press(Avalonia.Input.PhysicalKey.Escape);
            Assert.Equal(UnsavedChangesResult.KeepEditing, await pending);
            main.Close();
        });
    }

    /// <summary>The real (non-sentinel, non-separator) Window-menu entries — the sentinel's command is the
    /// only one that reports it cannot execute, which cleanly separates it from live window entries.</summary>
    private static List<OpenWindowEntry> WindowEntries(MainWindowViewModel vm) =>
        vm.OpenWindowEntries.Where(e => !e.IsSeparator && e.ActivateCommand.CanExecute(null)).ToList();
}
