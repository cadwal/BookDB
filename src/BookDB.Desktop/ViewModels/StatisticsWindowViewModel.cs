using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Helpers;
using BookDB.Logic.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BookDB.Desktop.ViewModels;

/// <summary>Display-layer projection of BreakdownRow with a formatted CountText.</summary>
public record BreakdownRowDisplay(string Label, int Count, double Percentage)
{
    public string CountText => $"{Count:N0} ({Percentage:F1}%)";
}

/// <summary>One bar of the books-added-per-year chart. Rendered by BooksPerYearChartBehavior.</summary>
public record BooksPerYearPoint(int Year, int Count);

public sealed partial class StatisticsWindowViewModel : ObservableObject
{
    private readonly IStatisticsService _statisticsService;
    private readonly IConnectionHealthMonitor _connectionMonitor;
    private readonly IConnectionFailureClassifier _connectionClassifier;

    /// <summary>Set by WindowService to close the window.</summary>
    public Action? CloseWindow { get; set; }

    [ObservableProperty]
    private int _totalBooks;

    /// <summary>Books-added-per-year data for the bar chart; the chart control renders it via a behavior.</summary>
    [ObservableProperty]
    private IReadOnlyList<BooksPerYearPoint> _booksPerYear = [];

    public ObservableCollection<BreakdownRowDisplay> FormatBreakdown { get; } = [];
    public ObservableCollection<BreakdownRowDisplay> CollectionBreakdown { get; } = [];
    public ObservableCollection<BreakdownRowDisplay> LanguageBreakdown { get; } = [];
    public ObservableCollection<BreakdownRowDisplay> PublishedYearBreakdown { get; } = [];

    public StatisticsWindowViewModel(
        IStatisticsService statisticsService,
        IConnectionHealthMonitor connectionMonitor,
        IConnectionFailureClassifier connectionClassifier)
    {
        _statisticsService = statisticsService;
        _connectionMonitor = connectionMonitor;
        _connectionClassifier = connectionClassifier;
    }

    [RelayCommand]
    public Task RefreshAsync() => TryRefreshAsync();

    /// <summary>
    /// Loads the statistics; returns false when the load failed because the remote connection is down (reported
    /// to the status indicator) so the caller can avoid opening a blank statistics window.
    /// </summary>
    public async Task<bool> TryRefreshAsync()
    {
        try
        {
            TotalBooks = await _statisticsService.GetTotalBookCountAsync();
            var yearData = await _statisticsService.GetBooksPerYearAsync();
            BooksPerYear = yearData.Select(d => new BooksPerYearPoint(d.Year, d.Count)).ToList();
            ReplaceCollection(FormatBreakdown, await _statisticsService.GetBreakdownByFormatAsync());
            ReplaceCollection(CollectionBreakdown, await _statisticsService.GetBreakdownByCollectionAsync());
            ReplaceCollection(LanguageBreakdown, await _statisticsService.GetBreakdownByLanguageAsync());
            ReplaceCollection(PublishedYearBreakdown, await _statisticsService.GetBreakdownByPublishedYearAsync());
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "StatisticsWindowViewModel: RefreshAsync failed");
            // A dropped remote connection drives the shared status-bar indicator; the monitor retries in the
            // background. Other errors just leave the figures as they were.
            _connectionMonitor.ReportIfConnectionLoss(_connectionClassifier, ex);
            return false;
        }
    }

    private static void ReplaceCollection(
        ObservableCollection<BreakdownRowDisplay> target,
        IReadOnlyList<BreakdownRow> source)
    {
        target.Clear();
        foreach (var row in source)
            target.Add(new BreakdownRowDisplay(row.Label, row.Count, row.Percentage));
    }

    [RelayCommand]
    private void Close() => CloseWindow?.Invoke();
}
