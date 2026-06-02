using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Desktop.Messages;
using BookDB.Desktop.Localization;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;

namespace BookDB.Desktop.ViewModels;

// ============================================================
// Root ViewModel
// ============================================================

public sealed partial class SettingsWindowViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ILookupService _lookupService;
    private readonly IMessenger _messenger;

    public Action<bool?>? CloseDialog { get; set; }

    [ObservableProperty]
    private int _selectedTabIndex;

    // Sub-VMs — constructed here, NOT registered in DI (same as ManageLookupsViewModel)
    public SettingsGeneralTabViewModel GeneralTab { get; }
    public SettingsBrowseTabViewModel BrowseTab { get; }
    public SettingsLookupTabViewModel LookupTab { get; }
    public SettingsImportTabViewModel ImportTab { get; }
    public SettingsAdvancedTabViewModel AdvancedTab { get; }

    public SettingsWindowViewModel(
        ISettingsService settingsService,
        ILookupService lookupService,
        IFilePickerService filePickerService,
        IMessenger messenger)
    {
        _settingsService = settingsService;
        _lookupService   = lookupService;
        _messenger       = messenger;

        GeneralTab  = new SettingsGeneralTabViewModel(settingsService, lookupService);
        BrowseTab   = new SettingsBrowseTabViewModel(settingsService);
        LookupTab   = new SettingsLookupTabViewModel(settingsService);
        ImportTab   = new SettingsImportTabViewModel(settingsService, filePickerService);
        AdvancedTab = new SettingsAdvancedTabViewModel(settingsService, filePickerService);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            await GeneralTab.LoadAsync(ct);
            await BrowseTab.LoadAsync(ct);
            await LookupTab.LoadAsync(ct);
            await ImportTab.LoadAsync(ct);
            await AdvancedTab.LoadAsync(ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsWindowViewModel: InitializeAsync failed");
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            await GeneralTab.SaveAsync();
            await BrowseTab.SaveAsync();
            await LookupTab.SaveAsync();
            await ImportTab.SaveAsync();
            await AdvancedTab.SaveAsync();
            _messenger.Send(new SettingsSavedMessage());
            CloseDialog?.Invoke(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsWindowViewModel: SaveAsync failed");
        }
    }

    [RelayCommand]
    private void Close() => CloseDialog?.Invoke(false);
}

// ============================================================
// Helper records for Settings ComboBox items
// ============================================================

public sealed record CollectionItem(int Id, string Name);

public sealed record LanguageOption(string CultureName, string DisplayName);

public sealed record LogLevelOption(string Value, string DisplayName);

// ============================================================
// General Tab
// ============================================================

public sealed partial class SettingsGeneralTabViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ILookupService _lookupService;

    [ObservableProperty]
    private int? _defaultCollectionId;

    public ObservableCollection<CollectionItem> Collections { get; } = [];

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    public ObservableCollection<LanguageOption> AvailableLanguages { get; } = [];

    public SettingsGeneralTabViewModel(ISettingsService settingsService, ILookupService lookupService)
    {
        _settingsService = settingsService;
        _lookupService = lookupService;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            var idStr = await _settingsService.GetAsync("DefaultCollectionId", ct);
            DefaultCollectionId = int.TryParse(idStr, out var id) ? id : null;

            var collections = await _lookupService.GetCollectionsAsync(ct);
            Collections.Clear();
            foreach (var c in collections)
                Collections.Add(new CollectionItem(c.CollectionId, c.Name));

            // Populate available languages via satellite directory scan
            AvailableLanguages.Clear();
            AvailableLanguages.Add(new LanguageOption("en", "English"));
            var assemblyDir = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location) ?? "";
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(assemblyDir))
                {
                    var satellitePath = Path.Combine(dir, "BookDB.Desktop.resources.dll");
                    if (File.Exists(satellitePath))
                    {
                        var code = Path.GetFileName(dir);
                        var nativeName = CultureInfo.GetCultureInfo(code).NativeName;
                        var displayName = string.IsNullOrEmpty(nativeName)
                            ? code
                            : char.ToUpperInvariant(nativeName[0]) + nativeName[1..];
                        AvailableLanguages.Add(new LanguageOption(code, displayName));
                    }
                }
            }
            catch { /* read-only directory or scan error — show only "en" */ }

            var storedLanguage = await _settingsService.GetAsync("Language", ct);
            SelectedLanguage = AvailableLanguages.FirstOrDefault(l =>
                l.CultureName == (storedLanguage ?? "en"))
                ?? AvailableLanguages.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsGeneralTabViewModel: LoadAsync failed");
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            await _settingsService.SetAsync(
                "DefaultCollectionId",
                DefaultCollectionId.HasValue ? DefaultCollectionId.Value.ToString() : null,
                ct);

            // Persist language selection
            if (SelectedLanguage is not null)
                await _settingsService.SetAsync("Language", SelectedLanguage.CultureName, ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsGeneralTabViewModel: SaveAsync failed");
        }
    }
}

