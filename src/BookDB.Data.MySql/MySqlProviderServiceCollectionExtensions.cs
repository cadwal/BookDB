using System;
using BookDB.Data.DbContexts;
using BookDB.Data.Interceptors;
using BookDB.Data.Interfaces;
using BookDB.Models.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microting.EntityFrameworkCore.MySql.Infrastructure;

namespace BookDB.Data.MySql;

public static class MySqlProviderServiceCollectionExtensions
{
    // Remote connections can drop transiently; one automatic retry absorbs a sub-second blip. Beyond that the
    // app-level ConnectionHealthMonitor owns reconnection/backoff, so we fail fast here rather than freezing the
    // UI through several EF retries. The command timeout is set explicitly so a stalled remote query can't hang
    // the UI forever.
    private const int MaxRetryCount = 1;
    private const int CommandTimeoutSeconds = 30;

    public static IServiceCollection AddMySqlProvider(
        this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<MySqlServerVersionCache>();
        services.AddDbContextFactory<BookDbContext>((sp, options) =>
        {
            // The startup connectivity gate has already detected the active server's version (family + number);
            // reuse it so EF neither re-probes the server nor falls back to a family guess. The fallback only runs
            // for a context built with no prior probe (e.g. a move-target builder), where detecting it is correct.
            var serverVersion = sp.GetRequiredService<MySqlServerVersionCache>().Detected
                ?? ResolveServerVersion(connectionString);
            options.UseMySql(connectionString, serverVersion, mySql =>
            {
                mySql.EnableRetryOnFailure(MaxRetryCount);
                mySql.CommandTimeout(CommandTimeoutSeconds);
            });
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            // MySqlConnector maps bool to tinyint(1) natively; the model customizer only pins DateTime columns to
            // datetime(6) with a UTC value converter (timezone-independent).
            options.ReplaceService<IModelCustomizer, MySqlModelCustomizer>();
            options.AddInterceptors(
                new DataChangeCommandInterceptor(sp.GetRequiredService<IDataChangeTracker>()));
        });

        services.AddSingleton<IDbUpRunner>(sp =>
            new MySqlDbUpRunner(
                connectionString,
                sp.GetRequiredService<ILogger<DatabaseStartupService>>()));

        services.AddSingleton<IBookSearchProvider, MySqlBookSearchProvider>();
        services.AddSingleton<IMaintenanceProvider, MySqlMaintenanceProvider>();
        services.AddSingleton<IBackupStrategy, MySqlBackupStrategy>();
        services.AddSingleton<IConnectionFailureClassifier, MySqlConnectionFailureClassifier>();
        services.AddSingleton<IConstraintViolationClassifier, MySqlConstraintViolationClassifier>();
        services.AddSingleton<ILookupNameMatcher, MySqlLookupNameMatcher>();
        services.AddSingleton<IIdentitySequenceResync, MySqlIdentitySequenceResync>();

        return services;
    }

    // Fallback version detection for a context built with no prior probe (a move-target builder, or any path where
    // the gate's detected version isn't available). AutoDetect opens a connection to read the family + version; if
    // that fails (e.g. building options to repair an unreachable server in Settings), fall back to a baseline so
    // building the options never throws — which would take the Settings window down with it and break the recovery
    // flow. A real query still surfaces the failure through the connection-health path, and the next startup
    // re-detects the true version once the connection works.
    private static ServerVersion ResolveServerVersion(string connectionString)
    {
        try
        {
            return ServerVersion.AutoDetect(connectionString);
        }
        catch (Exception)
        {
            return new MySqlServerVersion(new Version(8, 0));
        }
    }
}
