using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.Messaging;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

/// <summary>
/// ManageLookupsViewModel.InitializeAsync tab-index switch contracts.
/// Uses NSubstitute to stub the lookup service calls made by each sub-tab's LoadAsync.
/// SafeLoadAsync in ManageLookupsViewModel catches and swallows exceptions, so stubs only need to
/// cover the calls that actually happen during LoadAsync (not every possible service method).
/// </summary>
public sealed class ManageLookupsViewModelTests
{
    private static ManageLookupsViewModel CreateVm()
    {
        var service = Substitute.For<ILookupManagementService>();
        var lookupService = Substitute.For<ILookupService>();
        var windowService = Substitute.For<IWindowService>();
        var messenger = Substitute.For<IMessenger>();
        var settings = Substitute.For<ISettingsService>();

        // Stub all GetAllAsync<T> calls needed by the 8 sub-tab LoadAsync methods.
        // PersonTabViewModel.LoadAsync  => GetAllAsync<Person>
        lookupService.GetAllAsync<Person>(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Person>>(System.Array.Empty<Person>()));
        // PublisherTabViewModel         => GetAllAsync<Publisher>
        lookupService.GetAllAsync<Publisher>(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Publisher>>(System.Array.Empty<Publisher>()));
        // SeriesTabViewModel            => GetAllAsync<Series>
        lookupService.GetAllAsync<Series>(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Series>>(System.Array.Empty<Series>()));
        // LocationTabViewModel          => GetAllAsync<Location>
        lookupService.GetAllAsync<Location>(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Location>>(System.Array.Empty<Location>()));
        // OwnerTabViewModel             => GetAllAsync<Owner>
        lookupService.GetAllAsync<Owner>(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Owner>>(System.Array.Empty<Owner>()));
        // LanguageTabViewModel          => GetAllAsync<Language>
        lookupService.GetAllAsync<Language>(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Language>>(System.Array.Empty<Language>()));
        // CategoryTabViewModel          => GetAllAsync<Category>
        lookupService.GetAllAsync<Category>(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Category>>(System.Array.Empty<Category>()));
        // PurchasePlaceTabViewModel     => GetAllAsync<PurchasePlace>
        lookupService.GetAllAsync<PurchasePlace>(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PurchasePlace>>(System.Array.Empty<PurchasePlace>()));
        // CollectionTabViewModel.LoadAsync => GetCollectionsAsync
        lookupService.GetCollectionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Collection>>(System.Array.Empty<Collection>()));

        return new ManageLookupsViewModel(service, lookupService, windowService, messenger, settings,
            Substitute.For<IConnectionHealthMonitor>(), Substitute.For<BookDB.Data.Interfaces.IConnectionFailureClassifier>());
    }

    // InitializeAsync("Category") => SelectedTabIndex == 6
    [Fact]
    public async Task InitializeAsync_WithCategory_SetsSelectedTabIndex6()
    {
        var vm = CreateVm();

        await vm.InitializeAsync("Category");

        Assert.Equal(6, vm.SelectedTabIndex);
    }

    // InitializeAsync("PurchasePlace") => SelectedTabIndex == 7
    [Fact]
    public async Task InitializeAsync_WithPurchasePlace_SetsSelectedTabIndex7()
    {
        var vm = CreateVm();

        await vm.InitializeAsync("PurchasePlace");

        Assert.Equal(7, vm.SelectedTabIndex);
    }

    // InitializeAsync("Unknown") => SelectedTabIndex == 0
    [Fact]
    public async Task InitializeAsync_WithUnknownString_SetsSelectedTabIndex0()
    {
        var vm = CreateVm();

        await vm.InitializeAsync("Unknown");

        Assert.Equal(0, vm.SelectedTabIndex);
    }
}
