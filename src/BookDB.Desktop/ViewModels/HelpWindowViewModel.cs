using System;
using System.Globalization;
using System.Threading.Tasks;
using BookDB.Help;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BookDB.Desktop.ViewModels;

public partial class HelpWindowViewModel : ObservableObject
{
    /// <summary>Set by WindowService to close the window.</summary>
    public Action? CloseWindow { get; set; }

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _shortcutsContent = string.Empty;

    [ObservableProperty]
    private string _glossaryContent = string.Empty;

    [ObservableProperty]
    private string _importGuideContent = string.Empty;

    [ObservableProperty]
    private string _dataSourcesContent = string.Empty;

    [ObservableProperty]
    private string _remoteDatabasesContent = string.Empty;

    public async Task InitializeAsync(HelpTab initialTab)
    {
        SelectedTabIndex = (int)initialTab;
        try
        {
            var culture = CultureInfo.CurrentUICulture;
            ShortcutsContent   = await HelpContentLoader.LoadAsync("shortcuts", culture);
            GlossaryContent    = await HelpContentLoader.LoadAsync("glossary", culture);
            ImportGuideContent = await HelpContentLoader.LoadAsync("import-guide", culture);
            DataSourcesContent = await HelpContentLoader.LoadAsync("data-sources", culture);
            RemoteDatabasesContent = await HelpContentLoader.LoadAsync("remote-databases", culture);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "HelpWindowViewModel: InitializeAsync failed");
        }
    }

    [RelayCommand]
    private void Close() => CloseWindow?.Invoke();
}
