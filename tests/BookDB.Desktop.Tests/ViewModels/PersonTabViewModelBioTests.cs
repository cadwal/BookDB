using System.Threading;
using System.Threading.Tasks;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using CommunityToolkit.Mvvm.Messaging;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

public sealed class PersonTabViewModelBioTests
{
    private static PersonTabViewModel CreateVm()
    {
        var mgmt = Substitute.For<ILookupManagementService>();
        var lookup = Substitute.For<ILookupService>();
        var window = Substitute.For<IWindowService>();
        var messenger = Substitute.For<IMessenger>();

        mgmt.GetPersonBookContributionCountAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(0));
        mgmt.GetPersonBioAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PersonBioData?>(null));
        mgmt.PersonHasAuthorRoleAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        return new PersonTabViewModel(mgmt, lookup, window, messenger);
    }

    [Fact]
    public void IsBioSectionVisible_WhenNoPerson_ReturnsFalse()
    {
        var vm = CreateVm();
        Assert.False(vm.IsBioSectionVisible);
    }

    [Fact]
    public void IsBioSectionVisible_WhenPersonIdZero_ReturnsFalse()
    {
        var vm = CreateVm();
        vm.SelectedPerson = new PersonRow(0, "New", "New");
        Assert.False(vm.IsBioSectionVisible);
    }

    [Fact]
    public void IsBioSectionVisible_WhenPersonIdPositive_ReturnsTrue()
    {
        var vm = CreateVm();
        vm.SelectedPerson = new PersonRow(1, "Alice", "Alice");
        Assert.True(vm.IsBioSectionVisible);
    }

    [Fact]
    public void SaveBioCommand_WhenNoPersonSelected_CannotExecute()
    {
        var vm = CreateVm();
        Assert.False(vm.SaveBioCommand.CanExecute(null));
    }

    [Fact]
    public void SaveBioCommand_WhenPersonIdPositive_CanExecute()
    {
        var vm = CreateVm();
        vm.SelectedPerson = new PersonRow(1, "Alice", "Alice");
        Assert.True(vm.SaveBioCommand.CanExecute(null));
    }

    [Fact]
    public void OnSelectedPersonChanged_NullValue_ClearsBioEditFields()
    {
        var vm = CreateVm();

        // Select a person so SelectedPerson transitions from null → non-null → null
        vm.SelectedPerson = new PersonRow(1, "Alice", "Alice");

        // Simulate values already in the Edit fields (e.g. populated by LoadPersonBioAsync)
        vm.EditBio = "A biography.";
        vm.EditBirthDate = "1970";
        vm.EditBirthPlace = "London";
        vm.EditDeathDate = "2020";
        vm.EditDeathPlace = "Paris";
        vm.EditWebsite = "https://example.com";

        // Deselect — triggers the null-path in OnSelectedPersonChanged
        vm.SelectedPerson = null;

        Assert.Null(vm.EditBio);
        Assert.Null(vm.EditBirthDate);
        Assert.Null(vm.EditBirthPlace);
        Assert.Null(vm.EditDeathDate);
        Assert.Null(vm.EditDeathPlace);
        Assert.Null(vm.EditWebsite);
    }
}
