using ExchangeRateAggregator.Application.Models;
using ExchangeRateAggregator.Application.Validation;

namespace ExchangeRateAggregator.Application.Services;

public record ExchangeRateAggregationResult(
    IReadOnlyDictionary<string, ProviderRatesResult> SuccessfulProviders,
    IReadOnlyList<string> FailedProviders)
{
    public IReadOnlyList<ValidationError> ValidationErrors { get; init; } =
        Array.Empty<ValidationError>();

    public bool AllProvidersFailed =>
        ValidationErrors.Count == 0 &&
        SuccessfulProviders.Count == 0 &&
        FailedProviders.Count > 0;
}