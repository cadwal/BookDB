using System.Collections.Generic;
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
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Person tab has its own view model (display name paired with sort name, and a two-step merge panel). Adds a person
/// with both names, renames, then merges a second person into it via the canonical-choice flow — asserting the names
/// round-trip and that authorship of a book follows the merge to the surviving person.
/// </summary>
public class ManagePersonsFlowTests : HeadlessTest
{
    [Fact]
    public async Task AddRenameAndMergeAPerson_RoundTripsNamesAndReassignsAuthorship()
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

            var tab = vm.PersonTab;
            var lookups = host.Resolve<ILookupService>();

            // Add with distinct display + sort names.
            tab.AddCommand.Execute(null);
            tab.EditDisplayName = "Jane Austen";
            tab.EditSortName = "Austen, Jane";
            await ((IAsyncRelayCommand)tab.SaveCommand).ExecuteAsync(null);
            var janeId = tab.SelectedPerson!.PersonId;
            var jane = (await lookups.GetAllAsync<Person>()).Single(p => p.PersonId == janeId);
            Assert.Equal("Jane Austen", jane.DisplayName);
            Assert.Equal("Austen, Jane", jane.SortName);

            // Rename the display name.
            tab.SelectedPerson = tab.Persons.First(p => p.PersonId == janeId);
            tab.EditDisplayName = "J. Austen";
            await ((IAsyncRelayCommand)tab.SaveCommand).ExecuteAsync(null);
            Assert.Equal("J. Austen",
                (await lookups.GetAllAsync<Person>()).Single(p => p.PersonId == janeId).DisplayName);

            // A book authored by Jane, so the merge can prove authorship follows.
            var bookId = await SeedBookByAuthorAsync(host, "Emma", janeId, ct);

            // Add the merge target.
            tab.AddCommand.Execute(null);
            tab.EditDisplayName = "Charlotte Bronte";
            tab.EditSortName = "Bronte, Charlotte";
            await ((IAsyncRelayCommand)tab.SaveCommand).ExecuteAsync(null);
            var charlotteId = tab.SelectedPerson!.PersonId;

            // Open the merge (picker returns Charlotte), keep Charlotte as canonical, confirm.
            windowService
                .ShowMergeTargetPickerAsync(Arg.Any<string>(), Arg.Any<int>(),
                    Arg.Any<IReadOnlyList<LookupEntryRow>>(), Arg.Any<Window?>())
                .Returns(charlotteId);
            tab.SelectedPerson = tab.Persons.First(p => p.PersonId == janeId);
            await ((IAsyncRelayCommand)tab.MergePersonCommand).ExecuteAsync(null);
            tab.SetAsCanonicalCommand.Execute("target");
            await ((IAsyncRelayCommand)tab.ConfirmMergeCommand).ExecuteAsync(null);

            Assert.DoesNotContain(tab.Persons, p => p.PersonId == janeId);
            Assert.Contains(tab.Persons, p => p.PersonId == charlotteId);

            var reread = await host.Resolve<IBookService>().GetBookByIdAsync(bookId, ct);
            Assert.Contains(reread!.Contributors, c => c.PersonId == charlotteId);
            Assert.DoesNotContain(reread.Contributors, c => c.PersonId == janeId);
            window.Close();
        });
    }

    [Fact]
    public async Task SuspectedDuplicates_AreSurfacedPickedFromTheListAndMerged()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            // Two near-duplicate names (Levenshtein 1 once normalized) — the scan should pair them.
            await SeedPersonAsync(host, "Stephen King", "King, Stephen", ct);
            await SeedPersonAsync(host, "Steven King", "King, Steven", ct);

            var vm = host.Resolve<ManageLookupsViewModel>();
            await vm.InitializeAsync(null); // PersonTab.LoadAsync runs the duplicate scan
            var window = new ManageLookupsWindow { DataContext = vm };
            window.Show();
            Ui.Pump();

            var tab = vm.PersonTab;
            Assert.True(tab.HasSuspectedDuplicates);
            var pair = Assert.Single(tab.SuspectedDuplicates);
            var mergedId = pair.Left.PersonId;
            var survivorId = pair.Right.PersonId;

            // Picking the pair opens the merge panel pre-filled with the two people.
            tab.SelectDuplicatePairCommand.Execute(pair);
            Assert.True(tab.IsMergePanelOpen);
            Assert.Equal(mergedId, tab.MergeSource!.PersonId);
            Assert.Equal(survivorId, tab.MergeTarget!.PersonId);

            // Keep the target as canonical and confirm.
            tab.SetAsCanonicalCommand.Execute("target");
            await ((IAsyncRelayCommand)tab.ConfirmMergeCommand).ExecuteAsync(null);

            Assert.False(tab.IsMergePanelOpen);
            Assert.DoesNotContain(tab.Persons, p => p.PersonId == mergedId);
            Assert.Contains(tab.Persons, p => p.PersonId == survivorId);
            Assert.False(tab.HasSuspectedDuplicates); // the post-merge re-scan finds no more pairs
            window.Close();
        });
    }

    private static async Task SeedPersonAsync(TestHost host, string displayName, string sortName, CancellationToken ct)
    {
        var factory = host.Resolve<IDbContextFactory<BookDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        db.People.Add(new Person { DisplayName = displayName, SortName = sortName });
        await db.SaveChangesAsync(ct);
    }

    private static async Task<int> SeedBookByAuthorAsync(TestHost host, string title, int personId, CancellationToken ct)
    {
        var factory = host.Resolve<IDbContextFactory<BookDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        var authorRole = await db.ContributorRoles.FirstAsync(r => r.Code == "Author", ct);
        var book = new Book { Title = title };
        db.Books.Add(book);
        await db.SaveChangesAsync(ct);
        db.BookContributors.Add(new BookContributor
        {
            BookId = book.BookId,
            PersonId = personId,
            ContributorRoleId = authorRole.ContributorRoleId,
            SortOrder = 0,
        });
        await db.SaveChangesAsync(ct);
        return book.BookId;
    }
}
