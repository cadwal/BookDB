using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using BookDB.Mobile.ViewModels;
using BookDB.Mobile.Views;
using Xunit;

namespace BookDB.Mobile.UITests;

/// <summary>
/// Guards the smoke layer against silent gaps: a view added without a registered screen, and a page view model
/// whose view the locator cannot find.
/// </summary>
public class ScreenCoverageTests : HeadlessTest
{
    [Fact]
    public Task EveryViewIsReachedBySomeRegisteredScreen() => RunUi(async () =>
    {
        var reached = new HashSet<Type>();
        foreach (var screen in ScreenRegistry.All)
        {
            var content = await screen.BuildAsync();
            var window = Phone.Show(content);

            foreach (var visual in window.GetVisualDescendants().Prepend(window))
            {
                if (IsViewType(visual.GetType()))
                {
                    reached.Add(visual.GetType());
                }
            }

            window.Close();
        }

        var missing = ViewTypes()
            .Where(type => !reached.Contains(type))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "View types not reached by any registered screen:\n  " + string.Join("\n  ", missing));
    });

    /// <summary>
    /// The locator matches a view to a page by name and answers with a text block when it finds none — a
    /// screen that reads "View not found" rather than a crash. Every page the shell can make current is
    /// checked here so that answer never reaches a phone.
    /// </summary>
    [Fact]
    public Task EveryPageViewModelResolvesToItsView() => RunUi(() =>
    {
        var unresolved = PageViewModelTypes()
            .Where(type => Type.GetType(ExpectedViewName(type)) is null)
            .Select(type => $"{type.Name} → {ExpectedViewName(type)}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(unresolved.Count == 0,
            "Page view models the locator cannot find a view for:\n  " + string.Join("\n  ", unresolved));

        // And the convention checked above is the one the locator actually applies.
        var built = new ViewLocator().Build(new HubViewModel(new BookDB.Mobile.Tests.FakeNavigator(), ScreenRegistry.Batch()));
        Assert.IsType<HubView>(built);

        return Task.CompletedTask;
    });

    private static string ExpectedViewName(Type viewModel) =>
        viewModel.FullName!
            .Replace("ViewModels", "Views", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal)
        + ", " + viewModel.Assembly.GetName().Name;

    private static IEnumerable<Type> PageViewModelTypes() =>
        typeof(PageViewModel).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(PageViewModel).IsAssignableFrom(type));

    private static IEnumerable<Type> ViewTypes() => typeof(MainView).Assembly.GetTypes().Where(IsViewType);

    private static bool IsViewType(Type type) =>
        !type.IsAbstract
        && type.Namespace is not null
        && type.Namespace.StartsWith("BookDB.Mobile.Views", StringComparison.Ordinal)
        && typeof(UserControl).IsAssignableFrom(type);
}
