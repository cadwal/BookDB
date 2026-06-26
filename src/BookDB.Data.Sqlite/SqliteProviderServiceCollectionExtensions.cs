using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Data.Interceptors;
using BookDB.Data.Interfaces;
using BookDB.Models.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookDB.Data.Sqlite;

public static class SqliteProviderServiceCollectionExtensions
{
    public static IServiceCollection AddSqliteProvider(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContextFactory<BookDbContext>((sp, options) =>
        {
            options.UseSqlite(connectionString);
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            options.ReplaceService<IModelCustomizer, SqliteModelCustomizer>();
            options.AddInterceptors(
                new SqlitePragmaInterceptor(),
                new DataChangeCommandInterceptor(sp.GetRequiredService<IDataChangeTracker>()));
        });

        services.AddSingleton<IDbUpRunner>(sp =>
            new SqliteDbUpRunner(
                connectionString,
                sp.GetRequiredService<ILogger<DatabaseStartupService>>()));

        services.AddSingleton<IBookSearchProvider, SqliteBookSearchProvider>();
        services.AddSingleton<IMaintenanceProvider, SqliteMaintenanceProvider>();
        services.AddSingleton<IBackupStrategy, SqliteBackupStrategy>();
        services.AddSingleton<IConnectionFailureClassifier, SqliteConnectionFailureClassifier>();
        services.AddSingleton<IIdentitySequenceResync, SqliteIdentitySequenceResync>();
        services.AddSingleton<ILookupNameMatcher, SqliteLookupNameMatcher>();
        services.AddSingleton<IConstraintViolationClassifier, SqliteConstraintViolationClassifier>();

        return services;
    }
}
