using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;

namespace BookDB.Desktop.Behaviors;

/// <summary>
/// Focuses the attached control once after it is first loaded into the visual tree.
/// Use on the first input field of dialogs and windows where the field is always visible.
/// </summary>
public class FocusOnLoadedBehavior : Behavior<Control>
{
    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is not null)
            AssociatedObject.Loaded += OnLoaded;
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
            AssociatedObject.Loaded -= OnLoaded;
        base.OnDetaching();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (AssociatedObject is not null)
            AssociatedObject.Loaded -= OnLoaded;
        Dispatcher.UIThread.Post(() => AssociatedObject?.Focus(), DispatcherPriority.Loaded);
    }
}
