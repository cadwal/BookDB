using System;
using System.Collections.Generic;
using System.Linq;

namespace BookDB.Desktop.Behaviors;

/// <summary>
/// Horizontal bars with category ticks down the left axis and each datum's pre-formatted value label at the
/// bar's tip (e.g. formats, languages, top authors). The first datum renders at the top, so a largest-first
/// list reads top-to-bottom.
/// </summary>
public sealed class HorizontalBarChartBehavior : ChartBehaviorBase
{
    protected override void Draw(ScottPlot.Plot plot, IReadOnlyList<ChartDatum> data, ChartColors colors)
    {
        if (data.Count == 0)
            return;

        var count = data.Count;
        // Position n-1 is the top of the left axis, so reversing the index puts the first datum on top.
        var bars = data
            .Select((d, i) => new ScottPlot.Bar
            {
                Position = count - 1 - i,
                Value = d.Value,
                FillColor = colors.Bar,
                Orientation = ScottPlot.Orientation.Horizontal,
                Label = d.ValueLabel ?? string.Empty,
            })
            .ToArray();

        var barPlot = plot.Add.Bars(bars);
        barPlot.ValueLabelStyle.ForeColor = colors.Axis;

        plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
            data.Select((_, i) => (double)(count - 1 - i)).ToArray(),
            data.Select(d => d.Label).ToArray());

        // The value labels are drawn past each bar's tip; without headroom the longest bar's label is clipped at
        // the plot edge. Extend the value axis past the max bar, scaled to the longest label so it always fits.
        var max = data.Max(d => d.Value);
        if (max > 0)
        {
            var longestLabel = data.Max(d => (d.ValueLabel ?? string.Empty).Length);
            var headroom = Math.Clamp(longestLabel * 0.05, 0.2, 0.7);
            plot.Axes.SetLimitsX(0, max * (1 + headroom));
        }
    }
}
