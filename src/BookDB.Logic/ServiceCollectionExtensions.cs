using BookDB.Logic.Import;
using BookDB.Logic.Services;
using BookDB.MetadataSources.Registration;
using BookDB.MetadataSources.Services;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace BookDB.Logic;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBookDbLogicServices(this IServiceCollection services)
    {
        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        services.AddSingleton<ILookupService, LookupService>();
        services.AddSingleton(sp => (ISettingsService)sp.GetRequiredService<ILookupService>());
        services.AddSingleton<LookupManagementService>();
        services.AddSingleton<ILookupManagementService>(sp => sp.GetRequiredService<LookupManagementService>());
        services.AddSingleton<BookService>();
        services.AddSingleton<IBookService>(sp => sp.GetRequiredService<BookService>());
        services.AddSingleton<LoanService>();
        services.AddSingleton<ILoanService>(sp => sp.GetRequiredService<LoanService>());
        services.AddSingleton<BorrowerService>();
        services.AddSingleton<IBorrowerService>(sp => sp.GetRequiredService<BorrowerService>());
        services.AddSingleton<BookSearchService>();
        services.AddSingleton<IBookSearchService>(sp => sp.GetRequiredService<BookSearchService>());
        services.AddSingleton<BookImageService>();
        services.AddSingleton<IBookImageService>(sp => sp.GetRequiredService<BookImageService>());
        services.AddSingleton<BookMetadataService>();
        services.AddSingleton<IBookMetadataService>(sp => sp.GetRequiredService<BookMetadataService>());
        services.AddSingleton<BatchQueueService>();
        services.AddSingleton<BatchQueueProcessor>();
        services.AddSingleton<IBatchQueueProcessor>(sp => sp.GetRequiredService<BatchQueueProcessor>());
        services.AddScoped<ReaderwareBackupParser>();
        services.AddScoped<IBackupParser, ReaderwareBackupParser>();
        services.AddScoped<ImportService>();
        services.AddScoped<IImportService>(sp => sp.GetRequiredService<ImportService>());
        services.AddScoped<IReaderwareDbExportService, ReaderwareDbExportService>();
        services.AddMetadataSources();
        // Last-registration wins: replaces AddMetadataSources()'s IMetadataLookupService binding.
        services.AddSingleton<MetadataLookupService>();
        services.AddSingleton<IMetadataLookupService, FilteringMetadataLookupService>();
        services.AddSingleton<IStatisticsService, StatisticsService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<ICsvExportService, CsvExportService>();
        services.AddSingleton<IPrintService, PrintService>();
        return services;
    }
}
