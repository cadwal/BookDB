using BookDB.Desktop.Services;
using BookDB.Desktop.Tests.Helpers;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using CommunityToolkit.Mvvm.Messaging;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

/// <summary>
/// Tests for BookListViewModel CanCheckOut/CanCheckIn enable/disable logic.
/// </summary>
public sealed class LoanContextMenuTests
{
    private static (BookListViewModel vm, TestLookupServiceFactory factory) CreateSut()
    {
        var factory = new TestLookupServiceFactory();
        var messenger = new WeakReferenceMessenger();
        var loanService = Substitute.For<ILoanService>();
        var vm = new BookListViewModel(
            messenger,
            factory.BookService,
            factory.BookSearchService,
            factory.BookImageService,
            new TestLookupServiceFactory.NullWindowService(),
            new TestLookupServiceFactory.NullSettingsService(),
            factory.LookupService,
            new TestLookupServiceFactory.NullClipboardService(),
            loanService);
        return (vm, factory);
    }

    [Fact]
    public void CanCheckOut_TrueWhenNotLoaned()
    {
        var (vm, factory) = CreateSut();
        using (factory)
        {
            vm.SelectedBooks.Add(new BookRowViewModel { BookId = 1, Title = "Test Book", IsLoaned = false });
            Assert.True(vm.CheckOutCommand.CanExecute(null));
        }
    }

    [Fact]
    public void CanCheckOut_FalseWhenLoaned()
    {
        var (vm, factory) = CreateSut();
        using (factory)
        {
            vm.SelectedBooks.Add(new BookRowViewModel { BookId = 1, Title = "Test Book", IsLoaned = true });
            Assert.False(vm.CheckOutCommand.CanExecute(null));
        }
    }

    [Fact]
    public void CanCheckIn_TrueWhenLoaned()
    {
        var (vm, factory) = CreateSut();
        using (factory)
        {
            vm.SelectedBooks.Add(new BookRowViewModel { BookId = 1, Title = "Test Book", IsLoaned = true });
            Assert.True(vm.CheckInCommand.CanExecute(null));
        }
    }

    [Fact]
    public void CanCheckIn_FalseWhenNotLoaned()
    {
        var (vm, factory) = CreateSut();
        using (factory)
        {
            vm.SelectedBooks.Add(new BookRowViewModel { BookId = 1, Title = "Test Book", IsLoaned = false });
            Assert.False(vm.CheckInCommand.CanExecute(null));
        }
    }
}
