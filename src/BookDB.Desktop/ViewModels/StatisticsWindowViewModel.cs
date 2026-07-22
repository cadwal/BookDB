using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Globalization;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Behaviors;
using BookDB.Desktop.Helpers;
using BookDB.Desktop.Localization;
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

public sealed partial class StatisticsWindowViewModel : ObservableObject
{
    private readonly IStatisticsService _statisticsService;
    private readonly IConnectionHealthMonitor _connectionMonitor;
    private readonly IConnectionFailureClassifier _connectionClassifier;

    /// <summary>Set by WindowService to close the window.</summary>
    public Action? CloseWindow { get; set; }

    [ObservableProperty]
    private int _totalBooks;

    /// <summary>Books-added-per-year data for the vertical-bar chart; the chart control renders it via a behavior.</summary>
    [ObservableProperty]
    private IReadOnlyList<ChartDatum> _booksPerYear = [];

    /// <summary>Cumulative library-growth line; the chart control renders it via a behavior.</summary>
    [ObservableProperty]
    private IReadOnlyList<ChartDatum> _libraryGrowth = [];

    /// <summary>Horizontal-bar card series (top categories + an aggregated "Other"), each with a count (pct%) label.</summary>
    [ObservableProperty]
    private IReadOnlyList<ChartDatum> _formatChart = [];

    [ObservableProperty]
    private IReadOnlyList<ChartDatum> _languageChart = [];

    [ObservableProperty]
    private IReadOnlyList<ChartDatum> _collectionChart = [];

    [ObservableProperty]
    private IReadOnlyList<ChartDatum> _topAuthorsChart = [];

    [ObservableProperty]
    private IReadOnlyList<ChartDatum> _publishedYearChart = [];

    // Full, ordered breakdowns for the expander's tables; the cards render the capped chart projections above.
    // Every card has a matching table and vice versa.
    public ObservableCollection<BreakdownRowDisplay> FormatBreakdown { get; } = [];
    public ObservableCollection<BreakdownRowDisplay> CollectionBreakdown { get; } = [];
    public ObservableCollection<BreakdownRowDisplay> LanguageBreakdown { get; } = [];
    public ObservableCollection<BreakdownRowDisplay> TopAuthorsBreakdown { get; } = [];
    public ObservableCollection<BreakdownRowDisplay> PublishedYearBreakdown { get; } = [];

    /// <summary>Categories shown in a horizontal-bar card before the long tail folds into an "Other" bar.</summary>
    private const int MaxCategoryBars = 12;

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
            BooksPerYear = yearData
                .Select(d => new ChartDatum(d.Year.ToString(CultureInfo.CurrentCulture), d.Count))
                .ToList();

            var growth = await _statisticsService.GetLibraryGrowthAsync();
            LibraryGrowth = growth
                .Select(p => new ChartDatum($"{p.Year}-{p.Month:D2}", p.CumulativeCount))
                .ToList();

            FormatChart = CappedChart(ReplaceCollection(FormatBreakdown, await _statisticsService.GetBreakdownByFormatAsync()));
            CollectionChart = CappedChart(ReplaceCollection(CollectionBreakdown, await _statisticsService.GetBreakdownByCollectionAsync()));
            LanguageChart = CappedChart(ReplaceCollection(LanguageBreakdown, await _statisticsService.GetBreakdownByLanguageAsync()));
            PublishedYearChart = CappedChart(ReplaceCollection(PublishedYearBreakdown, await _statisticsService.GetBreakdownByPublishedYearAsync()));

            // The service already caps the authors, so there is no tail to fold into an "Other" bar here.
            var topAuthors = ReplaceCollection(TopAuthorsBreakdown, await _statisticsService.GetTopAuthorsAsync(MaxCategoryBars));
            TopAuthorsChart = topAuthors.Select(Bar).ToList();

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

    private static IReadOnlyList<BreakdownRowDisplay> ReplaceCollection(
        ObservableCollection<BreakdownRowDisplay> target,
        IReadOnlyList<BreakdownRow> source)
    {
        target.Clear();
        foreach (var row in source)
            target.Add(new BreakdownRowDisplay(
                row.Label ?? Resources.Statistics_UnknownCategory, row.Count, row.Percentage));
        return target.ToList();
    }

    /// <summary>Top categories as chart bars, folding everything past the cap into a single localized "Other" bar.</summary>
    private static IReadOnlyList<ChartDatum> CappedChart(IReadOnlyList<BreakdownRowDisplay> rows)
    {
        if (rows.Count <= MaxCategoryBars)
            return rows.Select(Bar).ToList();

        var chart = rows.Take(MaxCategoryBars - 1).Select(Bar).ToList();
        var tail = rows.Skip(MaxCategoryBars - 1).ToList();
        var count = tail.Sum(r => r.Count);
        var other = new BreakdownRowDisplay(
            Resources.Statistics_OtherCategory, count, Math.Round(tail.Sum(r => r.Percentage), 1));
        chart.Add(Bar(other));
        return chart;
    }

    private static ChartDatum Bar(BreakdownRowDisplay row) => new(row.Label, row.Count, row.CountText);

    [RelayCommand]
    private void Close() => CloseWindow?.Invoke();
}
