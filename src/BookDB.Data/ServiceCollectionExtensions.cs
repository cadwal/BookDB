using BookDB.Data.DbContexts;
using BookDB.Data.Interceptors;
using BookDB.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookDB.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBookDbData(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContextFactory<BookDbContext>(options =>
        {
            options.UseSqlite(connectionString);
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            options.AddInterceptors(new SqlitePragmaInterceptor());
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
