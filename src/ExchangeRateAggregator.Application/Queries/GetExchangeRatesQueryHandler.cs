using ExchangeRateAggregator.Application.Models;
using ExchangeRateAggregator.Application.Services;

namespace ExchangeRateAggregator.Application.Queries;

public class GetExchangeRatesQueryHandler
{
    private readonly ExchangeRateAggregationService _aggregationService;
    private readonly GetExchangeRatesQueryValidator _validator;
    private readonly ExchangeRatesTargetCurrencyFilter _targetCurrencyFilter;
    private readonly ExchangeRatesSorter _sorter;

    public GetExchangeRatesQueryHandler(
        ExchangeRateAggregationService aggregationService,
        GetExchangeRatesQueryValidator validator,
        ExchangeRatesTargetCurrencyFilter targetCurrencyFilter,
        ExchangeRatesSorter sorter)
    {
        _aggregationService = aggregationService;
        _validator = validator;
        _targetCurrencyFilter = targetCurrencyFilter;
        _sorter = sorter;
    }

    public async Task<ExchangeRateAggregationResult> HandleAsync(
        GetExchangeRatesQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var validationErrors = _validator.Validate(query, today);

        if (validationErrors.Count > 0)
        {
            return new ExchangeRateAggregationResult(
                SuccessfulProviders: new Dictionary<string, ProviderRatesResult>(),
                FailedProviders: Array.Empty<string>())
            {
                ValidationErrors = validationErrors
            };
        }

        var normalizedBase = query.BaseCurrency.ToUpperInvariant();
        var aggregationResult = await _aggregationService.GetRatesAsync(
            normalizedBase,
            query.RequestedDate,
            cancellationToken);

        var filteredResult = _targetCurrencyFilter.Apply(aggregationResult, query.TargetCurrency);

        return _sorter.Apply(filteredResult, query);
    }
}