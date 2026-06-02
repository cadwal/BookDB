using System.Threading.Tasks;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

/// <summary>
/// Tests for CheckOutDialogViewModel CanConfirm gating.
/// </summary>
public sealed class CheckOutDialogViewModelTests
{
    private static (CheckOutDialogViewModel vm, ILoanService loanService, IBorrowerService borrowerService) CreateSut()
    {
        var loanService = Substitute.For<ILoanService>();
        var borrowerService = Substitute.For<IBorrowerService>();
        var vm = new CheckOutDialogViewModel(loanService, borrowerService);
        return (vm, loanService, borrowerService);
    }

    [Fact]
    public async Task CanConfirm_DisabledWhenNoBorrowerSelected()
    {
        var (vm, _, _) = CreateSut();
        await vm.InitializeAsync(1);
        Assert.False(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task CanConfirm_EnabledWhenBorrowerSelected()
    {
        var (vm, _, _) = CreateSut();
        await vm.InitializeAsync(1);
        vm.SelectedBorrower = new ExistingBorrowerSuggestion(new Borrower { BorrowerId = 1, FirstName = "A" });
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }
}
