using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Xunit;

namespace BookDB.Mobile.UITests;

/// <summary>
/// Proves the binding-error gate the smokes rely on: a valid binding raises nothing, a binding to a property
/// that is not there is captured. Without this, a gate that had stopped listening would let every screen pass.
/// </summary>
public class HarnessSelfTests : HeadlessTest
{
    [Fact]
    public Task ValidBinding_RaisesNoBindingError() => RunUi(() =>
    {
        var control = new TextBlock { DataContext = "hello" };
        control.Bind(TextBlock.TextProperty, new Binding(".")); // bind to the DataContext itself — valid
        var window = Phone.Show(control);

        Assert.Equal("hello", control.Text);
        window.Close();
        return Task.CompletedTask;
    });

    [Fact]
    public async Task InvalidBinding_IsCapturedAsBindingError()
    {
        var errors = await CaptureBindingErrors(() =>
        {
            var control = new TextBlock { DataContext = new object() };
            control.Bind(TextBlock.TextProperty, new Binding("NoSuchProperty"));
            var window = Phone.Show(control);
            window.Close();
            return Task.CompletedTask;
        });

        Assert.NotEmpty(errors);
    }
}
