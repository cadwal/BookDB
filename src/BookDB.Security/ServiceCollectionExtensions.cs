using BookDB.Data.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace BookDB.Security;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Probes the OS credential store once and registers the resulting <see cref="ISecretStore"/> (real or
    /// null-object) plus the <see cref="SecretStoreAvailability"/> the Settings UI reads to enable/disable the
    /// PostgreSQL option.
    /// </summary>
    public static IServiceCollection AddSecretStore(this IServiceCollection services)
    {
        var (store, availability) = SecretStoreFactory.Create();
        services.AddSingleton(store);
        services.AddSingleton(availability);
        return services;
    }
}
