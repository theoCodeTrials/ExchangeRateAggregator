using ExchangeRateAggregator.Application.Models;
using ExchangeRateAggregator.Application.Providers;
using ExchangeRateAggregator.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ExchangeRateAggregator.Application.Tests.Services;

public class ExchangeRateAggregationServiceTests
{
    private const string BaseCurrency = "EUR";
    private const string UsDollar = "USD";
    private const string BritishPound = "GBP";
    private const string NbpProviderName = "NBP";
    private const string FrankfurterProviderName = "Frankfurter";
    private const string FawazProviderName = "Fawaz Currency API";

    private static readonly DateOnly RequestedDate = new(2026, 11, 6);

    [Fact]
    public async Task GetRatesAsyncPreservesBothProvidersQuotesForTheSameCurrencyPair()
    {
        var nbpResult = CreateSuccessfulResult(new ExchangeRateQuote(UsDollar, 1.08m));
        var frankfurterResult = CreateSuccessfulResult(new ExchangeRateQuote(UsDollar, 1.08m));
        var nbpProvider = CreateProviderReturning(NbpProviderName, nbpResult);
        var frankfurterProvider = CreateProviderReturning(
            FrankfurterProviderName, frankfurterResult);
        var service = CreateService(nbpProvider, frankfurterProvider);

        var result = await service.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        result.SuccessfulProviders.Should().HaveCount(2);
        result.SuccessfulProviders[NbpProviderName].Should().BeSameAs(nbpResult);
        result.SuccessfulProviders[FrankfurterProviderName].Should().BeSameAs(frankfurterResult);
        result.FailedProviders.Should().BeEmpty();
        result.AllProvidersFailed.Should().BeFalse();
        _ = nbpProvider.Received(1).GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);
        _ = frankfurterProvider.Received(1).GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);
    }

    [Fact]
    public async Task GetRatesAsyncPreservesSuccessfulRatesAndReportsBothProviderFailures()
    {
        var fawazResult = CreateSuccessfulResult(new ExchangeRateQuote(BritishPound, 0.87m));
        var service = CreateService(
            CreateFailingProvider(NbpProviderName),
            CreateThrowingProvider(FrankfurterProviderName),
            CreateProviderReturning(FawazProviderName, fawazResult));

        var result = await service.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        result.SuccessfulProviders.Should().ContainSingle();
        result.SuccessfulProviders[FawazProviderName].Should().BeSameAs(fawazResult);
        result.FailedProviders.Should().BeEquivalentTo(
            new[] { NbpProviderName, FrankfurterProviderName });
        result.AllProvidersFailed.Should().BeFalse();
    }

    [Fact]
    public async Task GetRatesAsyncReportsTotalFailureWhenEveryProviderFails()
    {
        var service = CreateService(
            CreateFailingProvider(NbpProviderName),
            CreateFailingProvider(FrankfurterProviderName),
            CreateFailingProvider(FawazProviderName));

        var result = await service.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        result.SuccessfulProviders.Should().BeEmpty();
        result.FailedProviders.Should().BeEquivalentTo(
            new[] { NbpProviderName, FrankfurterProviderName, FawazProviderName });
        result.AllProvidersFailed.Should().BeTrue();
    }

    [Fact]
    public async Task GetRatesAsyncTreatsAnEmptyProviderResultAsFailure()
    {
        var emptyProviderResult = new ProviderRatesResult(
            Failed: false,
            BaseCurrency: BaseCurrency,
            PublishedDate: RequestedDate,
            Rates: Array.Empty<ExchangeRateQuote>());
        var service = CreateService(
            CreateProviderReturning(NbpProviderName, emptyProviderResult));

        var result = await service.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        result.SuccessfulProviders.Should().BeEmpty();
        result.FailedProviders.Should().Equal(NbpProviderName);
        result.AllProvidersFailed.Should().BeTrue();
    }

    [Fact]
    public async Task GetRatesAsyncSkipsProviderCallsWhenTheCallerHasAlreadyCanceled()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var provider = CreateProvider(NbpProviderName);
        var service = CreateService(provider);

        Func<Task> getRates = () => service.GetRatesAsync(
            BaseCurrency, RequestedDate, cancellationSource.Token);

        await getRates.Should().ThrowAsync<OperationCanceledException>();
        _ = provider.DidNotReceive().GetRatesAsync(
            Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRatesAsyncPropagatesCallerCancellationDuringAProviderCall()
    {
        using var cancellationSource = new CancellationTokenSource();
        var provider = CreateProvider(NbpProviderName);
        provider.GetRatesAsync(BaseCurrency, RequestedDate, cancellationSource.Token)
            .Returns(_ =>
            {
                cancellationSource.Cancel();
                return Task.FromCanceled<ProviderRatesResult>(cancellationSource.Token);
            });
        var service = CreateService(provider);

        Func<Task> getRates = () => service.GetRatesAsync(
            BaseCurrency, RequestedDate, cancellationSource.Token);

        await getRates.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetRatesAsyncObservesCancellationEvenWhenTheProviderReturnsRates()
    {
        using var cancellationSource = new CancellationTokenSource();
        var providerResult = CreateSuccessfulResult(new ExchangeRateQuote(UsDollar, 1.08m));
        var provider = CreateProvider(NbpProviderName);
        provider.GetRatesAsync(BaseCurrency, RequestedDate, cancellationSource.Token)
            .Returns(_ =>
            {
                cancellationSource.Cancel();
                return Task.FromResult(providerResult);
            });
        var service = CreateService(provider);

        Func<Task> getRates = () => service.GetRatesAsync(
            BaseCurrency, RequestedDate, cancellationSource.Token);

        await getRates.Should().ThrowAsync<OperationCanceledException>();
    }

    private static ExchangeRateAggregationService CreateService(
        params IExchangeRateProvider[] providers) =>
        new(providers, NullLogger<ExchangeRateAggregationService>.Instance);

    private static ProviderRatesResult CreateSuccessfulResult(ExchangeRateQuote quote) =>
        new(
            Failed: false,
            BaseCurrency: BaseCurrency,
            PublishedDate: RequestedDate,
            Rates: new[] { quote });

    private static IExchangeRateProvider CreateProviderReturning(
        string providerName,
        ProviderRatesResult providerResult)
    {
        var provider = CreateProvider(providerName);
        provider.GetRatesAsync(BaseCurrency, RequestedDate, CancellationToken.None)
            .Returns(Task.FromResult(providerResult));

        return provider;
    }

    private static IExchangeRateProvider CreateFailingProvider(string providerName) =>
        CreateProviderReturning(
            providerName,
            new ProviderRatesResult(
                Failed: true,
                BaseCurrency: BaseCurrency,
                PublishedDate: null,
                Rates: Array.Empty<ExchangeRateQuote>()));

    private static IExchangeRateProvider CreateThrowingProvider(string providerName)
    {
        var provider = CreateProvider(providerName);
        provider.GetRatesAsync(BaseCurrency, RequestedDate, CancellationToken.None)
            .Returns(Task.FromException<ProviderRatesResult>(
                new HttpRequestException($"{providerName} is unavailable.")));

        return provider;
    }

    private static IExchangeRateProvider CreateProvider(string providerName)
    {
        var provider = Substitute.For<IExchangeRateProvider>();
        provider.Name.Returns(providerName);

        return provider;
    }
}