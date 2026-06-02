using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;
using BookDB.Desktop.ViewModels;

namespace BookDB.Desktop.Behaviors;

/// <summary>
/// Attaches to a <see cref="Window"/> and intercepts the Closing event.
/// If the DataContext implements <see cref="ICloseGuard"/> and
/// <see cref="ICloseGuard.ShouldGuardClose"/> is true, the close is
/// cancelled and the guard's async confirmation is awaited.
/// </summary>
public class WindowCloseGuardBehavior : Behavior<Window>
{
    private bool _closeConfirmed;

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is not null)
            AssociatedObject.Closing += OnWindowClosing;
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
            AssociatedObject.Closing -= OnWindowClosing;
        base.OnDetaching();
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed) return;

        if (AssociatedObject?.DataContext is ICloseGuard guard && guard.ShouldGuardClose)
        {
            e.Cancel = true;
            _ = ConfirmAndCloseAsync(guard);
        }
    }

    private async Task ConfirmAndCloseAsync(ICloseGuard guard)
    {
        var canClose = await guard.ConfirmCloseAsync();
        if (canClose)
        {
            _closeConfirmed = true;
            AssociatedObject?.Close();
        }
    }
}
