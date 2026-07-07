using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Svg.Skia;
using BookDB.Desktop.Localization;
using BookDB.Desktop.ViewModels;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Pins the menu iconography. Icons are keyed SvgImage resources (Styles/Icons.axaml), so identity is asserted
/// by resource reference: every action item carries its agreed icon, checkable items stay icon-free (the icon
/// slot doubles as the check indicator), the same action resolves to the same resource across menu, toolbar and
/// context menu, and every icon SVG stays single-colour so the IconCss theme tinting applies.
/// </summary>
public class MenuIconTests : HeadlessTest
{
    private static readonly IReadOnlyDictionary<string, string> MainMenuIcons = new Dictionary<string, string>
    {
        [Resources.Menu_NewBook] = "Icon.Add",
        [Resources.Menu_Backup] = "Icon.Archive",
        [Resources.Menu_RestoreBackup] = "Icon.History",
        [Resources.Menu_ImportReaderware] = "Icon.ArrowImport",
        [Resources.Menu_ImportReaderwareDatabase] = "Icon.ArrowImport",
        [Resources.Menu_ExportCsv] = "Icon.DocumentArrowUp",
        [Resources.Menu_PrintList] = "Icon.Print",
        [Resources.Menu_Exit] = "Icon.ArrowExit",
        [Resources.Menu_EditBook] = "Icon.Edit",
        [Resources.Menu_DeleteBooks] = "Icon.Delete",
        [Resources.Menu_DuplicateBook] = "Icon.DocumentCopy",
        [Resources.Menu_BulkEdit] = "Icon.TableEdit",
        [Resources.Menu_AdvancedSearch] = "Icon.Filter",
        [Resources.Menu_FullDetails] = "Icon.Open",
        [Resources.Menu_CatalogByIsbn] = "Icon.BarcodeScanner",
        [Resources.Menu_RecatalogSelected] = "Icon.BookArrowClockwise",
        [Resources.Menu_RecatalogAll] = "Icon.BookArrowClockwise",
        [Resources.Menu_ManageLookups] = "Icon.TextBulletListSquare",
        [Resources.Menu_ManageBorrowers] = "Icon.PeopleList",
        [Resources.Menu_Statistics] = "Icon.ChartMultiple",
        [Resources.Menu_Maintenance] = "Icon.WrenchScrewdriver",
        [Resources.Menu_Settings] = "Icon.Settings",
        [Resources.Menu_Help_KeyboardShortcuts] = "Icon.Keyboard",
        [Resources.Menu_Help_FieldGlossary] = "Icon.BookQuestionMark",
        [Resources.Menu_Help_ImportGuide] = "Icon.DocumentArrowDown",
        [Resources.Menu_Help_DataSources] = "Icon.Globe",
        [Resources.Menu_Help_RemoteDatabases] = "Icon.DatabaseLink",
        [Resources.Menu_AboutBookDB] = "Icon.Info",
    };

    private static readonly IReadOnlyDictionary<string, string> ContextMenuIcons = new Dictionary<string, string>
    {
        [Resources.ContextMenu_Edit] = "Icon.Edit",
        [Resources.ContextMenu_OpenInWindow] = "Icon.Open",
        [Resources.ContextMenu_Duplicate] = "Icon.DocumentCopy",
        [Resources.ContextMenu_Delete] = "Icon.Delete",
        [Resources.ContextMenu_BulkEdit] = "Icon.TableEdit",
        [Resources.BookList_ContextMenu_Recatalog] = "Icon.BookArrowClockwise",
        [Resources.ContextMenu_MoveToCollection] = "Icon.FolderArrowRight",
        [Resources.ContextMenu_CopyIsbn] = "Icon.Clipboard",
        [Resources.ContextMenu_CopyTitle] = "Icon.Clipboard",
        [Resources.ContextMenu_CheckOut] = "Icon.PersonArrowRight",
        [Resources.ContextMenu_CheckIn] = "Icon.PersonArrowLeft",
    };

    [Fact]
    public Task MainMenu_ActionItems_ShowTheAgreedIcons() => RunUi(async () =>
    {
        var (window, _) = await ShowMainWindow();
        var items = MenuLeafItems(window.Find<Menu>()).ToList();

        foreach (var (header, resourceKey) in MainMenuIcons)
        {
            var matches = items.Where(i => Equals(i.Header, header)).ToList();
            Assert.NotEmpty(matches);
            Assert.All(matches, i => AssertUsesIconResource(window, i, resourceKey));
        }

        window.Close();
    });

