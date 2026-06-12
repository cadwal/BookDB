using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Logic.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
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

    /// <summary>Set by WindowService to close the window.</summary>
    public Action? CloseWindow { get; set; }

    // NOT [ObservableProperty] — OxyPlot listens to InvalidatePlot, not PropertyChanged.
    // Replacing the model causes flicker; update in-place instead.
    public PlotModel ChartModel { get; } = new PlotModel
    {
        Title = Localization.Resources.Statistics_Section_BooksPerYear,
        Background = ToOxy(Helpers.Palette.Color("BrushBackground", Avalonia.Media.Colors.White)),
        TextColor = ToOxy(Helpers.Palette.Color("BrushTextPrimary", Avalonia.Media.Colors.Black)),
        TitleColor = ToOxy(Helpers.Palette.Color("BrushTextPrimary", Avalonia.Media.Colors.Black)),
        PlotAreaBorderColor = ToOxy(Helpers.Palette.Color("BrushBorder", Avalonia.Media.Colors.Gray)),
    };

    private static OxyColor ToOxy(Avalonia.Media.Color c) => OxyColor.FromArgb(c.A, c.R, c.G, c.B);

    [ObservableProperty]
    private int _totalBooks;

    public ObservableCollection<BreakdownRowDisplay> FormatBreakdown { get; } = [];
    public ObservableCollection<BreakdownRowDisplay> CollectionBreakdown { get; } = [];
    public ObservableCollection<BreakdownRowDisplay> LanguageBreakdown { get; } = [];
    public ObservableCollection<BreakdownRowDisplay> PublishedYearBreakdown { get; } = [];

    public StatisticsWindowViewModel(IStatisticsService statisticsService)
    {
        _statisticsService = statisticsService;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        try
        {
            TotalBooks = await _statisticsService.GetTotalBookCountAsync();
            var yearData = await _statisticsService.GetBooksPerYearAsync();
            BuildChart(yearData);
            ReplaceCollection(FormatBreakdown, await _statisticsService.GetBreakdownByFormatAsync());
            ReplaceCollection(CollectionBreakdown, await _statisticsService.GetBreakdownByCollectionAsync());
            ReplaceCollection(LanguageBreakdown, await _statisticsService.GetBreakdownByLanguageAsync());
            ReplaceCollection(PublishedYearBreakdown, await _statisticsService.GetBreakdownByPublishedYearAsync());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "StatisticsWindowViewModel: RefreshAsync failed");
        }
    }

    private void BuildChart(IReadOnlyList<(int Year, int Count)> data)
    {
        ChartModel.Series.Clear();
        ChartModel.Axes.Clear();

        var axisText = ToOxy(Helpers.Palette.Color("BrushTextSecondary", Avalonia.Media.Colors.Black));
        var axisLine = ToOxy(Helpers.Palette.Color("BrushBorder", Avalonia.Media.Colors.Gray));

        // OxyPlot 2.x BarSeries renders horizontal bars; CategoryAxis must be on Left (Y axis).
        // CategoryAxis.Labels is a List<string> (read-only property — use .Add(), not assignment).
        var catAxis = new CategoryAxis
        {
            Position = AxisPosition.Left,
            TextColor = axisText, TitleColor = axisText, AxislineColor = axisLine, TicklineColor = axisLine,
        };
        foreach (var (year, _) in data)
            catAxis.Labels.Add(year.ToString());

        var series = new BarSeries { FillColor = ToOxy(Helpers.Palette.Color("BrushChartBar", Avalonia.Media.Color.Parse("#4682b4"))) };
        foreach (var (_, count) in data)
            series.Items.Add(new BarItem(count));

        ChartModel.Axes.Add(catAxis);
        ChartModel.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            MinimumPadding = 0,
            TextColor = axisText, TitleColor = axisText, AxislineColor = axisLine, TicklineColor = axisLine,
        });

        ChartModel.Series.Add(series);
        ChartModel.InvalidatePlot(true); // REQUIRED — forces Avalonia re-render
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
