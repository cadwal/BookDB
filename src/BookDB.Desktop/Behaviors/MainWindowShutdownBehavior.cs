using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;
using BookDB.Desktop.ViewModels;

namespace BookDB.Desktop.Behaviors;

/// <summary>
/// Guards the main window's close behind the aggregate shutdown confirmation (running batch,
/// dirty inline edit, guarded secondary windows). The close is always cancelled first and
/// re-issued only after every guard agrees, because the confirmations are async and the app
/// (ShutdownMode.OnMainWindowClose) exits the instant the main window finishes closing.
/// </summary>
public class MainWindowShutdownBehavior : Behavior<Window>
{
    private bool _closeConfirmed;
    private bool _confirmInFlight;

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
        if (AssociatedObject?.DataContext is not MainWindowViewModel vm) return;

        e.Cancel = true;
        if (_confirmInFlight) return;
        _confirmInFlight = true;
        _ = ConfirmAndCloseAsync(vm);
    }

    private async Task ConfirmAndCloseAsync(MainWindowViewModel vm)
    {
        try
        {
            if (await vm.ConfirmShutdownAsync())
            {
                _closeConfirmed = true;
                AssociatedObject?.Close();
            }
        }
        finally
        {
            _confirmInFlight = false;
        }
    }
}
