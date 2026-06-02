using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;
using BookDB.Desktop.ViewModels;

namespace BookDB.Desktop.Behaviors;

public class DataGridColumnStateBehavior : Behavior<DataGrid>
{
    private DispatcherTimer? _widthCaptureTimer;
    private bool _columnRestoreApplied;

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is null) return;
        AssociatedObject.ColumnReordered += OnColumnReordered;
        AssociatedObject.LayoutUpdated += OnGridLayoutUpdated;
        AssociatedObject.DataContextChanged += OnDataContextChanged;
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
        {
            AssociatedObject.ColumnReordered -= OnColumnReordered;
            AssociatedObject.LayoutUpdated -= OnGridLayoutUpdated;
            AssociatedObject.DataContextChanged -= OnDataContextChanged;
        }
        base.OnDetaching();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (AssociatedObject?.DataContext is BookListViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            SetThumbnailColumnVisible(viewModel.ThumbnailColumnVisible);
            SetAuthorColumnVisible(viewModel.AuthorColumnVisible);
            SetSeriesColumnVisible(viewModel.SeriesColumnVisible);
            SetPublisherColumnVisible(viewModel.PublisherColumnVisible);
            SetYearColumnVisible(viewModel.YearColumnVisible);
            SetFormatColumnVisible(viewModel.FormatColumnVisible);
            SetRatingColumnVisible(viewModel.RatingColumnVisible);
            SetStatusColumnVisible(viewModel.StatusColumnVisible);
            SetLoanedToColumnVisible(viewModel.LoanedToColumnVisible);
            if (viewModel.ColumnStateRestoreReady && !_columnRestoreApplied)
            {
                // DispatcherPriority.Loaded: DataGrid must complete its first layout pass before
                // DisplayIndex assignments take effect.
                Dispatcher.UIThread.Post(() =>
                {
                    var states = viewModel.ConsumeColumnRestoreStates();
                    if (states != null)
                        ApplyColumnRestore(states);
                }, DispatcherPriority.Loaded);
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BookListViewModel.ColumnStateRestoreReady)
            && sender is BookListViewModel viewModel
            && viewModel.ColumnStateRestoreReady
            && !_columnRestoreApplied)
        {
            // DispatcherPriority.Loaded: DataGrid layout must be complete before applying DisplayIndex values.
            Dispatcher.UIThread.Post(() =>
            {
                var states = viewModel.ConsumeColumnRestoreStates();
                if (states != null)
                    ApplyColumnRestore(states);
            }, DispatcherPriority.Loaded);
        }
        else if (e.PropertyName == nameof(BookListViewModel.RowHeight))
        {
            // DataGrid doesn't automatically remeasure when RowHeight changes via binding.
            AssociatedObject?.InvalidateMeasure();
        }
        else if (e.PropertyName == nameof(BookListViewModel.PendingSelectAfterUpdate)
                 && sender is BookListViewModel vmSelect
                 && vmSelect.PendingSelectAfterUpdate is { } bookToSelect)
        {
            // Restore DataGrid selection after an in-place Books[index] replacement.
            // The Replace event clears the DataGrid selection; re-select the new instance.
            if (AssociatedObject is not null)
                AssociatedObject.SelectedItem = bookToSelect;
            vmSelect.PendingSelectAfterUpdate = null;
        }
        else if (e.PropertyName == nameof(BookListViewModel.ThumbnailColumnVisible)
                 && sender is BookListViewModel vmThumb)
        {
            // DataGridColumn is not in the visual tree, so $parent[DataGrid] XAML bindings
            // on column properties silently fall back to FallbackValue. Set IsVisible directly.
            SetThumbnailColumnVisible(vmThumb.ThumbnailColumnVisible);
        }
        else if (e.PropertyName == nameof(BookListViewModel.AuthorColumnVisible)
                 && sender is BookListViewModel vmAuthor)
            SetAuthorColumnVisible(vmAuthor.AuthorColumnVisible);
        else if (e.PropertyName == nameof(BookListViewModel.SeriesColumnVisible)
                 && sender is BookListViewModel vmSeries)
            SetSeriesColumnVisible(vmSeries.SeriesColumnVisible);
        else if (e.PropertyName == nameof(BookListViewModel.PublisherColumnVisible)
                 && sender is BookListViewModel vmPublisher)
            SetPublisherColumnVisible(vmPublisher.PublisherColumnVisible);
        else if (e.PropertyName == nameof(BookListViewModel.YearColumnVisible)
                 && sender is BookListViewModel vmYear)
            SetYearColumnVisible(vmYear.YearColumnVisible);
        else if (e.PropertyName == nameof(BookListViewModel.FormatColumnVisible)
                 && sender is BookListViewModel vmFormat)
            SetFormatColumnVisible(vmFormat.FormatColumnVisible);
        else if (e.PropertyName == nameof(BookListViewModel.RatingColumnVisible)
                 && sender is BookListViewModel vmRating)
            SetRatingColumnVisible(vmRating.RatingColumnVisible);
        else if (e.PropertyName == nameof(BookListViewModel.StatusColumnVisible)
                 && sender is BookListViewModel vmStatus)
            SetStatusColumnVisible(vmStatus.StatusColumnVisible);
        else if (e.PropertyName == nameof(BookListViewModel.LoanedToColumnVisible)
                 && sender is BookListViewModel vmLoaned)
            SetLoanedToColumnVisible(vmLoaned.LoanedToColumnVisible);
    }

    private void OnColumnReordered(object? sender, DataGridColumnEventArgs e)
    {
        if (AssociatedObject?.DataContext is BookListViewModel viewModel)
            PushColumnStatesToViewModel(viewModel);
    }

    private void OnGridLayoutUpdated(object? sender, EventArgs e)
    {
        // Debounce width capture: reset 300ms timer on each layout update
        if (!_columnRestoreApplied) return;  // ignore layout events before restore is done
        _widthCaptureTimer?.Stop();
        _widthCaptureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _widthCaptureTimer.Tick += (_, _) =>
        {
            _widthCaptureTimer?.Stop();
            _widthCaptureTimer = null;
            if (AssociatedObject?.DataContext is BookListViewModel viewModel)
                PushColumnStatesToViewModel(viewModel);
        };
        _widthCaptureTimer.Start();
    }

    private void PushColumnStatesToViewModel(BookListViewModel viewModel)
    {
        var states = new List<(string Header, int DisplayIndex, double Width)>();
        foreach (var col in AssociatedObject!.Columns)
        {
            var name = DataGridColumnEx.GetName(col);
            if (name != null && col.CanUserReorder)
                states.Add((name, col.DisplayIndex, col.ActualWidth));
        }
        viewModel.UpdateRuntimeColumnStates(states);
    }

    private void ApplyColumnRestore(IList<ColumnState> states)
    {
        // Apply widths first, then display indices (setting display index causes re-layout)
        foreach (var s in states)
        {
            var col = FindColumnByName(s.Name);
            if (col == null) continue;
            if (s.Width > 10)
                col.Width = new DataGridLength(s.Width);
        }
        // Apply display indices sorted ascending to avoid transient conflicts
        foreach (var s in states.OrderBy(s => s.DisplayIndex))
        {
            if (s.DisplayIndex <= 0) continue;
            var col = FindColumnByName(s.Name);
            if (col == null) continue;
            try { col.DisplayIndex = s.DisplayIndex; }
            catch { /* ignore if index is out of range */ }
        }
        _columnRestoreApplied = true;
    }

    private DataGridColumn? FindColumnByName(string name) =>
        AssociatedObject!.Columns.FirstOrDefault(c => DataGridColumnEx.GetName(c) == name);

    private void SetThumbnailColumnVisible(bool visible)
    {
        if (AssociatedObject is null || AssociatedObject.Columns.Count < 2) return;
        var col = FindColumnByName("Thumbnail");
        if (col is not null)
        {
            col.IsVisible = visible;
            AssociatedObject.InvalidateMeasure();
        }
    }

    private void SetAuthorColumnVisible(bool visible)
    {
        if (AssociatedObject is null) return;
        var col = FindColumnByName("Author");
        if (col is not null) { col.IsVisible = visible; AssociatedObject.InvalidateMeasure(); }
    }

    private void SetSeriesColumnVisible(bool visible)
    {
        if (AssociatedObject is null) return;
        var col = FindColumnByName("Series");
        if (col is not null) { col.IsVisible = visible; AssociatedObject.InvalidateMeasure(); }
    }

    private void SetPublisherColumnVisible(bool visible)
    {
        if (AssociatedObject is null) return;
        var col = FindColumnByName("Publisher");
        if (col is not null) { col.IsVisible = visible; AssociatedObject.InvalidateMeasure(); }
    }

    private void SetYearColumnVisible(bool visible)
    {
        if (AssociatedObject is null) return;
        var col = FindColumnByName("Year");
        if (col is not null) { col.IsVisible = visible; AssociatedObject.InvalidateMeasure(); }
    }

    private void SetFormatColumnVisible(bool visible)
    {
        if (AssociatedObject is null) return;
        var col = FindColumnByName("Format");
        if (col is not null) { col.IsVisible = visible; AssociatedObject.InvalidateMeasure(); }
    }

    private void SetRatingColumnVisible(bool visible)
    {
        if (AssociatedObject is null) return;
        var col = FindColumnByName("Rating");
        if (col is not null) { col.IsVisible = visible; AssociatedObject.InvalidateMeasure(); }
    }

    private void SetStatusColumnVisible(bool visible)
    {
        if (AssociatedObject is null) return;
        var col = FindColumnByName("Status");
        if (col is not null) { col.IsVisible = visible; AssociatedObject.InvalidateMeasure(); }
    }

    private void SetLoanedToColumnVisible(bool visible)
    {
        if (AssociatedObject is null) return;
        var col = FindColumnByName("LoanedTo");
        if (col is not null) { col.IsVisible = visible; AssociatedObject.InvalidateMeasure(); }
    }
}
