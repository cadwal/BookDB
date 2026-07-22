using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BookDB.MetadataSources.Services;
using BookDB.MetadataSources.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Retry;

namespace BookDB.MetadataSources.Registration;

public static class MetadataSourcesExtensions
{
    public static IServiceCollection AddMetadataSources(this IServiceCollection services)
    {
        // Reduced timeouts for user-visible batch operations:
        // 10s per attempt, 2 retries = max 30s total per source (within our per-item 30s guard).
        static void ConfigureResilience(HttpStandardResilienceOptions opts)
        {
            opts.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(25);
            opts.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            opts.Retry.MaxRetryAttempts = 1;
        }

        // Holds the optional Google Books API key; the Logic layer refreshes it from settings
        // before each lookup, and GoogleBooksClient reads it to move off the anonymous quota.
        services.AddSingleton<IGoogleBooksApiKeyAccessor, GoogleBooksApiKeyAccessor>();

        // Google Books uses a custom resilience pipeline that respects 429 Retry-After headers.
        // The standard handler retries 429 immediately, making rate limiting worse.
        services.AddHttpClient<GoogleBooksClient>(client =>
        {
            client.BaseAddress = new Uri("https://www.googleapis.com/books/v1/");
            client.DefaultRequestHeaders.Add("User-Agent", "BookDB/1.0");
        })
        .AddResilienceHandler("google-books", builder =>
        {
            // Total request timeout (all retries combined) — prevents unbounded waits
            builder.AddTimeout(TimeSpan.FromSeconds(25));
            // Retry: max 2 attempts, respect Retry-After header, exponential backoff with jitter
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Result?.StatusCode == HttpStatusCode.TooManyRequests ||
                    HttpClientResiliencePredicates.IsTransient(args.Outcome)),
                DelayGenerator = args =>
                {
                    // Respect Retry-After header when Google Books sends it with a 429
                    if (args.Outcome.Result?.Headers.RetryAfter?.Delta is { } retryAfter)
                        return new ValueTask<TimeSpan?>(retryAfter + TimeSpan.FromSeconds(1));
                    // Fallback: exponential backoff (2s, 4s for attempts 0 and 1)
                    return new ValueTask<TimeSpan?>(
                        TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber + 1)));
                }
            });
            // Per-attempt timeout
            builder.AddTimeout(TimeSpan.FromSeconds(10));
        });

        services.AddHttpClient<OpenLibraryClient>(client =>
        {
            client.BaseAddress = new Uri("https://openlibrary.org/");
            client.DefaultRequestHeaders.Add("User-Agent", "BookDB/1.0 (bookdb@example.com)");
        }).AddStandardResilienceHandler(ConfigureResilience);

        services.AddHttpClient<LibrisKbClient>(client =>
        {
            client.BaseAddress = new Uri("https://libris.kb.se/");
            client.DefaultRequestHeaders.Add("User-Agent", "BookDB/1.0");
        }).AddStandardResilienceHandler(ConfigureResilience);

        services.AddHttpClient<IsbnSearchOrgClient>(client =>
        {
            client.BaseAddress = new Uri("https://isbnsearch.org/");
            client.DefaultRequestHeaders.Add("User-Agent", "BookDB/1.0");
        }).AddStandardResilienceHandler(ConfigureResilience);

        services.AddSingleton<IMetadataSource>(sp => sp.GetRequiredService<GoogleBooksClient>());
        services.AddSingleton<IMetadataSource>(sp => sp.GetRequiredService<OpenLibraryClient>());
        services.AddSingleton<IMetadataSource>(sp => sp.GetRequiredService<LibrisKbClient>());
        services.AddSingleton<IMetadataSource>(sp => sp.GetRequiredService<IsbnSearchOrgClient>());

        services.AddSingleton<IMetadataLookupService, MetadataLookupService>();
        return services;
    }
}
