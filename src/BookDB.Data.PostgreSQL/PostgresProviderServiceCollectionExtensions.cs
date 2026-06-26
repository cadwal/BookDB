using BookDB.Data.DbContexts;
using BookDB.Data.Interceptors;
using BookDB.Data.Interfaces;
using BookDB.Models.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookDB.Data.PostgreSQL;

public static class PostgresProviderServiceCollectionExtensions
{
    // Remote connections can drop transiently; one automatic retry absorbs a sub-second blip. Beyond that the
    // app-level ConnectionHealthMonitor owns reconnection/backoff, so we fail fast here rather than freezing the
    // UI through several EF retries. The command timeout is set explicitly (Npgsql's default is 30 s) so a
    // stalled remote query can't hang the UI forever.
    private const int MaxRetryCount = 1;
    private const int CommandTimeoutSeconds = 30;

    public static IServiceCollection AddPostgresProvider(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContextFactory<BookDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.EnableRetryOnFailure(MaxRetryCount);
                npgsql.CommandTimeout(CommandTimeoutSeconds);
            });
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            // No pragma interceptor and no bool-to-integer convention — Npgsql maps bool natively. The model
            // customizer only pins DateTime columns to `timestamp without time zone` (timezone-independent).
            options.ReplaceService<IModelCustomizer, PostgresModelCustomizer>();
            options.AddInterceptors(
                new DataChangeCommandInterceptor(sp.GetRequiredService<IDataChangeTracker>()));
        });

        services.AddSingleton<IDbUpRunner>(sp =>
            new PostgresDbUpRunner(
                connectionString,
                sp.GetRequiredService<ILogger<DatabaseStartupService>>()));

        services.AddSingleton<IBookSearchProvider, PostgresBookSearchProvider>();
        services.AddSingleton<IMaintenanceProvider, PostgresMaintenanceProvider>();
        services.AddSingleton<IBackupStrategy, PostgresBackupStrategy>();
        services.AddSingleton<IConnectionFailureClassifier, PostgresConnectionFailureClassifier>();
        services.AddSingleton<IIdentitySequenceResync, PostgresIdentitySequenceResync>();
        services.AddSingleton<ILookupNameMatcher, PostgresLookupNameMatcher>();
        services.AddSingleton<IConstraintViolationClassifier, PostgresConstraintViolationClassifier>();

        return services;
    }
}
