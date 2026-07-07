using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Manage-lookups journeys. Every lookup tab wires Add/Rename/Merge/Delete to its own table, so the first test walks
/// all eight generic tabs; the second proves a merge actually reassigns a book from the merged-away publisher to the
/// target. (The Person tab, which has its own view model, is covered separately.)
/// </summary>
public class ManageLookupsFlowTests : HeadlessTest
{
    [Fact]
    public async Task EveryLookupTab_AddsRenamesMergesAndDeletes()
    {
        var ct = TestContext.Current.CancellationToken;

        var windowService = Substitute.For<IWindowService>();
        windowService.ShowDeleteConfirmationAsync(Arg.Any<string>()).Returns(true);

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(windowService));
            var vm = host.Resolve<ManageLookupsViewModel>();
            await vm.InitializeAsync(null);
            var window = new ManageLookupsWindow { DataContext = vm };
            window.Show();
            Ui.Pump();

            var tabs = new (string Name, LookupTabViewModel Tab)[]
            {
                ("Publisher", vm.PublisherTab),
                ("Series", vm.SeriesTab),
                ("Location", vm.LocationTab),
                ("Owner", vm.OwnerTab),
                ("Language", vm.LanguageTab),
                ("Category", vm.CategoryTab),
                ("PurchasePlace", vm.PurchasePlaceTab),
                ("Collection", vm.CollectionTab),
            };

            foreach (var (name, tab) in tabs)
            {
                // Add — the reload after save reads the tab's own table, so a mis-wired tab wouldn't see its entry.
                tab.AddCommand.Execute(null);
                tab.EditName = $"{name} Alpha";
                await ((IAsyncRelayCommand)tab.SaveCommand).ExecuteAsync(null);
                var alphaId = tab.SelectedEntry!.Id;
                Assert.True(alphaId > 0, $"{name}: add did not persist");
                Assert.Contains(tab.Entries, e => e.Id == alphaId && e.Name == $"{name} Alpha");

                // Rename
                tab.SelectedEntry = tab.Entries.First(e => e.Id == alphaId);
                tab.EditName = $"{name} Renamed";
                await ((IAsyncRelayCommand)tab.SaveCommand).ExecuteAsync(null);
                Assert.Contains(tab.Entries, e => e.Id == alphaId && e.Name == $"{name} Renamed");

                // Merge Alpha into a fresh Beta.
                Assert.True(tab.SupportsMerge, $"{name}: expected merge support");
                tab.AddCommand.Execute(null);
                tab.EditName = $"{name} Beta";
                await ((IAsyncRelayCommand)tab.SaveCommand).ExecuteAsync(null);
                var betaId = tab.SelectedEntry!.Id;
                windowService
                    .ShowMergeTargetPickerAsync(Arg.Any<string>(), Arg.Any<int>(),
                        Arg.Any<IReadOnlyList<LookupEntryRow>>(), Arg.Any<Window?>())
                    .Returns(betaId);
                tab.SelectedEntry = tab.Entries.First(e => e.Id == alphaId);
                await ((IAsyncRelayCommand)tab.MergeIntoCommand!).ExecuteAsync(null);
                Assert.DoesNotContain(tab.Entries, e => e.Id == alphaId);
                Assert.Contains(tab.Entries, e => e.Id == betaId);

                // Delete the survivor.
                tab.SelectedEntry = tab.Entries.First(e => e.Id == betaId);
                await ((IAsyncRelayCommand)tab.DeleteCommand).ExecuteAsync(null);
                Assert.DoesNotContain(tab.Entries, e => e.Id == betaId);
            }

