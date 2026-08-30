using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Xunit;

namespace BookDB.Mobile.UITests;

/// <summary>
/// Every screen builds with a real view model, renders headless, and does so without a binding error (the gate
/// in <see cref="HeadlessTest.RunUi"/>). Broad, cheap coverage of the blank-screen / binding-typo /
/// missing-resource class that a phone can only otherwise show a person holding it.
/// </summary>
public class SmokeTests : HeadlessTest
{
    public static IEnumerable<object[]> ScreenNames => ScreenRegistry.All.Select(s => new object[] { s.Name });

    [Theory]
    [MemberData(nameof(ScreenNames))]
    public Task Screen_RendersHeadless_WithoutBindingErrors(string name) => RunUi(async () =>
    {
        var content = await ScreenRegistry.ByName(name).BuildAsync();
        var window = Phone.Show(content);

        Assert.True(window.IsVisible);
        Assert.True(content.IsEffectivelyVisible);

        window.Close();
    });
}
