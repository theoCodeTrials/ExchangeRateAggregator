namespace ExchangeRateAggregator.Api.Models;

public record ExchangeRatesResponse(
    string BaseCurrency,
    DateOnly RequestedDate,
    IReadOnlyList<ProviderRatesResponse> Providers,
    IReadOnlyList<string> FailedProviders,
    bool AllProvidersFailed);