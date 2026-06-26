using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

/// <summary>
/// CheckOutDialogViewModel CanConfirm gating. The typed text is the single source of truth (no SelectedItem
/// binding), so Check Out is enabled whenever the borrower box is non-empty.
/// </summary>
public sealed class CheckOutDialogViewModelTests
{
    private static (CheckOutDialogViewModel vm, ILoanService loanService, IBorrowerService borrowerService) CreateSut()
    {
        var loanService = Substitute.For<ILoanService>();
        var borrowerService = Substitute.For<IBorrowerService>();
        borrowerService.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Borrower>>(Array.Empty<Borrower>()));
        var vm = new CheckOutDialogViewModel(loanService, borrowerService);
        return (vm, loanService, borrowerService);
    }

    [Fact]
    public async Task CanConfirm_DisabledWhenSearchTextEmpty()
    {
        var (vm, _, _) = CreateSut();
        await vm.InitializeAsync(1);
        Assert.False(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task CanConfirm_EnabledWhenSearchTextEntered()
    {
        var (vm, _, _) = CreateSut();
        await vm.InitializeAsync(1);
        vm.SearchText = "Anna Svensson";
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }
}
