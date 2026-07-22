using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Xaml.Interactivity;
using BookDB.Desktop.Helpers;
using BookDB.Desktop.Messages;
using CommunityToolkit.Mvvm.Messaging;
using ScottPlot.Avalonia;

namespace BookDB.Desktop.Behaviors;

/// <summary>One category of a chart: a label, its numeric value, and an optional pre-formatted value label.</summary>
public record ChartDatum(string Label, double Value, string? ValueLabel = null);

/// <summary>
/// Shared plumbing for the ScottPlot chart behaviors: binds a <see cref="Points"/> list, paints the plot's
/// theme surfaces from the palette, and re-renders both when the data changes and on a live flavour switch.
/// ScottPlot's plot is driven imperatively, so this binding seam lives in a behavior rather than window
/// code-behind (project code-behind rule); the palette colours are baked into the plot, so a DynamicResource
/// binding can't reach them — the theme-applied message forces the re-render instead.
/// </summary>
public abstract class ChartBehaviorBase : Behavior<AvaPlot>
{
    public static readonly StyledProperty<IReadOnlyList<ChartDatum>?> PointsProperty =
        AvaloniaProperty.Register<ChartBehaviorBase, IReadOnlyList<ChartDatum>?>(nameof(Points));

    public IReadOnlyList<ChartDatum>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        WeakReferenceMessenger.Default.Register<ThemeAppliedMessage>(this, (_, _) => Render());
        Render();
    }

    protected override void OnDetaching()
    {
        WeakReferenceMessenger.Default.Unregister<ThemeAppliedMessage>(this);
        base.OnDetaching();
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

        var colors = new ChartColors(
            ToScott(Palette.Color("BrushBackground", Colors.White)),
            ToScott(Palette.Color("BrushTextSecondary", Colors.Black)),
            ToScott(Palette.Color("BrushChartBar", Color.Parse("#4682b4"))));

        var plot = control.Plot;
        plot.Clear();
        plot.FigureBackground.Color = colors.Background;
        plot.DataBackground.Color = colors.Background;
        plot.Axes.Color(colors.Axis);

        Draw(plot, Points ?? [], colors);

        control.Refresh();
    }

    /// <summary>Draws the chart-type-specific series onto the already-themed, cleared plot.</summary>
    protected abstract void Draw(ScottPlot.Plot plot, IReadOnlyList<ChartDatum> data, ChartColors colors);

    protected static ScottPlot.Color ToScott(Color c) => new(c.R, c.G, c.B, c.A);

    protected readonly record struct ChartColors(
        ScottPlot.Color Background, ScottPlot.Color Axis, ScottPlot.Color Bar);
}
