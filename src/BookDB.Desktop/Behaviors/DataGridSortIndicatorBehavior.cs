using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using BookDB.Desktop.ViewModels;

namespace BookDB.Desktop.Behaviors;

/// <summary>
/// Toggles <c>sort-asc</c> / <c>sort-desc</c> CSS classes on <see cref="DataGridColumnHeader"/>
/// controls when the parent <see cref="BookListViewModel.SortColumn"/> or
/// <see cref="BookListViewModel.SortAscending"/> changes.
///
/// Subscribes to the VM's <see cref="INotifyPropertyChanged"/> rather than the DataGrid's
/// Sorting event, so it fires AFTER <see cref="DataGridSortBehavior"/> has updated the VM
/// and there is no risk of re-entrant Sorting loops.
/// </summary>
public class DataGridSortIndicatorBehavior : Behavior<DataGrid>
{
    private BookListViewModel? _viewModel;

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is null) return;
        AssociatedObject.DataContextChanged += OnDataContextChanged;
        OnDataContextChanged(AssociatedObject, EventArgs.Empty);
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
            AssociatedObject.DataContextChanged -= OnDataContextChanged;
        UnsubscribeVm();
        base.OnDetaching();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnsubscribeVm();
        _viewModel = AssociatedObject?.DataContext as BookListViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        // Defer so the visual tree (column headers) is available before we walk it.
        Dispatcher.UIThread.Post(UpdateHeaderClasses, DispatcherPriority.Background);
    }

    private void UnsubscribeVm()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BookListViewModel.SortColumn) or nameof(BookListViewModel.SortAscending))
            Dispatcher.UIThread.Post(UpdateHeaderClasses, DispatcherPriority.Render);
    }

    private void UpdateHeaderClasses()
    {
        if (AssociatedObject is null || _viewModel is null) return;

        // Match headers by Content string, not by index.
        // GetVisualDescendants includes a filler DataGridColumnHeader (Content=null)
        // that has no corresponding column, making index-based pairing off by one.
        var headers = AssociatedObject.GetVisualDescendants()
                                      .OfType<DataGridColumnHeader>()
                                      .ToList();

        foreach (var column in AssociatedObject.Columns)
        {
            var headerText = column.Header as string;
            var header     = headers.FirstOrDefault(h => h.Content as string == headerText);
            if (header is null) continue;

            var isSorted = !string.IsNullOrEmpty(column.SortMemberPath)
                           && column.SortMemberPath == _viewModel.SortColumn;
            if (isSorted)
                SetSortClass(header, _viewModel.SortAscending);
            else
                RemoveSortClasses(header);
        }

        // Clean up the filler header and any unmatched headers.
        var columnHeaders = AssociatedObject.Columns
                                            .Select(c => c.Header as string)
                                            .ToHashSet();
        foreach (var header in headers.Where(h => !columnHeaders.Contains(h.Content as string)))
            RemoveSortClasses(header);
    }

    private static void SetSortClass(DataGridColumnHeader header, bool ascending)
    {
        var add    = ascending ? "sort-asc" : "sort-desc";
        var remove = ascending ? "sort-desc" : "sort-asc";
        if (header.Classes.Contains(remove)) header.Classes.Remove(remove);
        if (!header.Classes.Contains(add))   header.Classes.Add(add);
    }

    private static void RemoveSortClasses(DataGridColumnHeader header)
    {
        if (header.Classes.Contains("sort-asc"))  header.Classes.Remove("sort-asc");
        if (header.Classes.Contains("sort-desc")) header.Classes.Remove("sort-desc");
    }
}
