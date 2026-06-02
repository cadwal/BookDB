using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;
using BookDB.Desktop.ViewModels;

namespace BookDB.Desktop.Behaviors;

public class LoanHistoryTabActivationBehavior : Behavior<TabControl>
{
    private const int LoanHistoryTabIndex = 6;

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
        if (AssociatedObject?.SelectedIndex != LoanHistoryTabIndex) return;
        if (AssociatedObject.DataContext is not BookEditViewModelBase vm) return;
        _ = vm.OnLoanHistoryTabActivatingAsync();
    }
}
