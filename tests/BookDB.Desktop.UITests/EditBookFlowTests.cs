using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Edit journey through the full details window's shared edit form. The save path exercises the whole facade — every
/// scalar field, every lookup selector, contributors and categories — and asserts each change round-trips to the
/// database and the list. A second test drives the discard path and asserts every field reverts and nothing persists.
/// </summary>
public class EditBookFlowTests : HeadlessTest
{
    [Fact]
    public async Task EditingEveryFieldAndSaving_PersistsEveryChange()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var bookId = await EditSample.SeedAsync(host, ct);

            var vm = host.Resolve<FullDetailsWindowViewModel>();
            Assert.True(await vm.LoadBookAsync(bookId));
            var window = new FullDetailsWindow { DataContext = vm };
            window.Show();
            Ui.Pump();

            // Real input on the Basic tab proves the edit form's two-way binding still writes back to the VM.
            Ui.RetypeInto(window, FindBox(window, EditSample.OriginalTitle), "Edited Title");
            Assert.Equal("Edited Title", vm.EditTitle);
            Ui.RetypeInto(window, FindBox(window, EditSample.OriginalIsbn), "2222222222");
            Assert.Equal("2222222222", vm.EditIsbn);

            // Every remaining scalar the form binds, set through the VM.
            vm.EditSubtitle = "New Subtitle";
            vm.EditAltTitle = "New AltTitle";
            vm.EditExternalId = "EXT-9";
            vm.EditSeriesNumber = "7";
            vm.EditPubDate = "2021";
            vm.EditPages = 321;
            vm.EditCopies = 4;
            vm.EditReadCount = 2;
            vm.EditKeywords = "kw1 kw2";
            vm.EditComments = "a comment";
            vm.EditBookInfo = "some info";
            vm.EditFavorite = true;
            vm.EditSigned = true;
            vm.EditOutOfPrint = true;
            vm.EditPurchasePrice = 12.50m;
            vm.EditPurchaseCurrency = "USD";
            vm.EditListPrice = 19.99m;
            vm.EditListPriceCurrency = "EUR";
            vm.EditPurchaseDate = "2020-01-02";
            vm.EditCopyrightDate = "2019";
            vm.EditPubPlace = "London";
            vm.EditMediaLink = "https://example.test/x";
            vm.EditDisplay = false;
            vm.Issn = "1234-5678";
            vm.Lccn = "LCCN-1";
            vm.DeweyDecimal = "823.9";
            vm.CallNumber = "CN-1";
            vm.Dimensions = "5x8";
            vm.Weight = 1.25m;
            vm.ItemValue = 40m;
            vm.ValuationDate = "2022-03-04";
            vm.AmazonNewValue = 15m;
            vm.AmazonUsedValue = 9m;
            vm.AmazonCollectibleValue = 55m;
            vm.AmazonNewCount = 3;
            vm.AmazonUsedCount = 6;
            vm.AmazonCollectibleCount = 1;
            vm.SalesRank = 12345;
            vm.LexileLevel = 800;