// ============================================================
// Lookup Tab
// ============================================================

public sealed partial class SettingsLookupTabViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private bool _librisKbEnabled = true;

    [ObservableProperty]
    private bool _googleBooksEnabled = true;

    [ObservableProperty]
    private bool _openLibraryEnabled = true;

    [ObservableProperty]
    private bool _isbnSearchOrgEnabled = true;

    public SettingsLookupTabViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            LibrisKbEnabled      = ParseBool(await _settingsService.GetAsync("LookupEnabled.LibrisKB",      ct), defaultValue: true);
            GoogleBooksEnabled   = ParseBool(await _settingsService.GetAsync("LookupEnabled.GoogleBooks",  ct), defaultValue: true);
            OpenLibraryEnabled   = ParseBool(await _settingsService.GetAsync("LookupEnabled.OpenLibrary",  ct), defaultValue: true);
            IsbnSearchOrgEnabled = ParseBool(await _settingsService.GetAsync("LookupEnabled.IsbnSearchOrg", ct), defaultValue: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsLookupTabViewModel: LoadAsync failed");
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            await _settingsService.SetAsync("LookupEnabled.LibrisKB",      LibrisKbEnabled.ToString().ToLowerInvariant(),      ct);
            await _settingsService.SetAsync("LookupEnabled.GoogleBooks",    GoogleBooksEnabled.ToString().ToLowerInvariant(),   ct);
            await _settingsService.SetAsync("LookupEnabled.OpenLibrary",    OpenLibraryEnabled.ToString().ToLowerInvariant(),   ct);
            await _settingsService.SetAsync("LookupEnabled.IsbnSearchOrg",  IsbnSearchOrgEnabled.ToString().ToLowerInvariant(), ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsLookupTabViewModel: SaveAsync failed");
        }
    }

    private static bool ParseBool(string? value, bool defaultValue)
        => value is null ? defaultValue : bool.TryParse(value, out var result) ? result : defaultValue;
}

// ============================================================
// Import Tab
// ============================================================

public sealed partial class SettingsImportTabViewModel : ObservableObject
{
    /// <summary>
    /// Default install location of Readerware, which ships the HSQLDB + Java runtime used to read a live
    /// Readerware database. On Windows this is auto-detected from the registry; the user can override via
    /// Browse. See <see cref="Helpers.ReaderwareInstallLocator"/>.
    /// </summary>
    public static string DefaultReaderwareToolPath => Helpers.ReaderwareInstallLocator.DefaultToolPath;

    private readonly ISettingsService _settingsService;
    private readonly IFilePickerService _filePickerService;

    [ObservableProperty]
    private string _overwritePolicy = "Skip";

    public IReadOnlyList<string> OverwritePolicies { get; } = ["Skip", "Overwrite", "Ask"];

    [ObservableProperty]
    private string _readerwareToolPath = DefaultReaderwareToolPath;

    public SettingsImportTabViewModel(ISettingsService settingsService, IFilePickerService filePickerService)
    {
        _settingsService = settingsService;
        _filePickerService = filePickerService;
    }

    [RelayCommand]
    private async Task BrowseReaderwareToolPathAsync()
    {
        var folder = await _filePickerService.PickFolderAsync(Localization.Resources.FilePicker_ChooseReaderwareToolFolder);
        if (!string.IsNullOrEmpty(folder))
            ReaderwareToolPath = folder;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            OverwritePolicy = await _settingsService.GetAsync("Import.OverwritePolicy", ct) ?? "Skip";
            ReaderwareToolPath = await _settingsService.GetAsync("Import.ReaderwareToolPath", ct)
                ?? DefaultReaderwareToolPath;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsImportTabViewModel: LoadAsync failed");
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            await _settingsService.SetAsync("Import.OverwritePolicy", OverwritePolicy, ct);
            await _settingsService.SetAsync("Import.ReaderwareToolPath", ReaderwareToolPath, ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsImportTabViewModel: SaveAsync failed");
        }
    }
}

