using BookDB.Desktop.Tests.Helpers;
using BookDB.Desktop.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

public sealed class AdvancedSearchViewModelTests
{
    private static AdvancedSearchViewModel CreateVm()
    {
        // AdvancedSearchViewModel constructor only stores the injected services and adds
        // one empty SearchConditionViewModel. Null services are safe for property-only tests.
        var messenger = new WeakReferenceMessenger();
        return new AdvancedSearchViewModel(messenger, null!, null!, null!);
    }

    // ---------------------------------------------------------------------------
    // CombinatorOption invariant key test
    // ---------------------------------------------------------------------------

    [Fact]
    public void AdvancedSearchViewModel_CombinatorKey_IsAlwaysInvariantEnglish()
    {
        var vm = CreateVm();

        // Combinators list must have exactly 2 entries.
        Assert.Equal(2, vm.Combinators.Count);

        // Each entry exposes an invariant Key of "AND" or "OR" — never a localized display string.
        Assert.Equal("AND", vm.Combinators[0].Key);
        Assert.Equal("OR",  vm.Combinators[1].Key);

        // Default combinator must be AND.
        Assert.Equal("AND", vm.Combinator.Key);

        // Selecting OR must update Combinator.Key to "OR".
        vm.Combinator = vm.Combinators[1];
        Assert.Equal("OR", vm.Combinator.Key);
    }
}