            // Every selector: switch from the seeded "A" value to "B".
            var newPublisherId = Other(vm.Publishers.Select(p => p.PublisherId), vm.EditPublisherId);
            var newFormatId = Other(vm.Formats.Select(f => f.FormatId), vm.EditFormatId);
            var newSeriesId = Other(vm.SeriesList.Select(s => s.SeriesId), vm.EditSeriesId);
            var newLanguageId = Other(vm.Languages.Select(l => l.LanguageId), vm.EditLanguageId);
            var newEditionId = Other(vm.Editions.Select(e => e.EditionId), vm.EditEditionId);
            var newRatingId = Other(vm.Ratings.Select(r => r.RatingId), vm.EditRatingId);
            var newConditionId = Other(vm.Conditions.Select(c => c.ConditionId), vm.EditConditionId);
            var newStatusId = Other(vm.Statuses.Select(s => s.StatusId), vm.EditStatusId);
            var newReadingLevelId = Other(vm.ReadingLevels.Select(r => r.ReadingLevelId), vm.EditReadingLevelId);
            var newLocationId = Other(vm.Locations.Select(l => l.LocationId), vm.EditLocationId);
            var newOwnerId = Other(vm.Owners.Select(o => o.OwnerId), vm.EditOwnerId);
            var newPurchasePlaceId = Other(vm.PurchasePlaces.Select(p => p.PurchasePlaceId), vm.EditPurchasePlaceId);
            var newSourceId = Other(vm.Sources.Select(s => s.SourceId), vm.EditSourceId);
            vm.EditPublisherId = newPublisherId;
            vm.EditFormatId = newFormatId;
            vm.EditSeriesId = newSeriesId;
            vm.EditLanguageId = newLanguageId;
            vm.EditEditionId = newEditionId;
            vm.EditRatingId = newRatingId;
            vm.EditConditionId = newConditionId;
            vm.EditStatusId = newStatusId;
            vm.EditReadingLevelId = newReadingLevelId;
            vm.EditLocationId = newLocationId;
            vm.EditOwnerId = newOwnerId;
            vm.EditPurchasePlaceId = newPurchasePlaceId;
            vm.EditSourceId = newSourceId;

            // Contributors: add a second (Editor). Categories: select the second one too.
            vm.AddContributorCommand.Execute(null);
            var addedContributor = vm.Contributors.Last();
            addedContributor.SearchText = "New Editor";
            addedContributor.RoleId = vm.ContributorRoles.First(r => r.Code == "Editor").ContributorRoleId;
            vm.CategoryRows.First(c => !c.IsSelected).IsSelected = true;

            Assert.True(vm.HasUnsavedChanges);

            var saveButton = window.ButtonFor(vm.SaveCommand);
            Assert.True(saveButton.IsEnabled);
            await ((IAsyncRelayCommand)saveButton.Command!).ExecuteAsync(null);

            var saved = await host.Resolve<IBookService>().GetBookByIdAsync(bookId, ct);
            Assert.NotNull(saved);
            Assert.Equal("Edited Title", saved!.Title);
            Assert.Equal("2222222222", saved.Isbn);
            Assert.Equal("New Subtitle", saved.Subtitle);
            Assert.Equal("New AltTitle", saved.AltTitle);
            Assert.Equal("EXT-9", saved.ExternalId);
            Assert.Equal("7", saved.SeriesNumber);
            Assert.Equal("2021", saved.PubDate);
            Assert.Equal(321, saved.Pages);
            Assert.Equal(4, saved.Copies);
            Assert.Equal(2, saved.ReadCount);
            Assert.Equal("kw1 kw2", saved.Keywords);
            Assert.Equal("a comment", saved.Comments);
            Assert.Equal("some info", saved.BookInfo);
            Assert.True(saved.Favorite);
            Assert.True(saved.Signed);
            Assert.True(saved.OutOfPrint);
            Assert.Equal(12.50m, saved.PurchasePrice);
            Assert.Equal("USD", saved.PurchaseCurrency);
            Assert.Equal(19.99m, saved.ListPrice);
            Assert.Equal("EUR", saved.ListPriceCurrency);
            Assert.Equal("2019", saved.CopyrightDate);
            Assert.Equal("London", saved.PubPlace);
            Assert.Equal("https://example.test/x", saved.MediaLink);
            Assert.False(saved.Display);
            Assert.Equal("1234-5678", saved.Issn);
            Assert.Equal("LCCN-1", saved.Lccn);
            Assert.Equal("823.9", saved.DeweyDecimal);
            Assert.Equal("CN-1", saved.CallNumber);
            Assert.Equal("5x8", saved.Dimensions);
            Assert.Equal(1.25m, saved.Weight);
            Assert.Equal(40m, saved.ItemValue);
            Assert.Equal(15m, saved.AmazonNewValue);
            Assert.Equal(9m, saved.AmazonUsedValue);
            Assert.Equal(55m, saved.AmazonCollectibleValue);
            Assert.Equal(3, saved.AmazonNewCount);
            Assert.Equal(6, saved.AmazonUsedCount);
            Assert.Equal(1, saved.AmazonCollectibleCount);
            Assert.Equal(12345, saved.SalesRank);
            Assert.Equal(800, saved.LexileLevel);

