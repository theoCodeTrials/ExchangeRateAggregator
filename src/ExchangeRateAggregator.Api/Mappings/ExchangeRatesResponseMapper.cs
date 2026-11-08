using ExchangeRateAggregator.Api.Models;
using ExchangeRateAggregator.Application.Models;
using ExchangeRateAggregator.Application.Services;

namespace ExchangeRateAggregator.Api.Mappings;

public class ExchangeRatesResponseMapper
{
    public ExchangeRatesResponse Map(
        GetExchangeRatesQuery query,
        ExchangeRateAggregationResult aggregationResult)
    {
        var providerResponses = aggregationResult.SuccessfulProviders
            .Select(provider => MapProvider(provider.Key, provider.Value))
            .ToArray();

        return new ExchangeRatesResponse(
            query.BaseCurrency.ToUpperInvariant(),
            query.RequestedDate,
            providerResponses,
            aggregationResult.FailedProviders,
            aggregationResult.AllProvidersFailed);
    }

    private ProviderRatesResponse MapProvider(
        string providerName,
        ProviderRatesResult providerResult)
    {
        var quoteResponses = providerResult.Rates
            .Select(quote => new ExchangeRateQuoteResponse(
                quote.TargetCurrency,
                quote.Rate))
            .ToArray();

        return new ProviderRatesResponse(providerName, quoteResponses);
    }
}