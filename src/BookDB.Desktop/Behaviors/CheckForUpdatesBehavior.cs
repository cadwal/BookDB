using System;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;
using BookDB.Desktop.ViewModels;

namespace BookDB.Desktop.Behaviors;

/// <summary>
/// Kicks off the weekly update check when the main window first opens — the window owns it so the
/// non-blocking check never races the splash screen. The command itself is fire-and-forget and fails
/// silently, so nothing here awaits it.
/// </summary>
public class CheckForUpdatesBehavior : Behavior<Window>
{
    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is not null)
            AssociatedObject.Opened += OnOpened;
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
            AssociatedObject.Opened -= OnOpened;
        base.OnDetaching();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (AssociatedObject?.DataContext is MainWindowViewModel vm)
            vm.CheckForUpdatesCommand.Execute(null);
    }
}
