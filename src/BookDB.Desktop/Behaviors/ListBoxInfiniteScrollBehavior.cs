using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using BookDB.Desktop.ViewModels;

namespace BookDB.Desktop.Behaviors;

/// <summary>
/// Loads the next page when the cover grid is scrolled near the bottom — the grid's equivalent of the
/// DataGrid's row-based <see cref="InfiniteScrollBehavior"/> (a WrapPanel has no LoadingRow to hook).
/// Listens for the bubbling <see cref="ScrollViewer.ScrollChangedEvent"/> so it works no matter when the
/// ListBox template is applied (the grid starts hidden in list mode). The VM's own guards
/// (IsLoadingMore / IsAllLoaded) keep repeated scroll events from stacking loads.
/// </summary>
public class ListBoxInfiniteScrollBehavior : Behavior<ListBox>
{
    /// <summary>Pixels from the bottom at which to start loading the next page.</summary>
    public double NearBottomThreshold { get; set; } = 300;

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject?.AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged);
    }

    protected override void OnDetaching()
    {
        AssociatedObject?.RemoveHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged);
        base.OnDetaching();
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (AssociatedObject?.DataContext is not BookListViewModel vm) return;
        if (vm.IsLoadingMore || vm.IsAllLoaded) return;

        var scrollViewer = e.Source as ScrollViewer
            ?? AssociatedObject.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer is null) return;

        var remaining = scrollViewer.Extent.Height - (scrollViewer.Offset.Y + scrollViewer.Viewport.Height);
        if (remaining <= NearBottomThreshold)
            vm.LoadMoreCommand.Execute(null);
    }
}
