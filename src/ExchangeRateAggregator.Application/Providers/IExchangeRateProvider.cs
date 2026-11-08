using ExchangeRateAggregator.Application.Models;

namespace ExchangeRateAggregator.Application.Providers;

public interface IExchangeRateProvider
{
    string Name { get; }

    Task<ProviderRatesResult> GetRatesAsync(
        string baseCurrency,
        DateOnly requestedDate,
        CancellationToken cancellationToken);
}