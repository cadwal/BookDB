using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Messages;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using BookDB.Desktop.Theming;
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
    private readonly IApplicationRestartService _restartService;
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
    public SettingsApplicationAccessTabViewModel ApplicationAccessTab { get; }
    public SettingsAppearanceTabViewModel AppearanceTab { get; }
    public DatabaseSettingsViewModel DatabaseTab { get; }

    public SettingsWindowViewModel(
        ISettingsService settingsService,
        ILookupService lookupService,
        IFilePickerService filePickerService,
        IShortcutService shortcutService,
        IBootstrapConfigService bootstrapConfig,
        SecretStoreAvailability secretStoreAvailability,
        IPostgresConnectionProber connectionProber,
        IMySqlConnectionProber mySqlConnectionProber,
        ISecretStore secretStore,
        IApplicationRestartService restartService,
        IBackupStrategy backupStrategy,
        IMessenger messenger)
    {
        _settingsService = settingsService;
        _lookupService   = lookupService;
        _restartService  = restartService;
        _messenger       = messenger;

        GeneralTab  = new SettingsGeneralTabViewModel(settingsService, lookupService, bootstrapConfig);
        BrowseTab   = new SettingsBrowseTabViewModel(settingsService);
        LookupTab   = new SettingsLookupTabViewModel(settingsService);
        ImportTab   = new SettingsImportTabViewModel(settingsService, filePickerService);
        AdvancedTab = new SettingsAdvancedTabViewModel(
            settingsService, filePickerService, bootstrapConfig,
            supportsFileBackup: backupStrategy.SupportsFileBackup);
        ApplicationAccessTab = new SettingsApplicationAccessTabViewModel(shortcutService);
        AppearanceTab = new SettingsAppearanceTabViewModel(bootstrapConfig);
        DatabaseTab = new DatabaseSettingsViewModel(
            bootstrapConfig, secretStoreAvailability, connectionProber, mySqlConnectionProber, secretStore);
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
            await ApplicationAccessTab.LoadAsync(ct);
            await AppearanceTab.LoadAsync(ct);
            await DatabaseTab.LoadAsync(ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsWindowViewModel: InitializeAsync failed");
        }
    }

    // The Database tab is the last TabItem in SettingsWindow.axaml; Save focuses it to surface a blocking error.
    private const int DatabaseTabIndex = 7;

    [RelayCommand]
    private async Task SaveAsync()
    {
        // A backend/connection change is config.json-only and forces a restart. It is handled on its own path:
        // the per-database preference tabs target the current backend — which may be the unreachable server the
        // user is switching away from — and are re-read from the new backend after the restart, so they are
        // skipped. This keeps Save from hanging on writes to a dead database during outage recovery.
        if (DatabaseTab.IsDirty)
        {
            if (!DatabaseTab.ValidateForSave())
            {
                SelectedTabIndex = DatabaseTabIndex;
                return;
            }
            try
            {
                await DatabaseTab.SaveAsync();
                _messenger.Send(new SettingsSavedMessage());
                if (DatabaseTab.DbChanged
                    && await _restartService.ConfirmRestartAsync(Resources.Settings_RestartConfirm_Body))
                {
                    _restartService.Restart(); // replaces the process — nothing after this runs
                    return;
                }
                CloseDialog?.Invoke(true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SettingsWindowViewModel: SaveAsync (backend switch) failed");
            }
            return;
        }

        try
        {
            await GeneralTab.SaveAsync();
            await BrowseTab.SaveAsync();
            await LookupTab.SaveAsync();
            await ImportTab.SaveAsync();
            await AdvancedTab.SaveAsync();
            await ApplicationAccessTab.SaveAsync();
            await AppearanceTab.SaveAsync();
            _messenger.Send(new SettingsSavedMessage());

            // Language, theme and log level are read once at startup, so each only takes effect after a restart.
            var restartNeeded = GeneralTab.LanguageChanged || AppearanceTab.ThemeChanged || AdvancedTab.LogLevelChanged;
            CloseDialog?.Invoke(true);

            if (restartNeeded && await _restartService.ConfirmRestartAsync(Resources.Settings_RestartConfirm_Body))
                _restartService.Restart();
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

public sealed record ThemeFlavourOption(ThemeFlavour Flavour, string DisplayName);

// ============================================================
// General Tab
// ============================================================

public sealed partial class SettingsGeneralTabViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ILookupService _lookupService;
    private readonly IBootstrapConfigService _bootstrapConfig;

    private string _loadedLanguageCode = "en";

    /// <summary>True when the user picked a language different from the one loaded — a restart-requiring change.</summary>
    public bool LanguageChanged => SelectedLanguage is not null && SelectedLanguage.CultureName != _loadedLanguageCode;

    [ObservableProperty]
    private int? _defaultCollectionId;

    public ObservableCollection<CollectionItem> Collections { get; } = [];

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    public ObservableCollection<LanguageOption> AvailableLanguages { get; } = [];

    public SettingsGeneralTabViewModel(
        ISettingsService settingsService,
        ILookupService lookupService,
        IBootstrapConfigService bootstrapConfig)
    {
        _settingsService = settingsService;
        _lookupService = lookupService;
        _bootstrapConfig = bootstrapConfig;
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

            _loadedLanguageCode = _bootstrapConfig.Load().Language ?? "en";
            SelectedLanguage = AvailableLanguages.FirstOrDefault(l =>
                l.CultureName == _loadedLanguageCode)
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

            // config.json owns the language, not the Settings table (unlike DefaultCollectionId above)
            if (SelectedLanguage is not null)
            {
                var cultureName = SelectedLanguage.CultureName;
                _bootstrapConfig.Update(c => c.Language = cultureName);
            }
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
    private readonly IBootstrapConfigService _bootstrapConfig;

    private string _loadedLogLevel = "Normal";

    /// <summary>True when the user picked a log level different from the one loaded — a restart-requiring change.</summary>
    public bool LogLevelChanged => SelectedLogLevel is not null && SelectedLogLevel.Value != _loadedLogLevel;

    private readonly bool _supportsFileBackup;

    [ObservableProperty]
    private bool _autoBackupEnabled;

    [ObservableProperty]
    private string _autoBackupFormat = "SQLite";

    [ObservableProperty]
    private string _autoBackupFolder = string.Empty;

    public IReadOnlyList<string> BackupFormats { get; } = ["SQLite", "CsvArchive"];

    /// <summary>Shown on a remote backend that can't do a file backup, so the constraint is visible regardless of
    /// the selected format — a SQLite auto-backup there falls back to CSV.</summary>
    public bool ShowRemoteBackupNote => !_supportsFileBackup;

    [ObservableProperty]
    private LogLevelOption? _selectedLogLevel;

    public ObservableCollection<LogLevelOption> AvailableLogLevels { get; } =
    [
        new LogLevelOption("Normal",  Resources.Settings_Advanced_Logging_Level_Normal),
        new LogLevelOption("Verbose", Resources.Settings_Advanced_Logging_Level_Verbose),
    ];

    public SettingsAdvancedTabViewModel(
        ISettingsService settingsService,
        IFilePickerService filePickerService,
        IBootstrapConfigService bootstrapConfig,
        bool supportsFileBackup)
    {
        _settingsService = settingsService;
        _filePickerService = filePickerService;
        _bootstrapConfig = bootstrapConfig;
        _supportsFileBackup = supportsFileBackup;
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

            _loadedLogLevel = _bootstrapConfig.Load().LogLevel ?? "Normal";
            SelectedLogLevel = AvailableLogLevels.FirstOrDefault(l => l.Value == _loadedLogLevel)
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
            {
                var level = SelectedLogLevel.Value;
                _bootstrapConfig.Update(c => c.LogLevel = level);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsAdvancedTabViewModel: SaveAsync failed");
        }
    }
}

// ============================================================
// Application Access Tab
// ============================================================

public sealed partial class SettingsApplicationAccessTabViewModel : ObservableObject
{
    private readonly IShortcutService _shortcutService;

    public SettingsApplicationAccessTabViewModel(IShortcutService shortcutService)
    {
        _shortcutService = shortcutService;
        RefreshStates();
    }

    public bool IsWindows => _shortcutService.IsWindows;
    public bool IsLinux => _shortcutService.IsLinux;
    public bool IsUnsupported => !_shortcutService.IsSupported;

    [ObservableProperty]
    private string _startMenuState = string.Empty;

    [ObservableProperty]
    private string _desktopState = string.Empty;

    [ObservableProperty]
    private string _applicationMenuState = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    // Action tab — nothing to persist; refresh the live shortcut states when the tab loads.
    public Task LoadAsync(CancellationToken ct = default)
    {
        RefreshStates();
        return Task.CompletedTask;
    }

    public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;

    [RelayCommand]
    private void AddToStartMenu() => Report(_shortcutService.CreateStartMenuShortcut());

    [RelayCommand]
    private void AddDesktopShortcut() => Report(_shortcutService.CreateDesktopShortcut());

    [RelayCommand]
    private void AddToApplicationMenu() => Report(_shortcutService.CreateApplicationMenuEntry());

    private void Report(ShortcutResult result)
    {
        if (result.Status == ShortcutStatus.Failed)
            Log.Error("SettingsApplicationAccessTabViewModel: shortcut creation failed: {Error}", result.Error);

        StatusMessage = result.Status switch
        {
            ShortcutStatus.Created => Resources.Settings_AppAccess_StatusCreated,
            ShortcutStatus.CreatedWithWingetWarning => Resources.Settings_AppAccess_StatusWingetWarning,
            _ => Resources.Settings_AppAccess_StatusFailed,
        };
        RefreshStates();
    }

    private void RefreshStates()
    {
        if (_shortcutService.IsWindows)
        {
            StartMenuState = Describe(_shortcutService.GetStartMenuShortcutState());
            DesktopState = Describe(_shortcutService.GetDesktopShortcutState());
        }
        else if (_shortcutService.IsLinux)
        {
            ApplicationMenuState = Describe(_shortcutService.GetApplicationMenuEntryState());
        }
    }

    private static string Describe(ShortcutState state) => state switch
    {
        ShortcutState.UpToDate => Resources.Settings_AppAccess_State_UpToDate,
        ShortcutState.Mismatch => Resources.Settings_AppAccess_State_Mismatch,
        ShortcutState.Missing => Resources.Settings_AppAccess_State_Missing,
        _ => string.Empty,
    };
}

// ============================================================
// Appearance Tab
// ============================================================

public sealed partial class SettingsAppearanceTabViewModel : ObservableObject
{
    private readonly IBootstrapConfigService _bootstrapConfig;

    private ThemeFlavour _loadedFlavour;

    /// <summary>True when the user picked a flavour different from the one loaded — a restart-requiring change.</summary>
    public bool ThemeChanged => SelectedFlavour is not null && SelectedFlavour.Flavour != _loadedFlavour;

    [ObservableProperty]
    private ThemeFlavourOption? _selectedFlavour;

    public ObservableCollection<ThemeFlavourOption> AvailableFlavours { get; } =
    [
        new ThemeFlavourOption(ThemeFlavour.Default,      Resources.Settings_Appearance_Flavour_Default),
        new ThemeFlavourOption(ThemeFlavour.Vibrant,      Resources.Settings_Appearance_Flavour_Vibrant),
        new ThemeFlavourOption(ThemeFlavour.HighContrast, Resources.Settings_Appearance_Flavour_HighContrast),
        new ThemeFlavourOption(ThemeFlavour.Dark,         Resources.Settings_Appearance_Flavour_Dark),
    ];

    public SettingsAppearanceTabViewModel(IBootstrapConfigService bootstrapConfig)
    {
        _bootstrapConfig = bootstrapConfig;
    }

    public Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            _loadedFlavour = ThemeSettings.Parse(_bootstrapConfig.Load().UiTheme);
            SelectedFlavour = AvailableFlavours.FirstOrDefault(f => f.Flavour == _loadedFlavour)
                ?? AvailableFlavours.First();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsAppearanceTabViewModel: LoadAsync failed");
        }
        return Task.CompletedTask;
    }

    public Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            if (SelectedFlavour is not null)
            {
                var value = ThemeSettings.ToStorageValue(SelectedFlavour.Flavour);
                _bootstrapConfig.Update(c => c.UiTheme = value);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsAppearanceTabViewModel: SaveAsync failed");
        }
        return Task.CompletedTask;
    }
}
