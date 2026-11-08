using System.Collections.ObjectModel;
using ExchangeRateAggregator.Application.Models;
using ExchangeRateAggregator.Application.Services;

namespace ExchangeRateAggregator.Application.Queries;

public class ExchangeRatesTargetCurrencyFilter
{
    public ExchangeRateAggregationResult Apply(
        ExchangeRateAggregationResult aggregationResult,
        string? targetCurrency)
    {
        if (targetCurrency is null)
        {
            return aggregationResult;
        }

        var filteredProviders = new Dictionary<string, ProviderRatesResult>(
            StringComparer.Ordinal);

        foreach (var (providerName, providerResult) in aggregationResult.SuccessfulProviders)
        {
            var matchingQuotes = providerResult.Rates
                .Where(quote => string.Equals(
                    quote.TargetCurrency,
                    targetCurrency,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            filteredProviders.Add(providerName, providerResult with
            {
                Rates = Array.AsReadOnly(matchingQuotes)
            });
        }

        return aggregationResult with
        {
            SuccessfulProviders =
                new ReadOnlyDictionary<string, ProviderRatesResult>(filteredProviders)
        };
    }
}