using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Models.Entities;
using BookDB.Models.Enums;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Advanced search journeys. The dialog builds condition rows against real data: Test previews the match count
/// inline (and the count is discarded the moment the query changes, so it can never go stale), the combinator
/// switches between every-condition and any-condition semantics, and Search applies the result to the book list.
/// A search saved under a name appears in the filter panel, applies on selection, round-trips through edit,
/// and disappears on delete — with the panel's empty-state hint showing exactly when the list is empty.
/// </summary>
public class AdvancedSearchFlowTests : HeadlessTest
{
    [Fact]
    public async Task ConditionsCombinatorAndTest_ThenSearchNarrowsTheList()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedTitles(host, ct);
            var list = host.Resolve<BookListViewModel>();
            await list.LoadBooksAsync(ct);
            Assert.Equal(3, list.Books.Count);

            var (vm, dialog, result) = await OpenAsync(host);

            // The dialog opens with a single condition row, and the last row can never be removed.
            var row = Assert.Single(vm.Conditions);
            var removeButton = dialog.Descendants<Button>().Single(b =>
                ReferenceEquals(b.Command, vm.RemoveConditionCommand) && ReferenceEquals(b.CommandParameter, row));
            removeButton.Command!.Execute(removeButton.CommandParameter);
            Ui.Pump();
            Assert.Single(vm.Conditions);

            // Title contains "Dune" (defaults) — Test shows the inline match count without closing.
            dialog.TypeInto(ValueBox(dialog, 0), "Dune");
            Assert.Equal("Dune", vm.Conditions[0].Value);
            var testButton = dialog.ButtonFor(vm.TestSearchCommand);
            await ((IAsyncRelayCommand)testButton.Command!).ExecuteAsync(null);
            var twoMatches = string.Format(Resources.AdvancedSearch_MatchCountFormat, 2);
            Assert.Contains(dialog.Descendants<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == twoMatches);
            Assert.Null(result());

            // Changing the query discards the stale count: add an author condition the Dune books don't meet.
            await Ui.ClickAsync(dialog.ButtonFor(vm.AddConditionCommand));
            Assert.Equal(string.Empty, vm.TestResultText);
            Assert.Equal(2, vm.Conditions.Count);
            FieldCombo(dialog, 1).SelectedItem =
                SearchConditionViewModel.AvailableFields.Single(f => f.Field == SearchField.Author);
            Ui.Pump();
            dialog.TypeInto(ValueBox(dialog, 1), "Austen");

            // Every condition must hold under AND — no book is a Dune title by Austen.
            await ((IAsyncRelayCommand)testButton.Command!).ExecuteAsync(null);
            Assert.Equal(string.Format(Resources.AdvancedSearch_MatchCountFormat, 0), vm.TestResultText);

            // Any condition may hold under OR — both Dune books plus Austen's.
            CombinatorCombo(dialog).SelectedItem = vm.Combinators.Single(c => c.Key == "OR");
            Ui.Pump();
            Assert.Equal(string.Empty, vm.TestResultText); // combinator change also clears the count
            await ((IAsyncRelayCommand)testButton.Command!).ExecuteAsync(null);
            Assert.Equal(string.Format(Resources.AdvancedSearch_MatchCountFormat, 3), vm.TestResultText);

            // Drop the author row again and apply: the list narrows to the two Dune books.
            var secondRow = vm.Conditions[1];
            dialog.Descendants<Button>().Single(b =>
                    ReferenceEquals(b.Command, vm.RemoveConditionCommand) && ReferenceEquals(b.CommandParameter, secondRow))
                .Command!.Execute(secondRow);
            Ui.Pump();
            await Ui.ClickAsync(dialog.ButtonFor(vm.SearchCommand));
            Assert.True(result());
            await Ui.PumpUntil(() => list.IsAdvancedSearchActive, ct);
            await list.LoadBooksAsync(ct);
            Assert.Equal(new[] { "Dune", "Dune Messiah" }, list.Books.Select(b => b.Title).OrderBy(t => t));

