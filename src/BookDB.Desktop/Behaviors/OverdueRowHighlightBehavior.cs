using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;
using BookDB.Desktop.ViewModels;

namespace BookDB.Desktop.Behaviors;

public class OverdueRowHighlightBehavior : Behavior<DataGrid>
{
    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is null) return;
        AssociatedObject.LoadingRow += OnLoadingRow;
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
            AssociatedObject.LoadingRow -= OnLoadingRow;
        base.OnDetaching();
    }

    private void OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is BookRowViewModel vm)
        {
            e.Row.Classes.Set("overdue", vm.IsOverdue);
        }
    }
}
