namespace ExchangeRateAggregator.Api.Models;

public record ProviderRatesResponse(
    string ProviderName,
    IReadOnlyList<ExchangeRateQuoteResponse> Rates);