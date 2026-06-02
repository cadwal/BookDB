using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;
using BookDB.Desktop.ViewModels;

namespace BookDB.Desktop.Behaviors;

/// <summary>
/// Attaches to the TabControl in BookEditForm. When the user selects the Contributors tab
/// (index 3), calls OnContributorsTabActivating() on BookEditViewModelBase BEFORE Avalonia
/// renders the tab content. This guarantees _suppressContributorDirty is true when the
/// AutoCompleteBox TwoWay bindings fire their PropertyChanged events during lazy tab render.
///
/// This replaces the failed AutoCompleteBoxInitSuppressDirtyBehavior, which hooked Initialized
/// too late (after binding noise had already set HasUnsavedChanges = true).
/// </summary>
public class ContributorsTabActivationBehavior : Behavior<TabControl>
{
    // The Contributors tab is at index 3 in BookEditForm.axaml:
    // 0=Basic Info, 1=Details, 2=Acquisition, 3=Contributors & Admin, 4=Additional Info, 5=Images
    private const int ContributorsTabIndex = 3;

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is null) return;
        AssociatedObject.SelectionChanged += OnSelectionChanged;
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
            AssociatedObject.SelectionChanged -= OnSelectionChanged;
        base.OnDetaching();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (AssociatedObject?.SelectedIndex != ContributorsTabIndex) return;
        if (AssociatedObject.DataContext is not BookEditViewModelBase vm) return;
        vm.OnContributorsTabActivating();
    }
}
