using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Data.DbContexts;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using BookDB.Models.Metadata;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// The enriched merge-review dialog: the picked Authors column seeds editable type-ahead rows
/// whose edits (not the raw pick) become the saved contributors, reusing an existing person when
/// the spelling matches; new books land in the collection chosen in the picker; and
/// "Save &amp; open editor" hands the saved book to the full editor.
/// </summary>
public class MergeReviewAuthorsFlowTests : HeadlessTest
{
    private static BookMetadata MakeSource(string sourceName, string title, IReadOnlyList<string> authors) =>
        new(Title: title, Subtitle: null, Authors: authors, Publisher: null, PubDate: null,
            Language: null, Isbn: "9780451526538", Pages: null, Description: null,
            CoverImageUrl: null, Series: null, SeriesNumber: null, SourceName: sourceName);

    [Fact]
    public async Task ReviewSave_ReusesTheCorrectedAuthor_AndFilesTheBookInThePickedCollection()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            await SeedData.AddBookAsync(host, "Seed Book", ["George Orwell"], ct);
            await SeedData.AddCollectionAsync(host, "Shelf A", ct);
            var shelfB = await SeedData.AddCollectionAsync(host, "Shelf B", ct);

            var sources = new List<BookMetadata>
            {
                MakeSource("GoogleBooks", "1984", ["George Orwel"]),
                MakeSource("OpenLibrary", "1984 ", ["G. Orwell"])
            };

            bool? closedWith = null;
            var vm = new MergeReviewViewModel(
                sources: sources,
                currentBook: null,
                coverOptions: [],
                bookMetadataService: host.Resolve<IBookMetadataService>(),
                messenger: host.Resolve<IMessenger>(),
                existingBookId: null,
                collectionId: null,
                closeDialog: r => closedWith = r,
                bookService: host.Resolve<IBookService>(),
                lookupService: host.Resolve<ILookupService>());
            await vm.InitializeAsync();

            // With no caller-supplied collection the picker defaults to the first real collection; there is no
            // "no collection" entry, so every saved book lands in a collection.
            Assert.DoesNotContain(vm.Collections, c => c.CollectionId <= 0);
            Assert.Same(vm.Collections[0], vm.SelectedCollection);

            var window = new MergeReviewDialog { DataContext = vm };
            window.Show();
            Ui.Pump();

            // One rendered type-ahead box per seeded author row.
            var boxes = window.Descendants<AutoCompleteBox>().ToList();
            Assert.Equal(vm.AuthorRows.Count, boxes.Count);

            // Correcting the misspelled pick resolves it to the seeded person (reuse, not create).
            vm.AuthorRows[0].SearchText = "George Orwell";
            Ui.Pump();
            Assert.True(vm.AuthorRows[0].IsExistingPerson);

            vm.SelectedCollection = vm.Collections.Single(c => c.CollectionId == shelfB.CollectionId);
            await vm.SaveCommand.ExecuteAsync(null);
            window.Close();

            Assert.True(closedWith);

            var factory = host.Resolve<IDbContextFactory<BookDbContext>>();
            await using var db = await factory.CreateDbContextAsync(ct);
            var saved = await db.Books
                .Include(b => b.Contributors).ThenInclude(bc => bc.Person)
                .SingleAsync(b => b.Title == "1984", ct);
            Assert.Equal(shelfB.CollectionId, saved.CollectionId);
            var seededPerson = await db.People.SingleAsync(p => p.DisplayName == "George Orwell", ct);
            var contributor = Assert.Single(saved.Contributors);
            Assert.Equal(seededPerson.PersonId, contributor.PersonId);
        });
    }

    [Fact]
    public async Task SaveAndOpenEditor_OpensTheFullEditorOnTheSavedBook()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var windowService = Substitute.For<IWindowService>();

            var sources = new List<BookMetadata>
            {
                MakeSource("GoogleBooks", "The Hobbit", ["J. R. R. Tolkien"]),
                MakeSource("OpenLibrary", "Hobbiten", ["J. R. R. Tolkien"])
            };

            bool? closedWith = null;
            var vm = new MergeReviewViewModel(
                sources: sources,
                currentBook: null,
                coverOptions: [],
                bookMetadataService: host.Resolve<IBookMetadataService>(),
                messenger: host.Resolve<IMessenger>(),
                existingBookId: null,
                collectionId: null,
                closeDialog: r => closedWith = r,
                windowService: windowService,
                bookService: host.Resolve<IBookService>(),
                lookupService: host.Resolve<ILookupService>());
            await vm.InitializeAsync();

            await vm.SaveAndOpenEditorCommand.ExecuteAsync(null);

            Assert.True(closedWith);

            var factory = host.Resolve<IDbContextFactory<BookDbContext>>();
            await using var db = await factory.CreateDbContextAsync(ct);
            var saved = await db.Books.SingleAsync(ct);
            await windowService.Received(1).OpenFullDetailsWindowAsync(saved.BookId);
        });
    }
}
