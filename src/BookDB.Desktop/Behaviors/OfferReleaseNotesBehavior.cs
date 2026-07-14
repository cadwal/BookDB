using System;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;
using BookDB.Desktop.ViewModels;

namespace BookDB.Desktop.Behaviors;

/// <summary>
/// Fires the once-per-version release-notes offer when the main window first opens. The window is
/// the trigger (not app startup code) so the prompt can own it and never races the splash screen.
/// </summary>
public class OfferReleaseNotesBehavior : Behavior<Window>
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
            vm.OfferReleaseNotesCommand.Execute(null);
    }
}