// ============================================================
// Browse Tab
// ============================================================

public sealed partial class SettingsBrowseTabViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private bool _isSortNameSelected = true;

    [ObservableProperty]
    private bool _isDisplayNameSelected;

    public SettingsBrowseTabViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            var label = await _settingsService.GetAsync("AuthorFacetLabel", ct) ?? "SortName";
            IsSortNameSelected    = label != "DisplayName";
            IsDisplayNameSelected = label == "DisplayName";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsBrowseTabViewModel: LoadAsync failed");
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            await _settingsService.SetAsync(
                "AuthorFacetLabel",
                IsDisplayNameSelected ? "DisplayName" : "SortName",
                ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsBrowseTabViewModel: SaveAsync failed");
        }
    }
}

// ============================================================
// Advanced Tab
// ============================================================

public sealed partial class SettingsAdvancedTabViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IFilePickerService _filePickerService;

    [ObservableProperty]
    private bool _autoBackupEnabled;

    [ObservableProperty]
    private string _autoBackupFormat = "SQLite";

    [ObservableProperty]
    private string _autoBackupFolder = string.Empty;

    public IReadOnlyList<string> BackupFormats { get; } = ["SQLite", "CsvArchive"];

    [ObservableProperty]
    private LogLevelOption? _selectedLogLevel;

    public ObservableCollection<LogLevelOption> AvailableLogLevels { get; } =
    [
        new LogLevelOption("Normal",  Resources.Settings_Advanced_Logging_Level_Normal),
        new LogLevelOption("Verbose", Resources.Settings_Advanced_Logging_Level_Verbose),
    ];

    public SettingsAdvancedTabViewModel(ISettingsService settingsService, IFilePickerService filePickerService)
    {
        _settingsService = settingsService;
        _filePickerService = filePickerService;
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        var folder = await _filePickerService.PickFolderAsync(Localization.Resources.FilePicker_ChooseAutoBackupDestination);
        if (!string.IsNullOrEmpty(folder))
            AutoBackupFolder = folder;
    }

    [RelayCommand]
    private void OpenLogFile()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BookDB");
        var logFileName = $"bookdb-{DateTime.Now:yyyyMMdd}.log";
        var logPath = Path.Combine(appDataPath, "logs", logFileName);
        var target = File.Exists(logPath) ? logPath : Path.Combine(appDataPath, "logs");
        try
        {
            Helpers.SystemLauncher.Open(target);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "OpenLogFile: failed to open log file or folder {Target}", target);
        }
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            var enabled = await _settingsService.GetAsync("AutoBackup.Enabled", ct);
            AutoBackupEnabled = enabled is not null && bool.TryParse(enabled, out var b) && b;

            AutoBackupFormat = await _settingsService.GetAsync("AutoBackup.Format", ct) ?? "SQLite";
            AutoBackupFolder = await _settingsService.GetAsync("LastBackupFolder", ct) ?? string.Empty;

            var storedLevel = await _settingsService.GetAsync("LogLevel", ct) ?? "Normal";
            SelectedLogLevel = AvailableLogLevels.FirstOrDefault(l => l.Value == storedLevel)
                ?? AvailableLogLevels.First();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsAdvancedTabViewModel: LoadAsync failed");
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            await _settingsService.SetAsync("AutoBackup.Enabled", AutoBackupEnabled.ToString().ToLowerInvariant(), ct);
            await _settingsService.SetAsync("AutoBackup.Format", AutoBackupFormat, ct);
            if (!string.IsNullOrWhiteSpace(AutoBackupFolder))
                await _settingsService.SetAsync("LastBackupFolder", AutoBackupFolder, ct);

            if (SelectedLogLevel is not null)
                await _settingsService.SetAsync("LogLevel", SelectedLogLevel.Value, ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsAdvancedTabViewModel: SaveAsync failed");
        }
    }
}
