using System.Collections.ObjectModel;
using ExchangeRateAggregator.Application.Models;
using ExchangeRateAggregator.Application.Services;

namespace ExchangeRateAggregator.Application.Queries;

public class ExchangeRatesSorter
{
    private const string DescendingSortDirection = "desc";

    public ExchangeRateAggregationResult Apply(
        ExchangeRateAggregationResult aggregationResult,
        GetExchangeRatesQuery query)
    {
        if (query.SortDirection is null)
        {
            return aggregationResult;
        }

        var descending = string.Equals(
            query.SortDirection,
            DescendingSortDirection,
            StringComparison.OrdinalIgnoreCase);

        var sortedProviders = new Dictionary<string, ProviderRatesResult>(
            StringComparer.Ordinal);

        foreach (var (providerName, providerResult) in aggregationResult.SuccessfulProviders)
        {
            sortedProviders.Add(providerName, providerResult with
            {
                Rates = SortQuotes(providerResult.Rates, descending)
            });
        }

        return aggregationResult with
        {
            SuccessfulProviders =
                new ReadOnlyDictionary<string, ProviderRatesResult>(sortedProviders)
        };
    }

    private static IReadOnlyList<ExchangeRateQuote> SortQuotes(
        IReadOnlyList<ExchangeRateQuote> quotes,
        bool descending)
    {
        var sortedQuotes = descending
            ? quotes.OrderByDescending(quote => quote.TargetCurrency, StringComparer.OrdinalIgnoreCase)
            : quotes.OrderBy(quote => quote.TargetCurrency, StringComparer.OrdinalIgnoreCase);

        return Array.AsReadOnly(sortedQuotes.ToArray());
    }
}