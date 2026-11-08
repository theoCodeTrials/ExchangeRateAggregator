using ExchangeRateAggregator.Application.Queries;
using ExchangeRateAggregator.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ExchangeRateAggregator.Application.Bootstrapping;

public static class DependencyInjection
{
    public static IServiceCollection AddExchangeRateApplication(
        this IServiceCollection services) => 
        services
        .AddScoped<ExchangeRateAggregationService>()
        .AddScoped<GetExchangeRatesQueryHandler>()
        .AddSingleton<GetExchangeRatesQueryValidator>()
        .AddSingleton<ExchangeRatesTargetCurrencyFilter>()
        .AddSingleton<ExchangeRatesSorter>();
}