using System;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;
using BookDB.Desktop.ViewModels;

namespace BookDB.Desktop.Behaviors;

public sealed class DataGridSortBehavior : Behavior<DataGrid>
{
    private BookListViewModel? _viewModel;

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is null) return;
        AssociatedObject.Sorting += OnSorting;
        AssociatedObject.DataContextChanged += OnDataContextChanged;
        OnDataContextChanged(AssociatedObject, EventArgs.Empty);
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
        {
            AssociatedObject.Sorting -= OnSorting;
            AssociatedObject.DataContextChanged -= OnDataContextChanged;
        }
        _viewModel = null;
        base.OnDetaching();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _viewModel = AssociatedObject?.DataContext as BookListViewModel;
    }

    private void OnSorting(object? sender, DataGridColumnEventArgs e)
    {
        if (_viewModel is null) return;

        // SortMemberPath is the stable, locale-independent column identifier and
        // already equals the VM SortColumn value — no header-text mapping needed.
        var sortColumn = e.Column.SortMemberPath;
        if (string.IsNullOrEmpty(sortColumn)) return;

        // Cancel Avalonia's built-in in-memory sort.
        // We reload from the DB instead; DB-side sort covers the full collection.
        e.Handled = true;

        bool ascending = _viewModel.SortColumn == sortColumn
            ? !_viewModel.SortAscending
            : true;

        _viewModel.SortColumn = sortColumn;
        _viewModel.SortAscending = ascending;
    }
}
