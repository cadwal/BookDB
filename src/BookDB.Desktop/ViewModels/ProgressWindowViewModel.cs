using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BookDB.Desktop.ViewModels;

public sealed partial class ProgressWindowViewModel : ObservableObject
{
    public string Header { get; }

    // Card = the chromeless shutdown-backup variant: no OS chrome, accent border, indeterminate bar.
    public bool IsCard { get; }

    public SystemDecorations Decorations => IsCard ? SystemDecorations.None : SystemDecorations.Full;

    [ObservableProperty]
    private string _status = Localization.Resources.AppDialog_Progress_PleaseWait;

    public ProgressWindowViewModel(string header, bool isCard = false)
    {
        Header = header;
        IsCard = isCard;
    }
}
