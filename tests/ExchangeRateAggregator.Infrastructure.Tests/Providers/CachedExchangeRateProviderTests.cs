using ExchangeRateAggregator.Application.Models;
using ExchangeRateAggregator.Application.Providers;
using ExchangeRateAggregator.Infrastructure.Providers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace ExchangeRateAggregator.Infrastructure.Tests.Providers;

public class CachedExchangeRateProviderTests
{
    private const string BaseCurrency = "EUR";
    private const string TargetCurrency = "USD";
    private const string ProviderName = "NBP";
    private const decimal PublishedRate = 1.08m;
    private const decimal EarlierRate = 1.07m;

    private static readonly DateOnly RequestedDate = new(2026, 11, 6);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);

    [Fact]
    public async Task GetRatesAsyncReusesTheSnapshotForTheSameNormalizedCurrencyAndDate()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var innerProvider = CreateProvider();
        var expectedResult = CreateSuccessfulResult(RequestedDate, PublishedRate);
        innerProvider.GetRatesAsync(BaseCurrency, RequestedDate, CancellationToken.None)
            .Returns(Task.FromResult(expectedResult));
        var cachedProvider = new CachedExchangeRateProvider(innerProvider, cache, CacheLifetime);

        var firstSnapshot = await cachedProvider.GetRatesAsync(
            BaseCurrency.ToLowerInvariant(), RequestedDate, CancellationToken.None);
        var secondSnapshot = await cachedProvider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        firstSnapshot.Should().BeEquivalentTo(expectedResult);
        secondSnapshot.Should().BeSameAs(firstSnapshot);
        _ = innerProvider.Received(1).GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);
    }

    [Fact]
    public async Task GetRatesAsyncCachesSeparateSnapshotsForDifferentDates()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var innerProvider = CreateProvider();
        var earlierDate = RequestedDate.AddDays(-1);
        var expectedEarlierResult = CreateSuccessfulResult(earlierDate, EarlierRate);
        var expectedRequestedResult = CreateSuccessfulResult(RequestedDate, PublishedRate);
        innerProvider.GetRatesAsync(BaseCurrency, earlierDate, CancellationToken.None)
            .Returns(Task.FromResult(expectedEarlierResult));
        innerProvider.GetRatesAsync(BaseCurrency, RequestedDate, CancellationToken.None)
            .Returns(Task.FromResult(expectedRequestedResult));
        var cachedProvider = new CachedExchangeRateProvider(innerProvider, cache, CacheLifetime);

        var earlierSnapshot = await cachedProvider.GetRatesAsync(
            BaseCurrency, earlierDate, CancellationToken.None);
        var requestedSnapshot = await cachedProvider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);
        var cachedEarlierSnapshot = await cachedProvider.GetRatesAsync(
            BaseCurrency, earlierDate, CancellationToken.None);
        var cachedRequestedSnapshot = await cachedProvider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        earlierSnapshot.Should().BeEquivalentTo(expectedEarlierResult);
        requestedSnapshot.Should().BeEquivalentTo(expectedRequestedResult);
        cachedEarlierSnapshot.Should().BeSameAs(earlierSnapshot);
        cachedRequestedSnapshot.Should().BeSameAs(requestedSnapshot);
        _ = innerProvider.Received(1).GetRatesAsync(
            BaseCurrency, earlierDate, CancellationToken.None);
        _ = innerProvider.Received(1).GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);
    }

    [Fact]
    public async Task GetRatesAsyncFetchesAgainAfterTheProviderReturnsAFailure()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var innerProvider = CreateProvider();
        var failedResult = new ProviderRatesResult(
            Failed: true,
            BaseCurrency: BaseCurrency,
            PublishedDate: null,
            Rates: Array.Empty<ExchangeRateQuote>());
        var expectedRecoveredResult = CreateSuccessfulResult(RequestedDate, PublishedRate);
        innerProvider.GetRatesAsync(BaseCurrency, RequestedDate, CancellationToken.None)
            .Returns(Task.FromResult(failedResult), Task.FromResult(expectedRecoveredResult));
        var cachedProvider = new CachedExchangeRateProvider(innerProvider, cache, CacheLifetime);

        var firstResult = await cachedProvider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);
        var recoveredSnapshot = await cachedProvider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        firstResult.Should().BeSameAs(failedResult);
        recoveredSnapshot.Should().BeEquivalentTo(expectedRecoveredResult);
        _ = innerProvider.Received(2).GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);
    }

    [Fact]
    public async Task GetRatesAsyncFetchesAgainAfterAnEmptyResultMarkedSuccessful()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var innerProvider = CreateProvider();
        var emptyResult = new ProviderRatesResult(
            Failed: false,
            BaseCurrency: BaseCurrency,
            PublishedDate: RequestedDate,
            Rates: Array.Empty<ExchangeRateQuote>());
        var expectedPublishedResult = CreateSuccessfulResult(RequestedDate, PublishedRate);
        innerProvider.GetRatesAsync(BaseCurrency, RequestedDate, CancellationToken.None)
            .Returns(Task.FromResult(emptyResult), Task.FromResult(expectedPublishedResult));
        var cachedProvider = new CachedExchangeRateProvider(innerProvider, cache, CacheLifetime);

        var firstResult = await cachedProvider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);
        var publishedSnapshot = await cachedProvider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        firstResult.Should().BeSameAs(emptyResult);
        publishedSnapshot.Should().BeEquivalentTo(expectedPublishedResult);
        _ = innerProvider.Received(2).GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);
    }

    private static ProviderRatesResult CreateSuccessfulResult(DateOnly publishedDate, decimal rate) =>
        new(
            Failed: false,
            BaseCurrency: BaseCurrency,
            PublishedDate: publishedDate,
            Rates: new[] { new ExchangeRateQuote(TargetCurrency, rate) });

    private static IExchangeRateProvider CreateProvider()
    {
        var provider = Substitute.For<IExchangeRateProvider>();
        provider.Name.Returns(ProviderName);

        return provider;
    }
}