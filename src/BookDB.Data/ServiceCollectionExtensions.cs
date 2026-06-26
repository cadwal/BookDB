using BookDB.Data.Interfaces;
using BookDB.Models;
using BookDB.Models.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace BookDB.Data;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the provider-neutral data services: the change tracker and the migration hosted service.
    /// The active provider (its DbContext factory and <see cref="IDbUpRunner"/>) is registered separately
    /// by the composition root via the provider's own <c>AddXxxProvider</c> extension.
    /// </summary>
    public static IServiceCollection AddBookDbData(this IServiceCollection services)
    {
        // Shared, process-lifetime flag set by the SaveChanges interceptor whenever real data changes.
        services.AddSingleton<IDataChangeTracker, DataChangeTracker>();

        // IStartupProgressReporter is registered by the composition root (Desktop) since the splash
        // screen owns the startup-progress channel; this hosted service only reports into it.
        services.AddHostedService(sp =>
            new DatabaseStartupService(
                sp.GetRequiredService<IDbUpRunner>(),
                sp.GetRequiredService<IStartupProgressReporter>()));

        return services;
    }
}
