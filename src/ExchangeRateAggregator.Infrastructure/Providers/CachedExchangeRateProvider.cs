using ExchangeRateAggregator.Application.Models;
using ExchangeRateAggregator.Application.Providers;
using Microsoft.Extensions.Caching.Memory;

namespace ExchangeRateAggregator.Infrastructure.Providers;

public class CachedExchangeRateProvider : IExchangeRateProvider
{
    private readonly IExchangeRateProvider _innerProvider;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheLifetime;

    public CachedExchangeRateProvider(
        IExchangeRateProvider inner,
        IMemoryCache cache,
        TimeSpan ttl)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);

        _innerProvider = inner;
        _cache = cache;
        _cacheLifetime = ttl;
    }

    public string Name => _innerProvider.Name;

    public async Task<ProviderRatesResult> GetRatesAsync(
        string baseCurrency,
        DateOnly requestedDate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedBase = baseCurrency.ToUpperInvariant();
        var cacheKey = new RateSnapshotCacheKey(Name, normalizedBase, requestedDate);

        if (_cache.TryGetValue<ProviderRatesResult>(cacheKey, out var cachedResult)
            && cachedResult is not null)
        {
            return cachedResult;
        }

        var providerResult = await _innerProvider.GetRatesAsync(
            normalizedBase, requestedDate, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (providerResult.Failed || providerResult.Rates.Count == 0)
        {
            return providerResult;
        }

        var cachedSnapshot = providerResult with
        {
            Rates = Array.AsReadOnly(providerResult.Rates.ToArray())
        };

        _cache.Set(cacheKey, cachedSnapshot, _cacheLifetime);

        return cachedSnapshot;
    }

    private readonly record struct RateSnapshotCacheKey(
        string ProviderName,
        string BaseCurrency,
        DateOnly RequestedDate);
}