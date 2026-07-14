using System;
using BookDB.Data;
using BookDB.Data.Interfaces;
using BookDB.Data.MySql;
using BookDB.Data.PostgreSQL;
using BookDB.Data.Sqlite;
using BookDB.Security;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using BookDB.Models;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace BookDB.Desktop;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBookDbDataServices(
        this IServiceCollection services, AppSettings appSettings)
    {
        services.AddSingleton(appSettings);

        switch (appSettings.Backend)
        {
            case DatabaseBackend.Sqlite:
                services.AddSqliteProvider(appSettings.ConnectionString);
                break;
            case DatabaseBackend.PostgreSql:
                services.AddPostgresProvider(appSettings.ConnectionString);
                break;
            case DatabaseBackend.MySql:
                services.AddMySqlProvider(appSettings.ConnectionString);
                break;
            default:
                throw new NotSupportedException(
                    $"Database backend '{appSettings.Backend}' is not supported yet.");
        }

        // Probes the OS credential store once and registers the result (real or null-object) plus the
        // availability the Settings UI reads to enable/disable the remote-backend options. The Settings UI
        // only shows a generic "no credential store" message, so the log carries the real cause (e.g.
        // libsecret missing, no Secret Service on the session bus). Warning, not Error — the app runs fine
        // on SQLite without a credential store.
        var (secretStore, secretStoreAvailability) = SecretStoreFactory.Create();
        if (!secretStoreAvailability.IsAvailable)
            Log.Warning("OS credential store unavailable — remote-database passwords cannot be stored: {Reason}",
                secretStoreAvailability.UnavailableReason);
        services.AddSingleton(secretStore);
        services.AddSingleton(secretStoreAvailability);
        // Backend-independent: a user on one backend must be able to test a remote server before switching to it.
        services.AddSingleton<IPostgresConnectionProber, PostgresConnectionProber>();
        services.AddSingleton<IMySqlConnectionProber, MySqlConnectionProber>();
        services.AddBookDbData();
        return services;
    }

    public static IServiceCollection AddBookDbDesktopServices(this IServiceCollection services)
    {
        // Startup-progress channel: the splash ViewModel subscribes and DatabaseStartupService
        // (BookDB.Data) reports migration sub-progress into it. Singleton so they share one instance.
        services.AddSingleton<IStartupProgressReporter, StartupProgressReporter>();
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<IReleaseNotesService, ReleaseNotesService>();
        services.AddSingleton<IShortcutService, ShortcutService>();
        services.AddSingleton<IWindowService, WindowService>();
        services.AddSingleton<IRecatalogFlowService, RecatalogFlowService>();
        services.AddSingleton<IRemoteWriteGuard, RemoteWriteGuard>();
        services.AddSingleton<IMigrationTargetBuilder, MigrationTargetBuilder>();
        services.AddSingleton<IApplicationRestartService, ApplicationRestartService>();
        // IFilePickerService uses TopLevel which requires the visual tree to be attached.
        // The factory is deferred: TopLevel is resolved on first picker call (not at DI construction),
        // by which time the MainWindow is shown and TopLevel is available.
        services.AddSingleton<IFilePickerService>(sp =>
            new FilePickerService(() =>
            {
                var mainWindow = sp.GetRequiredService<MainWindow>();
                return Avalonia.Controls.TopLevel.GetTopLevel(mainWindow)
                    ?? throw new InvalidOperationException(
                        "TopLevel is null — file picker called before window attached.");
            }));
        services.AddSingleton<IClipboardService>(sp =>
            new ClipboardService(() =>
            {
                var mainWindow = sp.GetRequiredService<MainWindow>();
                return Avalonia.Controls.TopLevel.GetTopLevel(mainWindow)?.Clipboard;
            }));
        services.AddSingleton<FilterPanelViewModel>(sp =>
            new FilterPanelViewModel(
                sp.GetRequiredService<IMessenger>(),
                sp.GetRequiredService<IBookService>(),
                sp.GetRequiredService<IBookSearchService>(),
                sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<IWindowService>()));
        services.AddHttpClient<CoverFetchService>(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "BookDB/1.0");
        }).AddStandardResilienceHandler();
        // CoverFetchService uses a typed HttpClient from IHttpClientFactory.
        // Registering it as singleton via factory ensures the typed HttpClient is used,
        // not an unrelated HttpClient from a plain singleton registration.
        services.AddSingleton<CoverFetchService>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>();
            var http = httpClientFactory.CreateClient(nameof(CoverFetchService));
            return new CoverFetchService(http);
        });
        services.AddSingleton<ICoverFetcher>(sp =>
            sp.GetRequiredService<CoverFetchService>());
        return services;
    }

    public static IServiceCollection AddBookDbViewModels(this IServiceCollection services)
    {
        services.AddTransient<ImportWizardViewModel>();
        services.AddTransient<ReaderwareDbImportViewModel>();
        services.AddTransient<ManageLookupsViewModel>();
        services.AddTransient<SettingsWindowViewModel>();
        services.AddTransient<MaintenanceViewModel>();
        services.AddTransient<MoveLibraryViewModel>();
        services.AddTransient<StatisticsWindowViewModel>();
        services.AddTransient<HelpWindowViewModel>();
        services.AddTransient<CsvColumnPickerViewModel>();
        services.AddTransient<PrintDialogViewModel>();
        services.AddTransient<MergeTargetPickerViewModel>();
        services.AddTransient<BatchQueueWindowViewModel>();
        services.AddTransient<LookupWizardViewModel>(sp =>
            new LookupWizardViewModel(
                sp.GetRequiredService<IWindowService>(),
                sp.GetRequiredService<IFilePickerService>()));
        services.AddTransient<MergeReviewViewModel>();
        services.AddTransient<AdvancedSearchViewModel>();
        services.AddTransient<AddBookDialogViewModel>();
        services.AddTransient<FullDetailsWindowViewModel>();
        services.AddTransient<BulkEditViewModel>();
        services.AddTransient<CheckOutDialogViewModel>();
        services.AddTransient<ManageBorrowersViewModel>();
        services.AddTransient<SplashViewModel>();
        services.AddSingleton<BookListViewModel>();
        services.AddSingleton<BookDetailViewModel>();
        services.AddSingleton<CollectionSelectorViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        return services;
    }

    public static IServiceCollection AddBookDbViews(this IServiceCollection services)
    {
        services.AddSingleton<FilterPanelView>();
        services.AddSingleton<BookListView>();
        services.AddSingleton<BookDetailView>();
        services.AddSingleton<CollectionSelectorView>();
        services.AddTransient<SplashWindow>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
