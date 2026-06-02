using Avalonia.Controls;
using BookDB.Desktop.ViewModels;

namespace BookDB.Desktop.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    public SplashWindow(SplashViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
