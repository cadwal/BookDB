using System;
using BookDB.Data;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using BookDB.Models;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace BookDB.Desktop;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBookDbDataServices(
        this IServiceCollection services, AppSettings appSettings)
    {
        services.AddSingleton(appSettings);
        services.AddBookDbData(appSettings.ConnectionString);
        return services;
    }

    public static IServiceCollection AddBookDbDesktopServices(this IServiceCollection services)
    {
        // Startup-progress channel: the splash ViewModel subscribes and DatabaseStartupService
        // (BookDB.Data) reports migration sub-progress into it. Singleton so they share one instance.
        services.AddSingleton<IStartupProgressReporter, StartupProgressReporter>();
        services.AddSingleton<IResourceProvider, DesktopResourceProvider>();
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<IWindowService, WindowService>();
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
