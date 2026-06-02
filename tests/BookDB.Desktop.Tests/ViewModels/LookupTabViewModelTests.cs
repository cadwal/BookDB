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
/// LookupTabViewModel subclass property and table-name contracts.
/// </summary>
public sealed class LookupTabViewModelTests
{
    private static ILookupManagementService MakeService() => Substitute.For<ILookupManagementService>();
    private static IWindowService MakeWindowService() => Substitute.For<IWindowService>();
    private static IMessenger MakeMessenger() => Substitute.For<IMessenger>();

    private static ILookupService MakeLookupService()
    {
        var svc = Substitute.For<ILookupService>();
        svc.GetAllAsync<Category>(Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Category>>(System.Array.Empty<Category>()));
        svc.GetAllAsync<PurchasePlace>(Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<PurchasePlace>>(System.Array.Empty<PurchasePlace>()));
        return svc;
    }

    // CategoryTabViewModel.SupportsMerge == true
    [Fact]
    public void CategoryTabViewModel_SupportsMerge_IsTrue()
    {
        var vm = new CategoryTabViewModel(MakeService(), MakeLookupService(), MakeWindowService(), MakeMessenger());

        Assert.True(vm.SupportsMerge,
            "CategoryTabViewModel overrides SupportsMerge => true.");
    }

    // PurchasePlaceTabViewModel.SupportsMerge == true
    [Fact]
    public void PurchasePlaceTabViewModel_SupportsMerge_IsTrue()
    {
        var vm = new PurchasePlaceTabViewModel(MakeService(), MakeLookupService(), MakeWindowService(), MakeMessenger());

        Assert.True(vm.SupportsMerge,
            "PurchasePlaceTabViewModel must override SupportsMerge => true.");
    }

    // CategoryTabViewModel table name is "Category"
    [Fact]
    public void CategoryTabViewModel_TableName_IsCategory()
    {
        var vm = new CategoryTabViewModel(MakeService(), MakeLookupService(), MakeWindowService(), MakeMessenger());

        var field = typeof(LookupTabViewModel)
            .GetField("TableName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var tableName = field?.GetValue(vm) as string;

        Assert.Equal("Category", tableName);
    }

    // PurchasePlaceTabViewModel table name is "PurchasePlace"
    [Fact]
    public void PurchasePlaceTabViewModel_TableName_IsPurchasePlace()
    {
        var vm = new PurchasePlaceTabViewModel(MakeService(), MakeLookupService(), MakeWindowService(), MakeMessenger());

        var field = typeof(LookupTabViewModel)
            .GetField("TableName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var tableName = field?.GetValue(vm) as string;

        Assert.Equal("PurchasePlace", tableName);
    }
}
