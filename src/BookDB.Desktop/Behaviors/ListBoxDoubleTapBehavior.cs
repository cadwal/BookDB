using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Xaml.Interactivity;
using BookDB.Desktop.Messages;
using BookDB.Desktop.ViewModels;
using CommunityToolkit.Mvvm.Messaging;

namespace BookDB.Desktop.Behaviors;

/// <summary>
/// Opens the double-tapped cover tile in edit mode via <see cref="BookSelectedMessage"/> — the grid's
/// mirror of <see cref="DataGridDoubleTapBehavior"/>, so a double-click opens the same book flow as the list.
/// </summary>
public class ListBoxDoubleTapBehavior : Behavior<ListBox>
{
    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is not null)
            AssociatedObject.DoubleTapped += OnDoubleTapped;
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
            AssociatedObject.DoubleTapped -= OnDoubleTapped;
        base.OnDetaching();
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (AssociatedObject?.SelectedItem is BookRowViewModel row)
            WeakReferenceMessenger.Default.Send(new BookSelectedMessage(row.BookId, openInEditMode: true));
    }
}
