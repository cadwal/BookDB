using System.Linq;
using System.Threading.Tasks;
using BookDB.Desktop.Behaviors;
using BookDB.Desktop.Theming;
using ScottPlot.Avalonia;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// The chart behavior family drives ScottPlot imperatively: each renders its series on attach, re-renders when the
/// bound points change, and — because the palette colours are baked into the plot where a DynamicResource can't
/// reach — re-renders on a live flavour switch. Tests restore the Default flavour; the headless app is shared.
/// </summary>
public class ChartBehaviorTests : HeadlessTest
{
    private static ChartDatum[] TwoBars => [new("2020", 2), new("2023", 5)];

    [Fact]
    public async Task VerticalBar_RendersBarsOnAttachAndFollowsPointChanges()
    {
        await RunUi(() =>
        {
            var plot = new AvaPlot();
            var behavior = new VerticalBarChartBehavior { Points = TwoBars };
            behavior.Attach(plot);

            var bars = plot.Plot.GetPlottables().OfType<ScottPlot.Plottables.BarPlot>().Single();
            Assert.Equal(2, bars.Bars.Count);

            behavior.Points = [new("2020", 1), new("2021", 2), new("2022", 3)];
            var afterChange = plot.Plot.GetPlottables().OfType<ScottPlot.Plottables.BarPlot>().Single();
            Assert.Equal(3, afterChange.Bars.Count);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task HorizontalBar_RendersHorizontalBarsWithValueLabels()
    {
        await RunUi(() =>
        {
            var plot = new AvaPlot();
            var behavior = new HorizontalBarChartBehavior
            {
                Points = [new("Hardcover", 512, "512 (60.0%)"), new("Paperback", 320, "320 (40.0%)")],
            };
            behavior.Attach(plot);

            var bars = plot.Plot.GetPlottables().OfType<ScottPlot.Plottables.BarPlot>().Single();
            Assert.Equal(2, bars.Bars.Count);
            Assert.All(bars.Bars, b => Assert.Equal(ScottPlot.Orientation.Horizontal, b.Orientation));
            Assert.Contains(bars.Bars, b => b.Label == "512 (60.0%)");

            // The value axis extends past the longest bar so its tip label isn't clipped at the plot edge.
            Assert.True(plot.Plot.Axes.GetLimits().Right > 512, "the value axis needs headroom for the tip label");
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task CumulativeLine_RendersAScatterLine()
    {
        await RunUi(() =>
        {
            var plot = new AvaPlot();
            var behavior = new CumulativeLineChartBehavior
            {
                Points = [new("Jan", 2), new("Feb", 5), new("Mar", 9)],
            };
            behavior.Attach(plot);

            Assert.Single(plot.Plot.GetPlottables().OfType<ScottPlot.Plottables.Scatter>());
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Chart_ReRendersFigureBackgroundOnFlavourSwitch()
    {
        await RunUi(() =>
        {
            var plot = new AvaPlot();
            var behavior = new VerticalBarChartBehavior { Points = TwoBars };
            behavior.Attach(plot);
            try
            {
                // Default flavour: BrushBackground is #ffffff.
                Assert.Equal(255, plot.Plot.FigureBackground.Color.R);

                // Dark flavour flips the variant, so BrushBackground resolves to #1e1e1e — the theme-applied
                // message must have driven a re-render for the baked-in figure colour to follow.
                ThemeApplier.Apply(ThemeFlavour.Dark);
                Assert.Equal(30, plot.Plot.FigureBackground.Color.R);
            }
            finally
            {
                ThemeApplier.Apply(ThemeFlavour.Default);
            }
            return Task.CompletedTask;
        });
    }
}
