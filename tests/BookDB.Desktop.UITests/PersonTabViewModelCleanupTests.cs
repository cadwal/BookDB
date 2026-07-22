using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Data.DbContexts;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// The cleanup panel's ignore flow over a real database: ignoring a proposal drops its row and bumps the ignored
/// footer, the ignored list shows it, and un-ignoring restores it on the re-scan.
/// </summary>
public class PersonTabViewModelCleanupTests : HeadlessTest
{
    [Fact]
    public async Task IgnoreThenUnignore_HidesTheRow_BumpsTheFooter_ThenRestoresIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedDirtyPersonAsync(host, "by Alice.", ct);

            var vm = new PersonTabViewModel(
                host.Resolve<ILookupManagementService>(),
                host.Resolve<ILookupService>(),
                Substitute.For<IWindowService>(),
                Substitute.For<IMessenger>());

            await vm.OpenDataCleanupCommand.ExecuteAsync(null);
            var row = Assert.Single(vm.CleanupProposals);
            Assert.False(vm.HasIgnored);
            Assert.Equal(0, vm.IgnoredCount);

            // Ignore drops the row and bumps the footer.
            await vm.IgnoreProposalCommand.ExecuteAsync(row);
            Assert.Empty(vm.CleanupProposals);
            Assert.True(vm.HasIgnored);
            Assert.Equal(1, vm.IgnoredCount);

            // The ignored list shows the dismissed proposal against the current person name.
            await vm.ViewIgnoredCommand.ExecuteAsync(null);
            Assert.True(vm.IsIgnoredListVisible);
            var ignored = Assert.Single(vm.IgnoredProposals);
            Assert.Equal("by Alice.", ignored.PersonDisplayName);

