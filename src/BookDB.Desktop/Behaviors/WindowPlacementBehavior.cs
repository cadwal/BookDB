using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;
using BookDB.Desktop.ViewModels;

namespace BookDB.Desktop.Behaviors;

/// <summary>
/// Restores the persisted main-window position on open — only when it lands on a live screen,
/// so a disconnected monitor never strands the window off-desktop — and records the position
/// on closing for the next session.
/// </summary>
public class WindowPlacementBehavior : Behavior<Window>
{
    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is not null)
        {
            AssociatedObject.Opened += OnOpened;
            AssociatedObject.Closing += OnClosing;
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
        {
            AssociatedObject.Opened -= OnOpened;
            AssociatedObject.Closing -= OnClosing;
        }
        base.OnDetaching();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (AssociatedObject is { DataContext: MainWindowViewModel vm } window
            && !double.IsNaN(vm.WindowLeft) && !double.IsNaN(vm.WindowTop))
        {
            var candidate = new PixelPoint((int)vm.WindowLeft, (int)vm.WindowTop);
            if (window.Screens.All.Any(s => s.Bounds.Contains(candidate)))
                window.Position = candidate;
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (AssociatedObject is { DataContext: MainWindowViewModel vm } window)
        {
            vm.WindowLeft = window.Position.X;
            vm.WindowTop = window.Position.Y;
        }
    }
}
