using System.Threading;
using Avalonia.Threading;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using CommunityToolkit.Mvvm.Messaging;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

/// <summary>
/// Verifies that toggling a column's visibility persists a "ColumnVisible.{Name}" setting.
/// The change handlers post the write through Dispatcher.UIThread, so each test pumps the
/// dispatcher with RunJobs() before asserting the settings service received the call.
/// </summary>
public sealed class ColumnVisibilityPersistenceTests
{
    private static BookListViewModel CreateVm(ISettingsService settingsService) =>
        new(
            new WeakReferenceMessenger(),
            Substitute.For<IBookService>(),
            Substitute.For<IBookSearchService>(),
            Substitute.For<IBookImageService>(),
            Substitute.For<IWindowService>(),
            settingsService,
            Substitute.For<ILookupService>(),
            Substitute.For<IClipboardService>(),
            Substitute.For<ILoanService>(),
            Substitute.For<BookDB.Logic.Services.IConnectionHealthMonitor>(),
            Substitute.For<BookDB.Data.Interfaces.IConnectionFailureClassifier>(),
            Substitute.For<BookDB.Desktop.Services.IRecatalogFlowService>());

    [Fact]
    public void HidingAuthorColumn_PersistsColumnVisibleAuthorKey()
    {
        var settings = Substitute.For<ISettingsService>();
        var vm = CreateVm(settings);

        // Author defaults to visible; hiding it triggers the change handler.
        vm.AuthorColumnVisible = false;
        Dispatcher.UIThread.RunJobs();

        settings.Received(1).SetAsync("ColumnVisible.Author", "False", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ShowingRatingColumn_PersistsColumnVisibleRatingKey()
    {
        var settings = Substitute.For<ISettingsService>();
        var vm = CreateVm(settings);

        // Rating defaults to hidden; showing it triggers the change handler.
        vm.RatingColumnVisible = true;
        Dispatcher.UIThread.RunJobs();

        settings.Received(1).SetAsync("ColumnVisible.Rating", "True", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ShowingStatusColumn_PersistsColumnVisibleStatusKey()
    {
        var settings = Substitute.For<ISettingsService>();
        var vm = CreateVm(settings);

        // Status defaults to hidden; showing it triggers the change handler.
        vm.StatusColumnVisible = true;
        Dispatcher.UIThread.RunJobs();

        settings.Received(1).SetAsync("ColumnVisible.Status", "True", Arg.Any<CancellationToken>());
    }
}
