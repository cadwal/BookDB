using System.Collections.Generic;
using System.Linq;

namespace BookDB.Desktop.Behaviors;

/// <summary>Vertical bars with category ticks along the bottom axis (e.g. books added per publication year).</summary>
public sealed class VerticalBarChartBehavior : ChartBehaviorBase
{
    protected override void Draw(ScottPlot.Plot plot, IReadOnlyList<ChartDatum> data, ChartColors colors)
    {
        // Bars sit at index positions 0..n so the category ticks line up even when the labels aren't consecutive.
        plot.Add.Bars(data
            .Select((d, i) => new ScottPlot.Bar { Position = i, Value = d.Value, FillColor = colors.Bar })
            .ToArray());
        plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
            data.Select((_, i) => (double)i).ToArray(),
            data.Select(d => d.Label).ToArray());
    }
}
