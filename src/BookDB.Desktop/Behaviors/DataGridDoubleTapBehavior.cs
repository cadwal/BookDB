using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Xaml.Interactivity;
using BookDB.Desktop.Messages;
using BookDB.Desktop.ViewModels;
using CommunityToolkit.Mvvm.Messaging;

namespace BookDB.Desktop.Behaviors;

/// <summary>
/// Sends a <see cref="BookSelectedMessage"/> with <c>openInEditMode = true</c>
/// when the user double-taps a row in the DataGrid.
/// Replaces the DoubleTapped code-behind in BookListView.
/// </summary>
public class DataGridDoubleTapBehavior : Behavior<DataGrid>
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