    [Fact]
    public Task MainMenu_EveryActionItem_HasAnIcon() => RunUi(async () =>
    {
        var (window, _) = await ShowMainWindow();

        var bare = MenuLeafItems(window.Find<Menu>())
            .Where(i => i.ToggleType == MenuItemToggleType.None && i.Icon is null)
            .Select(i => i.Header?.ToString())
            .ToList();

        Assert.True(bare.Count == 0, "Menu items without an icon: " + string.Join(", ", bare));
        window.Close();
    });

    [Fact]
    public Task CheckableViewToggles_StayIconFree() => RunUi(async () =>
    {
        var (window, _) = await ShowMainWindow();

        var toggles = MenuLeafItems(window.Find<Menu>())
            .Where(i => i.ToggleType == MenuItemToggleType.CheckBox)
            .ToList();

        Assert.NotEmpty(toggles);
        Assert.All(toggles, t => Assert.Null(t.Icon));
        window.Close();
    });

    [Fact]
    public Task ContextMenu_ActionItems_ShowTheAgreedIcons() => RunUi(async () =>
    {
        var (window, _) = await ShowMainWindow();
        var contextMenu = window.Find<DataGrid>().ContextMenu!;
        var items = contextMenu.Items.OfType<MenuItem>().ToList();

        foreach (var (header, resourceKey) in ContextMenuIcons)
        {
            var item = Assert.Single(items, i => Equals(i.Header, header));
            AssertUsesIconResource(window, item, resourceKey);
        }

        window.Close();
    });

    [Fact]
    public Task ToolbarButtons_MatchTheirMenuItems() => RunUi(async () =>
    {
        var (window, vm) = await ShowMainWindow();

        var advancedSearch = window.ButtonFor(vm.BookList.AdvancedSearchCommand).Find<Image>();
        Assert.Same(window.FindResource(MainMenuIcons[Resources.Menu_AdvancedSearch]), advancedSearch.Source);

        var catalogByIsbn = window.ButtonFor(vm.CatalogByIsbnCommand).Find<Image>();
        Assert.Same(window.FindResource(MainMenuIcons[Resources.Menu_CatalogByIsbn]), catalogByIsbn.Source);

        window.Close();
    });

    [Fact]
    public Task IconResources_ResolveTheirThemeCss() => RunUi(async () =>
    {
        var (window, _) = await ShowMainWindow();

        // If the DynamicResource IconCss* lookup failed inside the merged dictionary, Css stays null and the
        // glyphs would render untinted — this is the headless stand-in for the visual theme check.
        foreach (var key in MainMenuIcons.Values.Concat(ContextMenuIcons.Values).Distinct())
        {
            var svg = Assert.IsType<SvgImage>(window.FindResource(key));
            Assert.False(string.IsNullOrEmpty(svg.Css), $"{key} did not resolve its IconCss.");
        }

        window.Close();
    });

    [Fact]
    public Task IconAssets_UseOnlyCurrentColorFills() => RunUi(() =>
    {
        var assets = AssetLoader.GetAssets(new Uri("avares://BookDB.Desktop/Assets/Icons/"), null)
            .Where(uri => uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(assets);

        foreach (var uri in assets)
        {
            // book.svg is the in-app rendering of the application icon, not a themed menu glyph.
            if (uri.AbsolutePath.EndsWith("/book.svg", StringComparison.OrdinalIgnoreCase))
                continue;

            using var reader = new StreamReader(AssetLoader.Open(uri));
            var fills = Regex.Matches(reader.ReadToEnd(), "fill=\"([^\"]+)\"").Select(m => m.Groups[1].Value);
            Assert.All(fills, fill => Assert.Equal("currentColor", fill));
        }

        return Task.CompletedTask;
    });

    private static async Task<(Window Window, MainWindowViewModel Vm)> ShowMainWindow()
    {
        var host = TestHost.Create();
        var window = (Window)await SurfaceRegistry.ByName("Main").BuildAsync(host);
        window.Show();
        Ui.Pump();
        return (window, (MainWindowViewModel)window.DataContext!);
    }

    /// <summary>All leaf menu items reachable statically — skips the dynamic Window menu (data-driven items).</summary>
    private static IEnumerable<MenuItem> MenuLeafItems(Menu menu)
    {
        static IEnumerable<MenuItem> Walk(MenuItem item)
        {
            var children = item.Items.OfType<MenuItem>().ToList();
            if (children.Count == 0)
            {
                yield return item;
                yield break;
            }
            foreach (var child in children)
                foreach (var leaf in Walk(child))
                    yield return leaf;
        }

        return menu.Items.OfType<MenuItem>()
            .Where(top => top.Name != "WindowMenuParent")
            .SelectMany(Walk);
    }

    private static void AssertUsesIconResource(Window window, MenuItem item, string resourceKey)
    {
        var image = Assert.IsType<Image>(item.Icon);
        Assert.Same(window.FindResource(resourceKey), image.Source);
    }
}
