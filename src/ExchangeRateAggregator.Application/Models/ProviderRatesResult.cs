namespace ExchangeRateAggregator.Application.Models;

public record ProviderRatesResult(
    bool Failed,
    string BaseCurrency,
    DateOnly? PublishedDate,
    IReadOnlyList<ExchangeRateQuote> Rates);
