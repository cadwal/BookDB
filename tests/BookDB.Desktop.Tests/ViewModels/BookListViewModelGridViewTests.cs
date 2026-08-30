using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Services;
using BookDB.Desktop.Tests.Helpers;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using CommunityToolkit.Mvvm.Messaging;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

/// <summary>
/// The book list's persisted list/grid view mode: InitializeAsync restores the saved choice, and the
/// default (no saved value) is the list.
/// </summary>
public sealed class BookListViewModelGridViewTests : IDisposable
{
    private readonly TestLookupServiceFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private BookListViewModel CreateVm(ISettingsService settings) =>
        new(new WeakReferenceMessenger(), _factory.BookService, _factory.BookSearchService,
            _factory.BookImageService, new TestLookupServiceFactory.NullWindowService(), settings,
            _factory.LookupService, new TestLookupServiceFactory.NullClipboardService(),
            Substitute.For<ILoanService>(), Substitute.For<IConnectionHealthMonitor>(),
            Substitute.For<IConnectionFailureClassifier>(), Substitute.For<IRecatalogFlowService>());

    [Fact]
    public async Task Initialize_RestoresGridView_WhenPersisted()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
        settings.GetAsync("BookList_ViewMode", Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>("Grid"));

        var vm = CreateVm(settings);
        await vm.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.True(vm.IsGridView);
    }

    [Fact]
    public async Task Initialize_DefaultsToList_WhenNothingPersisted()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));

        var vm = CreateVm(settings);
        await vm.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.False(vm.IsGridView);
    }
}
