using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Desktop.ViewModels;

public enum WindowCategory { BookEdit, Utility }

public partial class OpenWindowEntry : ObservableObject
{
    [ObservableProperty]
    private string _title;

    public ICommand ActivateCommand { get; }
    public WindowCategory Category { get; }
    public bool IsSeparator { get; }
    public string SeparatorTag => IsSeparator ? "separator" : string.Empty;

    /// <summary>Normal window entry constructor.</summary>
    public OpenWindowEntry(string title, ICommand activateCommand,
        WindowCategory category = WindowCategory.BookEdit)
    {
        _title = title;
        ActivateCommand = activateCommand;
        Category = category;
        IsSeparator = false;
    }

    /// <summary>Sentinel separator entry — use CreateSeparator().</summary>
    private OpenWindowEntry()
    {
        _title = string.Empty;
        ActivateCommand = new RelayCommand(() => { }, () => false);
        IsSeparator = true;
    }

    public static OpenWindowEntry CreateSeparator() => new();
}
