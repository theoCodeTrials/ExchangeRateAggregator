using System.Collections.ObjectModel;
using System.Text.Json;
using ExchangeRateAggregator.Application.Models;
using ExchangeRateAggregator.Application.Providers;
using Microsoft.Extensions.Logging;

namespace ExchangeRateAggregator.Application.Services;

public class ExchangeRateAggregationService
{
    private readonly IExchangeRateProvider[] _providers;
    private readonly ILogger<ExchangeRateAggregationService> _logger;

    public ExchangeRateAggregationService(
        IEnumerable<IExchangeRateProvider> providers,
        ILogger<ExchangeRateAggregationService> logger)
    {
        _providers = providers.ToArray();
        _logger = logger;
    }

    public async Task<ExchangeRateAggregationResult> GetRatesAsync(
        string baseCurrency,
        DateOnly requestedDate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedBase = baseCurrency.ToUpperInvariant();
        var providerResults = await Task.WhenAll(_providers.Select(
            provider => FetchRatesAsync(
                provider, normalizedBase, requestedDate, cancellationToken)));

        cancellationToken.ThrowIfCancellationRequested();

        return BuildAggregationResult(providerResults);
    }

    private async Task<(string ProviderName, ProviderRatesResult Result)> FetchRatesAsync(
        IExchangeRateProvider provider,
        string normalizedBase,
        DateOnly requestedDate,
        CancellationToken cancellationToken)
    {
        var providerName = provider.Name;

        try
        {
            var providerResult = await provider.GetRatesAsync(
                normalizedBase, requestedDate, cancellationToken);

            return (providerName, providerResult);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or InvalidDataException
                or JsonException or OperationCanceledException or TimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogWarning(
                exception,
                "Exchange rate provider {ProviderName} failed for {BaseCurrency} on {RequestedDate}",
                providerName,
                normalizedBase,
                requestedDate);

            return (providerName, new ProviderRatesResult(
                Failed: true,
                BaseCurrency: normalizedBase,
                PublishedDate: null,
                Rates: Array.Empty<ExchangeRateQuote>()));
        }
    }

    private static ExchangeRateAggregationResult BuildAggregationResult(
        IReadOnlyList<(string ProviderName, ProviderRatesResult Result)> providerResults)
    {
        var successfulProviders = new Dictionary<string, ProviderRatesResult>(
            StringComparer.Ordinal);
        var failedProviders = new List<string>();

        foreach (var (providerName, providerResult) in providerResults)
        {
            if (providerResult.Failed ||
                providerResult.PublishedDate is null ||
                providerResult.Rates.Count == 0)
            {
                failedProviders.Add(providerName);
                continue;
            }

            successfulProviders.Add(providerName, providerResult);
        }

        return new ExchangeRateAggregationResult(
            new ReadOnlyDictionary<string, ProviderRatesResult>(successfulProviders),
            failedProviders.AsReadOnly());
    }
}