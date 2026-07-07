using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Proves the binding-error gate: a valid binding raises nothing (and <see cref="HeadlessTest.RunUi"/> passes),
/// while a binding to a missing property is captured — so any binding failure in a real test fails that test.
/// </summary>
public class HarnessSelfTests : HeadlessTest
{
    [Fact]
    public Task ValidBinding_RaisesNoBindingError() => RunUi(() =>
    {
        var control = new TextBlock { DataContext = "hello" };
        control.Bind(TextBlock.TextProperty, new Binding(".")); // bind to the DataContext itself — valid
        var window = new Window { Content = control };
        window.Show();
        Dispatcher.UIThread.RunJobs();

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
            var window = new Window { Content = control };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.Close();
            return Task.CompletedTask;
        });

        Assert.NotEmpty(errors);
    }
}