            window.Close();
        });
    }

    [Fact]
    public async Task MergingAPublisher_ReassignsItsBooksToTheTarget()
    {
        var ct = TestContext.Current.CancellationToken;

        var windowService = Substitute.For<IWindowService>();

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(windowService));
            var vm = host.Resolve<ManageLookupsViewModel>();
            await vm.InitializeAsync(null);
            var window = new ManageLookupsWindow { DataContext = vm };
            window.Show();
            Ui.Pump();

            var tab = vm.PublisherTab;

            tab.AddCommand.Execute(null);
            tab.EditName = "Penguin";
            await ((IAsyncRelayCommand)tab.SaveCommand).ExecuteAsync(null);
            var penguinId = tab.SelectedEntry!.Id;

            tab.AddCommand.Execute(null);
            tab.EditName = "Vintage";
            await ((IAsyncRelayCommand)tab.SaveCommand).ExecuteAsync(null);
            var vintageId = tab.SelectedEntry!.Id;

            var book = await host.Resolve<IBookService>()
                .AddBookAsync(new Book { Title = "Merge Subject", PublisherId = penguinId }, ct);

            windowService
                .ShowMergeTargetPickerAsync(Arg.Any<string>(), Arg.Any<int>(),
                    Arg.Any<IReadOnlyList<LookupEntryRow>>(), Arg.Any<Window?>())
                .Returns(vintageId);

            tab.SelectedEntry = tab.Entries.First(e => e.Id == penguinId);
            await ((IAsyncRelayCommand)tab.MergeIntoCommand!).ExecuteAsync(null);

            Assert.DoesNotContain(tab.Entries, e => e.Id == penguinId);
            var reread = await host.Resolve<IBookService>().GetBookByIdAsync(book.BookId, ct);
            Assert.Equal(vintageId, reread!.PublisherId);
            window.Close();
        });
    }

    [Fact]
    public async Task APublisherInUse_ShowsItsUsage_AndCannotBeDeleted()
    {
        var ct = TestContext.Current.CancellationToken;

        var windowService = Substitute.For<IWindowService>();
        windowService.ShowDeleteConfirmationAsync(Arg.Any<string>()).Returns(true);

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(windowService));
            var vm = host.Resolve<ManageLookupsViewModel>();
            await vm.InitializeAsync(null);
            var window = new ManageLookupsWindow { DataContext = vm };
            window.Show();
            Ui.Pump();

            var tabs = window.Find<TabControl>();
            tabs.SelectedIndex = 1; // Publisher (after Person)
            Ui.Pump();
            var tab = vm.PublisherTab;

            tab.AddCommand.Execute(null);
            tab.EditName = "Busy House";
            await ((IAsyncRelayCommand)tab.SaveCommand).ExecuteAsync(null);
            var busyId = tab.SelectedEntry!.Id;

            tab.AddCommand.Execute(null);
            tab.EditName = "Idle House";
            await ((IAsyncRelayCommand)tab.SaveCommand).ExecuteAsync(null);
            var idleId = tab.SelectedEntry!.Id;

            await host.Resolve<IBookService>()
                .AddBookAsync(new Book { Title = "Occupier", PublisherId = busyId }, ct);

            // The in-use entry reports its usage and Delete is gated off.
            tab.SelectedEntry = tab.Entries.First(e => e.Id == busyId);
            await Ui.PumpUntil(() => tab.UsedByCount == 1, ct); // usage count loads after selection
            var deleteButton = window.ButtonFor(tab.DeleteCommand);
            Assert.Contains(window.Descendants<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == string.Format(Resources.ManageLookups_UsedByBooks, 1));
            Assert.False(deleteButton.IsEffectivelyEnabled);

            // The unused entry shows zero usage and can be deleted.
            tab.SelectedEntry = tab.Entries.First(e => e.Id == idleId);
            await Ui.PumpUntil(() => tab.UsedByCount == 0, ct);
            Assert.Contains(window.Descendants<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == string.Format(Resources.ManageLookups_UsedByBooks, 0));
            Assert.True(deleteButton.IsEffectivelyEnabled);
            await ((IAsyncRelayCommand)tab.DeleteCommand).ExecuteAsync(null);
            Assert.DoesNotContain(tab.Entries, e => e.Id == idleId);
            Assert.Contains(tab.Entries, e => e.Id == busyId); // the in-use one is still there
            window.Close();
        });
    }
}
