using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Desktop.Helpers;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;

namespace BookDB.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly IWindowService? _windowService;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel, IWindowService windowService) : this()
    {
        DataContext = viewModel;
        _windowService = windowService;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MainWindowViewModel vm
            && !double.IsNaN(vm.WindowLeft) && !double.IsNaN(vm.WindowTop))
        {
            var candidate = new Avalonia.PixelPoint((int)vm.WindowLeft, (int)vm.WindowTop);
            bool onScreen = Screens.All.Any(s => s.Bounds.Contains(candidate));
            if (onScreen)
                Position = candidate;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is MainWindowViewModel vm)
        {
            vm.WindowLeft = Position.X;
            vm.WindowTop = Position.Y;

            // Close secondary windows synchronously before the main window closes.
            // AppHost.ShutdownAsync also does this, but its async handler fires too late
            // to reliably prevent secondary windows from outliving the main window.
            _windowService?.CloseAllSecondaryWindows();

            if (vm.IsBatchQueueRunning && !_shutdownConfirmed)
            {
                e.Cancel = true;
                _ = AwaitConfirmShutdownAsync(vm);
            }
        }
    }

    private bool _shutdownConfirmed;

    private async Task AwaitConfirmShutdownAsync(MainWindowViewModel vm)
    {
        var canClose = await vm.ConfirmShutdownAsync();
        if (canClose)
        {
            _shutdownConfirmed = true;
            Close();
        }
    }
}