            Assert.Equal(newPublisherId, saved.PublisherId);
            Assert.Equal(newFormatId, saved.FormatId);
            Assert.Equal(newSeriesId, saved.SeriesId);
            Assert.Equal(newLanguageId, saved.LanguageId);
            Assert.Equal(newEditionId, saved.EditionId);
            Assert.Equal(newRatingId, saved.RatingId);
            Assert.Equal(newConditionId, saved.ConditionId);
            Assert.Equal(newStatusId, saved.StatusId);
            Assert.Equal(newReadingLevelId, saved.ReadingLevelId);
            Assert.Equal(newLocationId, saved.LocationId);
            Assert.Equal(newOwnerId, saved.OwnerId);
            Assert.Equal(newPurchasePlaceId, saved.PurchasePlaceId);
            Assert.Equal(newSourceId, saved.SourceId);
            Assert.Equal(2020, saved.PurchaseDate?.Year);
            Assert.Equal(2022, saved.ValuationDate?.Year);

            Assert.Equal(2, saved.Contributors.Count);
            Assert.Contains(saved.Contributors, c => c.Person?.DisplayName == "New Editor");
            Assert.Contains(saved.Contributors, c => c.Person?.DisplayName == "Orig Author");
            Assert.Equal(2, saved.Categories.Count);

            var list = host.Resolve<BookListViewModel>();
            await list.LoadBooksAsync(ct);
            Assert.Contains(list.Books, b => b.Title == "Edited Title");
            window.Close();
        });
    }

    [Fact]
    public async Task EditingThenDiscarding_RevertsEveryFieldAndPersistsNothing()
    {
        var ct = TestContext.Current.CancellationToken;

        // The discard path routes through the unsaved-changes dialog; fake it to answer "Discard".
        var windowService = Substitute.For<IWindowService>();
        windowService.ShowUnsavedChangesDialogAsync(Arg.Any<string>()).Returns(UnsavedChangesResult.Discard);

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(windowService));
            var bookId = await EditSample.SeedAsync(host, ct);

            var vm = host.Resolve<FullDetailsWindowViewModel>();
            Assert.True(await vm.LoadBookAsync(bookId));
            var window = new FullDetailsWindow { DataContext = vm };
            window.Show();
            Ui.Pump();

            var originalFormatId = vm.EditFormatId;
            vm.EditTitle = "Should Not Persist";
            vm.EditIsbn = "9999999999";
            vm.EditFavorite = true;
            vm.EditFormatId = Other(vm.Formats.Select(f => f.FormatId), vm.EditFormatId);
            Assert.True(vm.HasUnsavedChanges);

            var cancelButton = window.ButtonFor(vm.CancelEditCommand);
            await ((IAsyncRelayCommand)cancelButton.Command!).ExecuteAsync(null);

            // The edit fields snap back to the loaded values...
            Assert.False(vm.HasUnsavedChanges);
            Assert.Equal(EditSample.OriginalTitle, vm.EditTitle);
            Assert.Equal(EditSample.OriginalIsbn, vm.EditIsbn);
            Assert.False(vm.EditFavorite);
            Assert.Equal(originalFormatId, vm.EditFormatId);

            // ...and nothing reached the database.
            var saved = await host.Resolve<IBookService>().GetBookByIdAsync(bookId, ct);
            Assert.Equal(EditSample.OriginalTitle, saved!.Title);
            Assert.Equal(EditSample.OriginalIsbn, saved.Isbn);
            Assert.False(saved.Favorite);
            Assert.Equal(originalFormatId, saved.FormatId);
            window.Close();
        });
    }

    private static TextBox FindBox(Window window, string currentText) =>
        window.Descendants<TextBox>().First(t => t.Text == currentText);


    private static int Other(IEnumerable<int> ids, int? current) => ids.First(id => id != current);
}
