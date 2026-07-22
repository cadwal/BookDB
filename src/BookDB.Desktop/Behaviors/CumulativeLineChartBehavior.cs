using System;
using System.Collections.Generic;
using System.Linq;

namespace BookDB.Desktop.Behaviors;

/// <summary>A single line over ordered category points, for a cumulative series (e.g. library growth over time).</summary>
public sealed class CumulativeLineChartBehavior : ChartBehaviorBase
{
    // A monthly series spans years, so labelling every point overlaps into an unreadable smear — cap the axis
    // to a handful of evenly-spaced labels.
    private const int MaxAxisLabels = 12;

    protected override void Draw(ScottPlot.Plot plot, IReadOnlyList<ChartDatum> data, ChartColors colors)
    {
        if (data.Count == 0)
            return;

        var xs = data.Select((_, i) => (double)i).ToArray();
        var ys = data.Select(d => d.Value).ToArray();

        var line = plot.Add.Scatter(xs, ys);
        line.Color = colors.Bar;
        line.LineWidth = 2;
        line.MarkerSize = 4;

        var step = (int)Math.Ceiling(data.Count / (double)MaxAxisLabels);
        var positions = new List<double>();
        var labels = new List<string>();
        for (var i = 0; i < data.Count; i += step)
        {
            positions.Add(i);
            labels.Add(data[i].Label);
        }
        plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(positions.ToArray(), labels.ToArray());
    }
}
