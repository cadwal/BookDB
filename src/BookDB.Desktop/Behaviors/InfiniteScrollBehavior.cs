using System;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;
using BookDB.Desktop.ViewModels;

namespace BookDB.Desktop.Behaviors;

public class InfiniteScrollBehavior : Behavior<DataGrid>
{
    private DispatcherTimer? _debounceTimer;
    private BookListViewModel? _viewModel;

    /// <summary>
    /// Number of rows from the bottom at which to trigger loading.
    /// </summary>
    public int NearBottomThreshold { get; set; } = 20;

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is null) return;
        AssociatedObject.LoadingRow += OnLoadingRow;
        AssociatedObject.DataContextChanged += OnDataContextChanged;
        OnDataContextChanged(AssociatedObject, EventArgs.Empty);
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
        {
            AssociatedObject.LoadingRow -= OnLoadingRow;
            AssociatedObject.DataContextChanged -= OnDataContextChanged;
        }

        _viewModel = null;
        _debounceTimer?.Stop();
        _debounceTimer = null;

        base.OnDetaching();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _viewModel = AssociatedObject?.DataContext as BookListViewModel;
    }

    private void OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (_viewModel is null) return;
        if (_viewModel.IsLoadingMore || _viewModel.IsAllLoaded) return;
        if (_debounceTimer is { IsEnabled: true }) return;

        var rowIndex = e.Row.Index;
        var count = _viewModel.Books.Count;
        if (count == 0) return;
        if (rowIndex < count - NearBottomThreshold) return;

        var vmSnapshot = _viewModel;
        _debounceTimer?.Stop();
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer?.Stop();
            _debounceTimer = null;
            if (vmSnapshot is { IsLoadingMore: false, IsAllLoaded: false })
                vmSnapshot.LoadMoreCommand.Execute(null);
        };
        _debounceTimer.Start();
    }
}
