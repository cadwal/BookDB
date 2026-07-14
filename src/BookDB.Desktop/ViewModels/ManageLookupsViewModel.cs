using System;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Messages;
using BookDB.Desktop.Services;
using BookDB.Logic.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;

namespace BookDB.Desktop.ViewModels;

public sealed partial class ManageLookupsViewModel : ObservableObject
{
    private readonly ILookupManagementService _service;
    private readonly ILookupService _lookupService;
    private readonly IWindowService _windowService;
    private readonly IMessenger _messenger;

    public Action? CloseWindow { get; set; }

    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>Whether any tab has its inline entry editor (or, for Person, its merge/cleanup panel) open —
    /// the gate for Esc-closes-the-window. Deliberately global: an edit left pending on a tab the user
    /// has since switched away from still blocks Esc, so it can't be lost by an accidental keypress.</summary>
    public bool IsAnyTabEditing =>
        PersonTab.HasSelection || PersonTab.IsMergePanelOpen || PersonTab.IsCleanupPanelOpen
        || PublisherTab.HasSelection
        || SeriesTab.HasSelection
        || LocationTab.HasSelection
        || OwnerTab.HasSelection
        || LanguageTab.HasSelection
        || CategoryTab.HasSelection
        || PurchasePlaceTab.HasSelection
        || CollectionTab.HasSelection;

    // Sub-VMs — constructed here, NOT registered in DI.
    public PersonTabViewModel PersonTab { get; }
    public PublisherTabViewModel PublisherTab { get; }
    public SeriesTabViewModel SeriesTab { get; }
    public LocationTabViewModel LocationTab { get; }
    public OwnerTabViewModel OwnerTab { get; }
    public LanguageTabViewModel LanguageTab { get; }
    public CategoryTabViewModel CategoryTab { get; }
    public PurchasePlaceTabViewModel PurchasePlaceTab { get; }
    public CollectionTabViewModel CollectionTab { get; }

    public ManageLookupsViewModel(
        ILookupManagementService service,
        ILookupService lookupService,
        IWindowService windowService,
        IMessenger messenger,
        ISettingsService settingsService,
        IConnectionHealthMonitor connectionMonitor,
        IConnectionFailureClassifier connectionClassifier)
    {
        _service = service;
        _lookupService = lookupService;
        _windowService = windowService;
        _messenger = messenger;

        PersonTab    = new PersonTabViewModel(service, lookupService, windowService, messenger);
        PublisherTab = new PublisherTabViewModel(service, lookupService, windowService, messenger);
        SeriesTab    = new SeriesTabViewModel(service, lookupService, windowService, messenger);
        LocationTab  = new LocationTabViewModel(service, lookupService, windowService, messenger);
        OwnerTab     = new OwnerTabViewModel(service, lookupService, windowService, messenger);
        LanguageTab      = new LanguageTabViewModel(service, lookupService, windowService, messenger);
        CategoryTab      = new CategoryTabViewModel(service, lookupService, windowService, messenger);
        PurchasePlaceTab = new PurchasePlaceTabViewModel(service, lookupService, windowService, messenger);
        CollectionTab    = new CollectionTabViewModel(service, lookupService, windowService, messenger, settingsService);

        // Give every lookup tab the connection monitor/classifier so a write that fails on a dropped remote
        // connection lights the shared status-bar indicator instead of a generic "save failed" message.
        LookupTabViewModel[] tabs =
            [PublisherTab, SeriesTab, LocationTab, OwnerTab, LanguageTab, CategoryTab, PurchasePlaceTab, CollectionTab];
        foreach (var tab in tabs)
        {
            tab.ConnectionMonitor = connectionMonitor;
            tab.ConnectionClassifier = connectionClassifier;
            tab.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IsAnyTabEditing));
        }
        PersonTab.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IsAnyTabEditing));
    }

    public async Task InitializeAsync(string? initialTab = null)
    {
        await SafeLoadAsync(PersonTab.LoadAsync,        "PersonTab");
        await SafeLoadAsync(PublisherTab.LoadAsync,     "PublisherTab");
        await SafeLoadAsync(SeriesTab.LoadAsync,        "SeriesTab");
        await SafeLoadAsync(LocationTab.LoadAsync,      "LocationTab");
        await SafeLoadAsync(OwnerTab.LoadAsync,         "OwnerTab");
        await SafeLoadAsync(LanguageTab.LoadAsync,      "LanguageTab");
        await SafeLoadAsync(CategoryTab.LoadAsync,      "CategoryTab");
        await SafeLoadAsync(PurchasePlaceTab.LoadAsync, "PurchasePlaceTab");
        await SafeLoadAsync(CollectionTab.LoadAsync,    "CollectionTab");
        SelectedTabIndex = initialTab switch
        {
            "Person"        => 0,
            "Publisher"     => 1,
            "Series"        => 2,
            "Location"      => 3,
            "Owner"         => 4,
            "Language"      => 5,
            "Category"      => 6,
            "PurchasePlace" => 7,
            "Collection"    => 8,
            _               => 0
        };
    }

    private static async Task SafeLoadAsync(Func<Task> loadFn, string name)
    {
        try { await loadFn(); }
        catch (Exception ex) { Log.Error(ex, "ManageLookupsViewModel: {Tab} LoadAsync failed", name); }
    }

    [RelayCommand]
    private void Close()
    {
        CloseWindow?.Invoke();
    }
}
