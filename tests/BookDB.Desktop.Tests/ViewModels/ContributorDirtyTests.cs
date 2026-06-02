using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.Messaging;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

/// <summary>
/// Tests that HasUnsavedChanges stays false when OnContributorsTabActivating() is active.
/// Uses FullDetailsWindowViewModel (the concrete subclass) because BookEditViewModelBase is abstract.
/// </summary>
public sealed class ContributorDirtyTests
{
    private static FullDetailsWindowViewModel CreateVm()
    {
        var bookSvc = Substitute.For<IBookService>();
        var bookImgSvc = Substitute.For<IBookImageService>();
        var lookupSvc = Substitute.For<ILookupService>();
        var fileSvc = Substitute.For<IFilePickerService>();
        var winSvc = Substitute.For<IWindowService>();
        var msgr = Substitute.For<IMessenger>();
        var httpFactory = Substitute.For<IHttpClientFactory>();

        // Provide minimal ILookupService stubs so InitializeAsync does not throw
        lookupSvc.GetContributorRolesAsync(Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<ContributorRole>>(System.Array.Empty<ContributorRole>()));
        lookupSvc.GetAllAsync<Person>(Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Person>>(System.Array.Empty<Person>()));

        var loanSvc = Substitute.For<ILoanService>();
        return new FullDetailsWindowViewModel(bookSvc, bookImgSvc, lookupSvc,
                                              fileSvc, msgr, winSvc, httpFactory, loanSvc);
    }

    [Fact]
    public void HasUnsavedChanges_StaysFalse_WhileSuppressContributorDirtyIsActive()
    {
        var vm = CreateVm();

        // Simulate a contributor already loaded from the book (as CopyBookToEditFields does).
        // Adding a row wires up the PropertyChanged handler on the row.
        var row = new ContributorRowViewModel { PersonName = "Test Author", IsNew = false };
        vm.Contributors.Add(row);

        // Reset dirty — simulates the state after CopyBookToEditFields completes
        // (which also runs a Loaded-priority suppression post, but we clear manually here
        // to isolate the OnContributorsTabActivating behaviour under test).
        vm.HasUnsavedChanges = false;

        // Now simulate the user switching to the Contributors tab.
        // This sets _suppressContributorDirty = true.
        vm.OnContributorsTabActivating();

        // While suppression is active, Avalonia fires PropertyChanged on the AutoCompleteBox
        // TwoWay binding (lazy tab render re-initializes the binding value).
        row.PersonName = "Test Author";   // same value, but PropertyChanged fires regardless

        Assert.False(vm.HasUnsavedChanges,
            "HasUnsavedChanges should stay false while OnContributorsTabActivating suppression is active.");
    }

    [Fact]
    public void HasUnsavedChanges_BecomesTrue_WhenNotSuppressed()
    {
        var vm = CreateVm();

        // Add contributor WITHOUT calling OnContributorsTabActivating
        var row = new ContributorRowViewModel { PersonName = "Test Author", IsNew = false };
        vm.Contributors.Add(row);

        // Reset after Add (which may have dirtied due to CollectionChanged)
        vm.HasUnsavedChanges = false;

        // Now fire a property change — no suppression active
        row.PersonName = "Test Author Changed";

        Assert.True(vm.HasUnsavedChanges,
            "HasUnsavedChanges should become true when no suppression is active and a contributor property changes.");
    }
}
