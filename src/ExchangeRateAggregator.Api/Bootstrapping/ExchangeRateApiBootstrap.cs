using ExchangeRateAggregator.Api.Mappings;
using ExchangeRateAggregator.Application.Bootstrapping;
using ExchangeRateAggregator.Infrastructure.Bootstrapping;

namespace ExchangeRateAggregator.Api.Bootstrapping;

internal static class ExchangeRateApiBootstrap
{
    private const string ProvidersSectionName = "ExchangeRateProviders";
    private const string RetrySectionName = "Retry";
    private const string NbpBaseAddressKey = "NbpBaseAddress";
    private const string FrankfurterBaseAddressKey = "FrankfurterBaseAddress";
    private const string FawazBaseAddressKey = "FawazBaseAddress";
    private const string RequestTimeoutKey = "RequestTimeout";
    private const string CacheLifetimeKey = "CacheLifetime";
    private const string MaxRetryAttemptsKey = "MaxRetryAttempts";
    private const string RetryDelayKey = "Delay";

    public static WebApplicationBuilder AddExchangeRateApi(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddExchangeRateApplication()
            .AddSingleton<ExchangeRatesResponseMapper>();

        RegisterExchangeRateProviders(builder.Services, builder.Configuration);

        return builder;
    }

    private static void RegisterExchangeRateProviders(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var providerSettings = configuration.GetRequiredSection(ProvidersSectionName);
        var retrySettings = providerSettings.GetRequiredSection(RetrySectionName);

        var requestTimeout = GetRequiredValue<TimeSpan>(providerSettings, RequestTimeoutKey);
        var cacheLifetime = GetRequiredValue<TimeSpan>(providerSettings, CacheLifetimeKey);
        var maxRetryAttempts = GetRequiredValue<int>(retrySettings, MaxRetryAttemptsKey);
        var retryDelay = GetRequiredValue<TimeSpan>(retrySettings, RetryDelayKey);

        services.AddExchangeRateProviderClients(
            GetBaseAddress(providerSettings, NbpBaseAddressKey),
            GetBaseAddress(providerSettings, FrankfurterBaseAddressKey),
            GetBaseAddress(providerSettings, FawazBaseAddressKey),
            requestTimeout,
            clientBuilder => clientBuilder.AddExchangeRateRetry(maxRetryAttempts, retryDelay));

        services.AddCachedExchangeRateProviders(cacheLifetime);
    }

    private static T GetRequiredValue<T>(IConfigurationSection configuration, string key)
        where T : struct =>
        configuration.GetValue<T?>(key)
        ?? throw new InvalidOperationException(
            $"Missing configuration value '{configuration.Path}:{key}'.");

    private static Uri GetBaseAddress(IConfigurationSection configuration, string key)
    {
        if (Uri.TryCreate(configuration[key], UriKind.Absolute, out var baseAddress) &&
            (baseAddress.Scheme == Uri.UriSchemeHttps || baseAddress.Scheme == Uri.UriSchemeHttp) &&
            baseAddress.AbsolutePath.EndsWith('/'))
        {
            return baseAddress;
        }

        throw new InvalidOperationException(
            $"Configuration value '{configuration.Path}:{key}' must be an absolute HTTP or HTTPS base URL with a trailing slash.");
    }
}