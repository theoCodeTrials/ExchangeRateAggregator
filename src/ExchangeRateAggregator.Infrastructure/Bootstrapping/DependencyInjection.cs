using ExchangeRateAggregator.Application.Providers;
using ExchangeRateAggregator.Infrastructure.Providers;
using ExchangeRateAggregator.Infrastructure.Statistics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace ExchangeRateAggregator.Infrastructure.Bootstrapping;

public static class DependencyInjection
{
    private const string RetryPipelineName = "exchange-rate-retry";

    public static IServiceCollection AddExchangeRateProviderClients(
        this IServiceCollection services,
        Uri nbpBaseAddress,
        Uri frankfurterBaseAddress,
        Uri fawazBaseAddress,
        TimeSpan requestTimeout,
        Action<IHttpClientBuilder>? configureClientBuilder = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            requestTimeout, TimeSpan.Zero);

        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<InMemoryProviderStatisticsStore>();

        RegisterProviderClient<NbpExchangeRateProvider>(
            services,
            NbpExchangeRateProvider.ProviderName,
            nbpBaseAddress,
            requestTimeout,
            configureClientBuilder);

        RegisterProviderClient<FrankfurterExchangeRateProvider>(
            services,
            FrankfurterExchangeRateProvider.ProviderName,
            frankfurterBaseAddress,
            requestTimeout,
            configureClientBuilder);

        RegisterProviderClient<FawazExchangeRateProvider>(
            services,
            FawazExchangeRateProvider.ProviderName,
            fawazBaseAddress,
            requestTimeout,
            configureClientBuilder);

        return services;
    }

    public static IHttpClientBuilder AddExchangeRateRetry(
        this IHttpClientBuilder clientBuilder,
        int maxRetryAttempts,
        TimeSpan retryDelay)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRetryAttempts);

        if (retryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryDelay), retryDelay, "Retry delay must be non-negative.");
        }

        clientBuilder.AddResilienceHandler(RetryPipelineName, pipeline =>
        {
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = maxRetryAttempts,
                Delay = retryDelay,
                BackoffType = DelayBackoffType.Constant,
                ShouldRetryAfterHeader = true
            });
        });

        return clientBuilder;
    }

    public static IServiceCollection AddExchangeRatePerformanceMonitoring(this IServiceCollection services) => 
        services.AddHostedService<ProviderPerformanceMonitor>();

    public static IServiceCollection AddCachedExchangeRateProviders(
        this IServiceCollection services,
        TimeSpan cacheLifetime)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            cacheLifetime, TimeSpan.Zero);

        services.AddMemoryCache();

        RegisterCachedProvider<NbpExchangeRateProvider>(services, cacheLifetime);
        RegisterCachedProvider<FrankfurterExchangeRateProvider>(services, cacheLifetime);
        RegisterCachedProvider<FawazExchangeRateProvider>(services, cacheLifetime);

        return services;
    }

    private static void RegisterProviderClient<TProvider>(
        IServiceCollection services,
        string providerName,
        Uri baseAddress,
        TimeSpan requestTimeout,
        Action<IHttpClientBuilder>? configureClientBuilder)
        where TProvider : class, IExchangeRateProvider
    {
        var clientBuilder = services.AddHttpClient<TProvider>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = requestTimeout;
        });

        configureClientBuilder?.Invoke(clientBuilder);

        clientBuilder.AddHttpMessageHandler(serviceProvider =>
            new ProviderAttemptTimingHandler(
                providerName,
                serviceProvider.GetRequiredService<InMemoryProviderStatisticsStore>()));
    }

    private static void RegisterCachedProvider<TProvider>(
        IServiceCollection services,
        TimeSpan cacheLifetime)
        where TProvider : class, IExchangeRateProvider
    {
        services.AddTransient<IExchangeRateProvider>(serviceProvider =>
            new CachedExchangeRateProvider(
                serviceProvider.GetRequiredService<TProvider>(),
                serviceProvider.GetRequiredService<IMemoryCache>(),
                cacheLifetime));
    }
}