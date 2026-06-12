using BookDB.Data.DbContexts;
using BookDB.Data.Interceptors;
using BookDB.Models;
using BookDB.Models.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookDB.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBookDbData(
        this IServiceCollection services, string connectionString)
    {
        // Shared, process-lifetime flag set by the SaveChanges interceptor whenever real data changes.
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();

        services.AddDbContextFactory<BookDbContext>((sp, options) =>
        {
            options.UseSqlite(connectionString);
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            options.AddInterceptors(
                new SqlitePragmaInterceptor(),
                new DataChangeCommandInterceptor(sp.GetRequiredService<IDataChangeTracker>()));
        });
        // IStartupProgressReporter is registered by the composition root (Desktop) since the
        // splash screen owns the startup-progress channel; this hosted service only reports into it.
        services.AddHostedService(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<DatabaseStartupService>>();
            var progress = sp.GetRequiredService<IStartupProgressReporter>();
            return new DatabaseStartupService(connectionString, logger, progress);
        });
        return services;
    }
}