            // Un-ignore restores the proposal on the re-scan and clears the footer.
            await vm.UnignoreCommand.ExecuteAsync(ignored);
            Assert.Empty(vm.IgnoredProposals);
            Assert.Equal(0, vm.IgnoredCount);
            Assert.False(vm.HasIgnored);
            Assert.Single(vm.CleanupProposals);
        });
    }

    [Fact]
    public async Task AllIgnored_CloseAndReopen_KeepsIgnoredListReachable()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedDirtyPersonAsync(host, "by Alice.", ct);

            var vm = new PersonTabViewModel(
                host.Resolve<ILookupManagementService>(),
                host.Resolve<ILookupService>(),
                Substitute.For<IWindowService>(),
                Substitute.For<IMessenger>());

            await vm.LoadAsync();
            await vm.OpenDataCleanupCommand.ExecuteAsync(null);
            await vm.IgnoreProposalCommand.ExecuteAsync(Assert.Single(vm.CleanupProposals));
            Assert.True(vm.HasNoCleanup);
            Assert.True(vm.HasIgnored);

            // Close the panel and reopen it: the ignored proposal is still reachable to un-ignore.
            vm.CloseDataCleanupCommand.Execute(null);
            await vm.OpenDataCleanupCommand.ExecuteAsync(null);

            Assert.True(vm.HasIgnored);
            await vm.ViewIgnoredCommand.ExecuteAsync(null);
            Assert.NotEmpty(vm.IgnoredProposals);
        });
    }

    [Fact]
    public async Task DataCleanupButton_StaysEnabled_AfterOpeningSelectingAndClosing()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedDirtyPersonAsync(host, "by Alice.", ct);

            var vm = new PersonTabViewModel(
                host.Resolve<ILookupManagementService>(),
                host.Resolve<ILookupService>(),
                Substitute.For<IWindowService>(),
                Substitute.For<IMessenger>());
            var view = new PersonTabView { DataContext = vm };
            var window = view.Host();
            try
            {
                await vm.LoadAsync();
                Ui.Pump();
                var button = view.Descendants<Button>()
                    .First(b => ReferenceEquals(b.Command, vm.OpenDataCleanupCommand));
                Assert.True(button.IsEffectivelyEnabled);

                // Open cleanup, then let another CanExecute-affecting change fire while it's open (selecting a
                // person in the always-visible list re-evaluates the command).
                await vm.OpenDataCleanupCommand.ExecuteAsync(null);
                vm.SelectedPerson = vm.FilteredPersons.First();
                Ui.Pump();

                // Closing the panel must re-enable the outer entry — otherwise the ignored list is unreachable.
                vm.CloseDataCleanupCommand.Execute(null);
                Ui.Pump();
                Assert.True(button.IsEffectivelyEnabled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task AllIgnored_FreshViewModel_KeepsIgnoredListReachable()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedDirtyPersonAsync(host, "by Alice.", ct);

            var vm1 = new PersonTabViewModel(
                host.Resolve<ILookupManagementService>(),
                host.Resolve<ILookupService>(),
                Substitute.For<IWindowService>(),
                Substitute.For<IMessenger>());
            await vm1.LoadAsync();
            await vm1.OpenDataCleanupCommand.ExecuteAsync(null);
            await vm1.IgnoreProposalCommand.ExecuteAsync(Assert.Single(vm1.CleanupProposals));

            // A fresh view model (an app restart) opens cleanup with nothing pending — the ignored proposal
            // must still be reachable to un-ignore.
            var vm2 = new PersonTabViewModel(
                host.Resolve<ILookupManagementService>(),
                host.Resolve<ILookupService>(),
                Substitute.For<IWindowService>(),
                Substitute.For<IMessenger>());
            await vm2.LoadAsync();
            Assert.False(vm2.HasCleanupWork);
            await vm2.OpenDataCleanupCommand.ExecuteAsync(null);

            Assert.True(vm2.HasIgnored);
            await vm2.ViewIgnoredCommand.ExecuteAsync(null);
            Assert.NotEmpty(vm2.IgnoredProposals);
        });
    }

    [Fact]
    public async Task CleanupPanel_RendersProposalAndIgnoredViews_WithoutBindingErrors()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedDirtyPersonAsync(host, "by Alice.", ct);

            var vm = new PersonTabViewModel(
                host.Resolve<ILookupManagementService>(),
                host.Resolve<ILookupService>(),
                Substitute.For<IWindowService>(),
                Substitute.For<IMessenger>());
            var view = new PersonTabView { DataContext = vm };
            var window = view.Host();
            try
            {
                await vm.OpenDataCleanupCommand.ExecuteAsync(null);
                Ui.Pump();
                // The proposals grid renders its per-row Ignore button (bound through #Root to the VM command).
                Assert.Contains(view.Descendants<Button>(), b => ReferenceEquals(b.Command, vm.IgnoreProposalCommand));

                await vm.IgnoreProposalCommand.ExecuteAsync(vm.CleanupProposals[0]);
                await vm.ViewIgnoredCommand.ExecuteAsync(null);
                Ui.Pump();
                // The ignored list renders its per-row Un-ignore button.
                Assert.Contains(view.Descendants<Button>(), b => ReferenceEquals(b.Command, vm.UnignoreCommand));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task LoadAsync_BadgesPendingWork_AndFoldsDuplicatesIntoCleanup()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedDirtyPersonAsync(host, "by Alice.", ct);       // a rename proposal
            await SeedDirtyPersonAsync(host, "Jane Austen", ct);     // near-duplicate pair (distance 1)
            await SeedDirtyPersonAsync(host, "Jane Austin", ct);

            var service = host.Resolve<ILookupManagementService>();
            var vm = new PersonTabViewModel(
                service,
                host.Resolve<ILookupService>(),
                Substitute.For<IWindowService>(),
                Substitute.For<IMessenger>());

            await vm.LoadAsync();

            // The entry-point badge advertises pending work without opening the panel, and counts the
            // suspected duplicate alongside the name proposals.
            Assert.True(vm.HasCleanupWork);
            Assert.NotEmpty(vm.SuspectedDuplicates);
            var (renames, splits, _) = await service.ScanPersonNameCleanupAsync(ct);
            Assert.Equal(renames.Count + splits.Count + vm.SuspectedDuplicates.Count, vm.CleanupPendingCount);

            // Opening cleanup surfaces the duplicates in the same panel — it is not treated as "nothing to do".
            await vm.OpenDataCleanupCommand.ExecuteAsync(null);
            Assert.False(vm.HasNoCleanup);
        });
    }

    [Fact]
    public async Task IgnoreDuplicate_DropsThePair_ShowsInIgnored_ThenUnignoreRestores()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedDirtyPersonAsync(host, "Jane Austen", ct);
            await SeedDirtyPersonAsync(host, "Jane Austin", ct); // distance 1 → suspected duplicate

            var vm = new PersonTabViewModel(
                host.Resolve<ILookupManagementService>(),
                host.Resolve<ILookupService>(),
                Substitute.For<IWindowService>(),
                Substitute.For<IMessenger>());

            await vm.LoadAsync();
            var pair = Assert.Single(vm.SuspectedDuplicates);

            // Ignoring the pair drops it from the suggestions and counts toward the ignored footer.
            await vm.IgnoreDuplicateCommand.ExecuteAsync(pair);
            Assert.Empty(vm.SuspectedDuplicates);
            Assert.True(vm.HasIgnored);
            Assert.Equal(1, vm.IgnoredCount);

            // It appears in the ignored list as a Duplicate; un-ignoring brings the pair back.
            await vm.ViewIgnoredCommand.ExecuteAsync(null);
            var ignored = Assert.Single(vm.IgnoredProposals);
            await vm.UnignoreCommand.ExecuteAsync(ignored);
            Assert.Empty(vm.IgnoredProposals);
            Assert.False(vm.HasIgnored);
            Assert.Single(vm.SuspectedDuplicates);
        });
    }

    [Fact]
    public async Task CleanupPanel_RendersDuplicateIgnore_AndMergeBecomesVisibleInView()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedDirtyPersonAsync(host, "Jane Austen", ct);
            await SeedDirtyPersonAsync(host, "Jane Austin", ct);

            var vm = new PersonTabViewModel(
                host.Resolve<ILookupManagementService>(),
                host.Resolve<ILookupService>(),
                Substitute.For<IWindowService>(),
                Substitute.For<IMessenger>());
            var view = new PersonTabView { DataContext = vm };
            var window = view.Host();
            try
            {
                await vm.LoadAsync();
                await vm.OpenDataCleanupCommand.ExecuteAsync(null);
                Ui.Pump();

                // The duplicates section renders a per-pair Ignore button bound to the VM command.
                Assert.Contains(view.Descendants<Button>(), b => ReferenceEquals(b.Command, vm.IgnoreDuplicateCommand));

                // Selecting a pair swaps to the merge panel — its confirm button is actually realized and visible
                // in the control tree, i.e. the pane is not blank (the reported regression).
                vm.SelectDuplicatePairCommand.Execute(vm.SuspectedDuplicates[0]);
                Ui.Pump();
                var confirm = view.Descendants<Button>().First(b => ReferenceEquals(b.Command, vm.ConfirmMergeCommand));
                Assert.True(confirm.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task SelectDuplicate_FromOpenCleanupPanel_ShowsMergePanel_NotBlank()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedDirtyPersonAsync(host, "Jane Austen", ct);
            await SeedDirtyPersonAsync(host, "Jane Austin", ct);

            var vm = new PersonTabViewModel(
                host.Resolve<ILookupManagementService>(),
                host.Resolve<ILookupService>(),
                Substitute.For<IWindowService>(),
                Substitute.For<IMessenger>());

            await vm.LoadAsync();
            await vm.OpenDataCleanupCommand.ExecuteAsync(null);
            Assert.True(vm.IsCleanupPanelVisible);

            // Clicking a duplicate from inside the open cleanup panel must switch to the merge panel — not
            // leave both panels hidden (the blank-pane regression).
            vm.SelectDuplicatePairCommand.Execute(vm.SuspectedDuplicates[0]);
            Assert.True(vm.IsMergePanelVisible);
            Assert.False(vm.IsCleanupPanelVisible);
        });
    }

    [Fact]
    public async Task CancelMerge_FromDuplicatePair_ReturnsToCleanupPanel()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedDirtyPersonAsync(host, "Jane Austen", ct);
            await SeedDirtyPersonAsync(host, "Jane Austin", ct);

            var vm = new PersonTabViewModel(
                host.Resolve<ILookupManagementService>(),
                host.Resolve<ILookupService>(),
                Substitute.For<IWindowService>(),
                Substitute.For<IMessenger>());

            await vm.LoadAsync();
            await vm.OpenDataCleanupCommand.ExecuteAsync(null);
            vm.SelectDuplicatePairCommand.Execute(vm.SuspectedDuplicates[0]);
            Assert.True(vm.IsMergePanelVisible);

            // Aborting a duplicate fix launched from cleanup returns to that panel — not the person list.
            vm.CancelMergeCommand.Execute(null);
            Assert.False(vm.IsMergePanelVisible);
            Assert.True(vm.IsCleanupProposalsVisible);
            Assert.NotEmpty(vm.SuspectedDuplicates);
        });
    }

    [Fact]
    public async Task DuplicateIgnore_PersistsForAFreshViewModel()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedDirtyPersonAsync(host, "Jane Austen", ct);
            await SeedDirtyPersonAsync(host, "Jane Austin", ct);

            var vm1 = new PersonTabViewModel(
                host.Resolve<ILookupManagementService>(),
                host.Resolve<ILookupService>(),
                Substitute.For<IWindowService>(),
                Substitute.For<IMessenger>());
            await vm1.LoadAsync();
            await vm1.IgnoreDuplicateCommand.ExecuteAsync(Assert.Single(vm1.SuspectedDuplicates));
            Assert.Empty(vm1.SuspectedDuplicates);

            // A fresh view model over the same database (an app restart) still suppresses the ignored pair.
            var vm2 = new PersonTabViewModel(
                host.Resolve<ILookupManagementService>(),
                host.Resolve<ILookupService>(),
                Substitute.For<IWindowService>(),
                Substitute.For<IMessenger>());
            await vm2.LoadAsync();
            Assert.Empty(vm2.SuspectedDuplicates);
            Assert.Equal(1, vm2.DuplicateIgnoredCount);
        });
    }

    [Fact]
    public async Task DuplicateIgnore_ResurfacesWhenAPersonInThePairIsRenamed()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedDirtyPersonAsync(host, "Jane Austen", ct);
            var austinId = await SeedPersonReturningIdAsync(host, "Jane Austin", ct);

            var vm = new PersonTabViewModel(
                host.Resolve<ILookupManagementService>(),
                host.Resolve<ILookupService>(),
                Substitute.For<IWindowService>(),
                Substitute.For<IMessenger>());
            await vm.LoadAsync();
            await vm.IgnoreDuplicateCommand.ExecuteAsync(Assert.Single(vm.SuspectedDuplicates));
            Assert.Empty(vm.SuspectedDuplicates);

            // Renaming a person in the pair changes the ignore fingerprint, so the (still-similar) pair resurfaces.
            await RenamePersonAsync(host, austinId, "Jane Auston", ct);
            await vm.LoadAsync();
            Assert.Single(vm.SuspectedDuplicates);
            Assert.Equal(0, vm.DuplicateIgnoredCount);
        });
    }

    [Fact]
    public async Task DuplicateIgnore_DoesNotSuppressADistinctPairThatSharesADisplayName()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            // Two distinct people who happen to share a display name, both near-matching "Jane Austen".
            // The ignore fingerprint keys on the other person's id, so dismissing one pair must not alias
            // onto the other (name alone can't tell them apart).
            await SeedDirtyPersonAsync(host, "Jane Austen", ct);
            await SeedDirtyPersonAsync(host, "Jane Austin", ct);
            await SeedDirtyPersonAsync(host, "Jane Austin", ct);

            var vm = new PersonTabViewModel(
                host.Resolve<ILookupManagementService>(),
                host.Resolve<ILookupService>(),
                Substitute.For<IWindowService>(),
                Substitute.For<IMessenger>());
            await vm.LoadAsync();
            Assert.Equal(2, vm.SuspectedDuplicates.Count);

            await vm.IgnoreDuplicateCommand.ExecuteAsync(vm.SuspectedDuplicates[0]);

            // The other, genuinely distinct pair stays surfaced; exactly one is suppressed.
            Assert.Single(vm.SuspectedDuplicates);
            Assert.Equal(1, vm.DuplicateIgnoredCount);
        });
    }

    private static async Task SeedDirtyPersonAsync(TestHost host, string displayName, CancellationToken ct)
    {
        var factory = host.Resolve<IDbContextFactory<BookDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        db.People.Add(new Person { DisplayName = displayName, SortName = displayName });
        await db.SaveChangesAsync(ct);
    }

    private static async Task<int> SeedPersonReturningIdAsync(TestHost host, string displayName, CancellationToken ct)
    {
        var factory = host.Resolve<IDbContextFactory<BookDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        var person = new Person { DisplayName = displayName, SortName = displayName };
        db.People.Add(person);
        await db.SaveChangesAsync(ct);
        return person.PersonId;
    }

    private static async Task RenamePersonAsync(TestHost host, int personId, string newName, CancellationToken ct)
    {
        var factory = host.Resolve<IDbContextFactory<BookDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        // The context factory defaults to NoTracking, so opt in explicitly or the update won't persist.
        var person = await db.People.AsTracking().FirstAsync(p => p.PersonId == personId, ct);
        person.DisplayName = newName;
        person.SortName = newName;
        await db.SaveChangesAsync(ct);
    }
}