            // Clear Search restores the full list and drops the advanced-search state.
            await list.ClearSearchCommand.ExecuteAsync(null);
            Assert.False(list.IsAdvancedSearchActive);
            Assert.Equal(3, list.Books.Count);
            dialog.Close();
        });
    }

    [Fact]
    public async Task CancellingTheDialog_ClosesWithFalse()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedTitles(host, ct);
            var (vm, dialog, result) = await OpenAsync(host);

            dialog.TypeInto(ValueBox(dialog, 0), "Dune");
            await Ui.ClickAsync(dialog.ButtonFor(vm.CancelCommand));
            Assert.False(result());
            dialog.Close();
        });
    }

    [Fact]
    public async Task ASavedSearch_ShowsInThePanel_AppliesEditsAndDeletes()
    {
        var ct = TestContext.Current.CancellationToken;
        var windowService = Substitute.For<IWindowService>();

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(windowService));
            await SeedTitles(host, ct);
            var list = host.Resolve<BookListViewModel>();
            await list.LoadBooksAsync(ct);

            var panel = host.Resolve<FilterPanelViewModel>();
            await panel.LoadSavedSearchesAsync();
            var panelView = new FilterPanelView { DataContext = panel };
            var panelWindow = panelView.Host();

            // With no saved searches the panel says so.
            Assert.Contains(panelView.Descendants<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == Resources.FilterPanel_SavedSearches_Empty);

            // Save "Dune shelf" from the dialog: Save is gated on a name.
            var (vm, dialog, result) = await OpenAsync(host);
            dialog.TypeInto(ValueBox(dialog, 0), "Dune");
            var saveButton = dialog.ButtonFor(vm.SaveSearchCommand);
            Assert.False(saveButton.IsEffectivelyEnabled);
            dialog.TypeInto(NameBox(dialog), "Dune shelf");
            Assert.True(saveButton.IsEffectivelyEnabled);
            await Ui.ClickAsync(saveButton);
            Assert.True(result());
            dialog.Close();

            // The panel hears the change, lists the search, and hides the empty-state hint.
            await Ui.PumpUntil(() => panel.SavedSearches.Count == 1, ct);
            Ui.Pump();
            Assert.DoesNotContain(panelWindow.Descendants<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == Resources.FilterPanel_SavedSearches_Empty);
            Assert.Contains(panelWindow.Descendants<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == "Dune shelf");

            // Selecting it applies the saved query to the list.
            var saved = panel.SavedSearches.Single();
            panelWindow.Find<ListBox>().SelectedItem = saved;
            await Ui.PumpUntil(() => list.IsAdvancedSearchActive, ct);
            await list.LoadBooksAsync(ct);
            Assert.Equal(2, list.Books.Count);

            // The row's edit button hands the search to the window service (which reopens the dialog).
            await Ui.ClickAsync(panelWindow.Descendants<Button>().First(b =>
                ReferenceEquals(b.Command, panel.EditSavedSearchCommand)));
            await windowService.Received(1).ShowAdvancedSearchDialogAsync(saved);

            // Editing pre-fills a fresh dialog VM from the record; re-saving updates in place.
            var (editVm, editDialog, editResult) = await OpenAsync(host);
            editVm.LoadFromSavedSearch(saved);
            Assert.Equal("Dune shelf", editVm.SavedSearchName);
            var condition = Assert.Single(editVm.Conditions);
            Assert.Equal(SearchField.Title, condition.SelectedField.Field);
            Assert.Equal("Dune", condition.Value);
            condition.Value = "Emma";
            Ui.RetypeInto(editDialog, NameBox(editDialog), "Dune shelf II");
            await Ui.ClickAsync(editDialog.ButtonFor(editVm.SaveSearchCommand));
            Assert.True(editResult());
            editDialog.Close();
            await Ui.PumpUntil(() => panel.SavedSearches.Count == 1 && panel.SavedSearches[0].Name == "Dune shelf II", ct);

            // Applying the edited search now matches Emma alone (settle on the applied rows —
            // IsAdvancedSearchActive is already true from the previous apply).
            panel.ActiveSavedSearch = null;
            Ui.Pump();
            panelWindow.Find<ListBox>().SelectedItem = panel.SavedSearches.Single();
            await Ui.PumpUntil(() => list.Books.Count == 1 && list.Books[0].Title == "Emma", ct);

            // The row's delete button removes it and the empty-state hint returns.
            await Ui.ClickAsync(panelWindow.Descendants<Button>().First(b =>
                ReferenceEquals(b.Command, panel.DeleteSavedSearchCommand)));
            Assert.Empty(panel.SavedSearches);
            Assert.Null(panel.ActiveSavedSearch);
            Ui.Pump();
            Assert.Contains(panelWindow.Descendants<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == Resources.FilterPanel_SavedSearches_Empty);
            panelWindow.Close();
        });
    }

    // ─── Plumbing ────────────────────────────────────────────────────────────

    private static async Task SeedTitles(TestHost host, System.Threading.CancellationToken ct)
    {
        await SeedData.AddBookAsync(host, "Dune", new[] { "Frank Herbert" }, ct);
        await SeedData.AddBookAsync(host, "Dune Messiah", new[] { "Frank Herbert" }, ct);
        await SeedData.AddBookAsync(host, "Emma", new[] { "Jane Austen" }, ct);
    }

    /// <summary>Opens the dialog the way <c>WindowService.ShowAdvancedSearchDialogAsync</c> composes it.</summary>
    private static async Task<(AdvancedSearchViewModel Vm, AdvancedSearchDialog Dialog, Func<bool?> Result)>
        OpenAsync(TestHost host)
    {
        var vm = host.Resolve<AdvancedSearchViewModel>();
        await vm.InitializeAsync();
        bool? result = null;
        vm.SetCloseAction(r => result = r);
        var dialog = new AdvancedSearchDialog { DataContext = vm };
        dialog.Show();
        Ui.Pump();
        return (vm, dialog, () => result);
    }

    // ComboBoxes in tree order: the combinator first, then field/operator per condition row.
    private static ComboBox CombinatorCombo(AdvancedSearchDialog dialog) => dialog.Descendants<ComboBox>()[0];
    private static ComboBox FieldCombo(AdvancedSearchDialog dialog, int row) => dialog.Descendants<ComboBox>()[1 + row * 2];

    // Visible TextBoxes in tree order: one value box per condition row, then the saved-search name box last.
    // (The ComboBox template carries a hidden inner TextBox, so unfiltered indexing lands on those.)
    private static TextBox ValueBox(AdvancedSearchDialog dialog, int row) =>
        dialog.Descendants<TextBox>().Where(t => t.IsEffectivelyVisible).ElementAt(row);

    private static TextBox NameBox(AdvancedSearchDialog dialog) =>
        dialog.Descendants<TextBox>().Last(t => t.IsEffectivelyVisible);
}
