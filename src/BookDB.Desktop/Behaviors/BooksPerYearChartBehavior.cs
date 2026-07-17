using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Xaml.Interactivity;
using BookDB.Desktop.Helpers;
using BookDB.Desktop.ViewModels;
using ScottPlot.Avalonia;

namespace BookDB.Desktop.Behaviors;

/// <summary>
/// Renders the books-added-per-year bar chart onto its <see cref="AvaPlot"/> from the bound
/// <see cref="Points"/>, themed from the palette. ScottPlot's plot is driven imperatively, so the binding
/// seam lives in a behavior rather than window code-behind (project code-behind rule).
/// </summary>
public class BooksPerYearChartBehavior : Behavior<AvaPlot>
{
    public static readonly StyledProperty<IReadOnlyList<BooksPerYearPoint>?> PointsProperty =
        AvaloniaProperty.Register<BooksPerYearChartBehavior, IReadOnlyList<BooksPerYearPoint>?>(nameof(Points));

    public IReadOnlyList<BooksPerYearPoint>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        Render();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PointsProperty)
            Render();
    }

    private void Render()
    {
        if (AssociatedObject is not { } control)
            return;

        var background = ToScott(Palette.Color("BrushBackground", Avalonia.Media.Colors.White));
        var axis = ToScott(Palette.Color("BrushTextSecondary", Avalonia.Media.Colors.Black));
        var barColor = ToScott(Palette.Color("BrushChartBar", Avalonia.Media.Color.Parse("#4682b4")));

        var plot = control.Plot;
        plot.Clear();
        plot.FigureBackground.Color = background;
        plot.DataBackground.Color = background;
        plot.Axes.Color(axis);

        var points = Points ?? [];
        // Bars sit at index positions 0..n so the year ticks line up even when years aren't consecutive.
        plot.Add.Bars(points
            .Select((p, i) => new ScottPlot.Bar { Position = i, Value = p.Count, FillColor = barColor })
            .ToArray());
        plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
            points.Select((_, i) => (double)i).ToArray(),
            points.Select(p => p.Year.ToString()).ToArray());

        control.Refresh();
    }

    private static ScottPlot.Color ToScott(Avalonia.Media.Color c) => new(c.R, c.G, c.B, c.A);
}
