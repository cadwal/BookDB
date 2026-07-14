using System.Collections.Generic;
using System.Threading.Tasks;
using BookDB.Desktop.Services;
using BookDB.Desktop.Tests.Helpers;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using CommunityToolkit.Mvvm.Messaging;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

/// <summary>
/// The list's re-catalog entry points hand every book — ISBN-less ones included — to the shared
/// flow, so no path silently drops a book the flow could have prompted for.
/// </summary>
public class BookListRecatalogTests
{
    private static (BookListViewModel Vm, TestLookupServiceFactory Factory, IRecatalogFlowService Flow) CreateSut()
    {
        var factory = new TestLookupServiceFactory();
        var flow = Substitute.For<IRecatalogFlowService>();
        var vm = new BookListViewModel(
            new WeakReferenceMessenger(),
            factory.BookService,
            factory.BookSearchService,
            factory.BookImageService,
            new TestLookupServiceFactory.NullWindowService(),
            new TestLookupServiceFactory.NullSettingsService(),
            factory.LookupService,
            new TestLookupServiceFactory.NullClipboardService(),
            Substitute.For<ILoanService>(),
            Substitute.For<IConnectionHealthMonitor>(),
            Substitute.For<BookDB.Data.Interfaces.IConnectionFailureClassifier>(),
            flow);
        return (vm, factory, flow);
    }

    [Fact]
    public async Task RecatalogSelected_PassesEverySelectedBook_IsbnLessIncluded()
    {
        var (vm, factory, flow) = CreateSut();
        using (factory)
        {
            vm.SelectedBooks.Add(new BookRowViewModel { BookId = 1, Title = "Has Isbn", Isbn = "9780441013593" });
            vm.SelectedBooks.Add(new BookRowViewModel { BookId = 2, Title = "No Isbn" });

            await vm.RecatalogSelectedCommand.ExecuteAsync(null);

            await flow.Received(1).RecatalogAsync(Arg.Is<IReadOnlyList<RecatalogCandidate>>(books =>
                books.Count == 2 &&
                books[0] == new RecatalogCandidate(1, "Has Isbn", "9780441013593") &&
                books[1] == new RecatalogCandidate(2, "No Isbn", null)));
        }
    }

    [Fact]
    public async Task RecatalogAll_PassesEveryLoadedBook()
    {
        var (vm, factory, flow) = CreateSut();
        using (factory)
        {
            vm.Books.Add(new BookRowViewModel { BookId = 1, Title = "Has Isbn", Isbn = "9780441013593" });
            vm.Books.Add(new BookRowViewModel { BookId = 2, Title = "No Isbn" });

            await vm.RecatalogAllAsync();

            await flow.Received(1).RecatalogAsync(Arg.Is<IReadOnlyList<RecatalogCandidate>>(books =>
                books.Count == 2 && books[1] == new RecatalogCandidate(2, "No Isbn", null)));
        }
    }
}
