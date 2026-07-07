using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using BookDB.Desktop.Views;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Guards smoke coverage: every concrete window/pane under <c>BookDB.Desktop.Views</c> must be reached by some
/// registered surface — either built directly or rendered in-situ inside one. A newly added view that no surface
/// exercises fails this test, so coverage can't silently regress.
/// </summary>
public class SurfaceCoverageTests : HeadlessTest
{
    [Fact]
    public Task EveryViewIsReachedBySomeRegisteredSurface() => RunUi(async () =>
    {
        var reached = new HashSet<Type>();
        foreach (var surface in SurfaceRegistry.All)
        {
            using var host = TestHost.Create();
            var content = await surface.BuildAsync(host);
            var window = content as Window ?? new Window { Content = content, Width = 1000, Height = 700 };
            window.Show();
            Ui.Pump();

            foreach (var visual in window.GetVisualDescendants().Prepend(window))
                if (IsViewType(visual.GetType()))
                    reached.Add(visual.GetType());

            window.Close();
        }

        var missing = ViewTypes()
            .Where(t => !reached.Contains(t))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "View types not reached by any registered surface:\n  " + string.Join("\n  ", missing));
    });

    private static IEnumerable<Type> ViewTypes() => typeof(MainWindow).Assembly.GetTypes().Where(IsViewType);

    private static bool IsViewType(Type t) =>
        !t.IsAbstract
        && t.Namespace is not null
        && t.Namespace.StartsWith("BookDB.Desktop.Views", StringComparison.Ordinal) // incl. sub-namespaces (ImportStepViews)
        && (typeof(Window).IsAssignableFrom(t) || typeof(UserControl).IsAssignableFrom(t));
}
