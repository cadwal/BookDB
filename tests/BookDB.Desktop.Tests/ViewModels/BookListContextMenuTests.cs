using System.Threading.Tasks;
using BookDB.Desktop.Services;
using BookDB.Desktop.Tests.Helpers;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using CommunityToolkit.Mvvm.Messaging;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

public sealed class BookListContextMenuTests
{
    private sealed class CapturingClipboardService : IClipboardService
    {
        public string? LastText { get; private set; }
        public Task SetTextAsync(string text) { LastText = text; return Task.CompletedTask; }
    }

    private static (BookListViewModel vm, TestLookupServiceFactory factory, CapturingClipboardService clipboard) CreateSut()
    {
        var factory = new TestLookupServiceFactory();
        var messenger = new WeakReferenceMessenger();
        var clipboard = new CapturingClipboardService();
        var vm = new BookListViewModel(
            messenger,
            factory.BookService,
            factory.BookSearchService,
            factory.BookImageService,
            new TestLookupServiceFactory.NullWindowService(),
            new TestLookupServiceFactory.NullSettingsService(),
            factory.LookupService,
            clipboard,
            Substitute.For<ILoanService>());
        return (vm, factory, clipboard);
    }

    [Fact]
    public void CanBulkEdit_ReturnsTrueForSingleSelection()
    {
        var (vm, factory, _) = CreateSut();
        using (factory)
        {
            vm.SelectedBooks.Add(new BookRowViewModel { BookId = 1, Title = "Test Book" });
            Assert.True(vm.BulkEditCommand.CanExecute(null));
        }
    }

    [Fact]
    public async Task CopyIsbnCommand_CopiesNewlineSeparatedIsbn_ForMultiSelection()
    {
        var (vm, factory, clipboard) = CreateSut();
        using (factory)
        {
            vm.SelectedBooks.Add(new BookRowViewModel { BookId = 1, Title = "Book A", Isbn = "9780001234567" });
            vm.SelectedBooks.Add(new BookRowViewModel { BookId = 2, Title = "Book B", Isbn = "9780009876543" });
            await vm.CopyIsbnCommand.ExecuteAsync(null);
            Assert.Equal("9780001234567\n9780009876543", clipboard.LastText);
        }
    }

}
