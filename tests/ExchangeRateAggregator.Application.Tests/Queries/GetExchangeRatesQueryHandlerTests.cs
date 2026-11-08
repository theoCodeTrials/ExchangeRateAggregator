using ExchangeRateAggregator.Application.Models;
using ExchangeRateAggregator.Application.Providers;
using ExchangeRateAggregator.Application.Queries;
using ExchangeRateAggregator.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ExchangeRateAggregator.Application.Tests.Queries;

public class GetExchangeRatesQueryHandlerTests
{
    private const string BaseCurrency = "EUR";
    private const string LowercaseBaseCurrency = "eur";
    private const string UnsupportedBaseCurrency = "EYT";
    private const string TargetCurrency = "USD";
    private const string AustralianDollar = "AUD";
    private const string BritishPound = "GBP";
    private const string ProviderName = "NBP";

    private static readonly DateOnly HistoricalDate = new(2024, 3, 6);

    [Fact]
    public async Task HandleAsyncRejectsUnsupportedBaseCurrencyBeforeCallingProviders()
    {
        var provider = CreateProvider();
        var handler = CreateHandler(provider);
        var query = new GetExchangeRatesQuery(UnsupportedBaseCurrency, HistoricalDate);

        var result = await handler.HandleAsync(query, CancellationToken.None);

        result.ValidationErrors.Should().ContainSingle()
            .Which.Field.Should().Be(nameof(query.BaseCurrency));
        result.SuccessfulProviders.Should().BeEmpty();
        result.FailedProviders.Should().BeEmpty();
        result.AllProvidersFailed.Should().BeFalse();
        AssertProviderWasNotCalled(provider);
    }

    [Fact]
    public async Task HandleAsyncRejectsFutureDatesBeforeCallingProviders()
    {
        var provider = CreateProvider();
        var handler = CreateHandler(provider);
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
        var query = new GetExchangeRatesQuery(BaseCurrency, futureDate);

        var result = await handler.HandleAsync(query, CancellationToken.None);

        result.ValidationErrors.Should().ContainSingle()
            .Which.Field.Should().Be(nameof(query.RequestedDate));
        result.SuccessfulProviders.Should().BeEmpty();
        result.FailedProviders.Should().BeEmpty();
        AssertProviderWasNotCalled(provider);
    }

    [Fact]
    public async Task HandleAsyncReturnsBothValidationErrorsWithoutCallingProviders()
    {
        var provider = CreateProvider();
        var handler = CreateHandler(provider);
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
        var query = new GetExchangeRatesQuery(UnsupportedBaseCurrency, futureDate);

        var result = await handler.HandleAsync(query, CancellationToken.None);

        result.ValidationErrors.Select(error => error.Field).Should().BeEquivalentTo(
            new[] { nameof(query.BaseCurrency), nameof(query.RequestedDate) });
        result.SuccessfulProviders.Should().BeEmpty();
        result.FailedProviders.Should().BeEmpty();
        AssertProviderWasNotCalled(provider);
    }

    [Fact]
    public async Task HandleAsyncAggregatesUsingTheNormalizedCurrencyRequestedDateAndCallerToken()
    {
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        var provider = CreateProvider();
        var providerResult = new ProviderRatesResult(
            Failed: false,
            BaseCurrency: BaseCurrency,
            PublishedDate: HistoricalDate,
            Rates: new[] { new ExchangeRateQuote(TargetCurrency, 1.08m) });
        provider.GetRatesAsync(BaseCurrency, HistoricalDate, cancellationToken)
            .Returns(Task.FromResult(providerResult));
        var handler = CreateHandler(provider);
        var query = new GetExchangeRatesQuery(LowercaseBaseCurrency, HistoricalDate);

        var result = await handler.HandleAsync(query, cancellationToken);

        result.ValidationErrors.Should().BeEmpty();
        result.SuccessfulProviders.Should().ContainSingle();
        result.SuccessfulProviders[ProviderName].Should().BeEquivalentTo(providerResult);
        result.FailedProviders.Should().BeEmpty();
        _ = provider.Received(1).GetRatesAsync(
            BaseCurrency, HistoricalDate, cancellationToken);
    }

    [Fact]
    public async Task HandleAsyncPreservesTotalProviderFailureForAValidQuery()
    {
        var provider = CreateProvider();
        var providerResult = new ProviderRatesResult(
            Failed: true,
            BaseCurrency: BaseCurrency,
            PublishedDate: null,
            Rates: Array.Empty<ExchangeRateQuote>());
        provider.GetRatesAsync(BaseCurrency, HistoricalDate, CancellationToken.None)
            .Returns(Task.FromResult(providerResult));
        var handler = CreateHandler(provider);
        var query = new GetExchangeRatesQuery(BaseCurrency, HistoricalDate);

        var result = await handler.HandleAsync(query, CancellationToken.None);

        result.ValidationErrors.Should().BeEmpty();
        result.SuccessfulProviders.Should().BeEmpty();
        result.FailedProviders.Should().Equal(ProviderName);
        result.AllProvidersFailed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsyncPropagatesCancellationBeforeCallingProviders()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var provider = CreateProvider();
        var handler = CreateHandler(provider);
        var query = new GetExchangeRatesQuery(BaseCurrency, HistoricalDate);

        Func<Task> handleQuery = () => handler.HandleAsync(query, cancellationSource.Token);

        await handleQuery.Should().ThrowAsync<OperationCanceledException>();
        AssertProviderWasNotCalled(provider);
    }

    [Fact]
    public async Task HandleAsyncFiltersTargetCurrencyWithoutChangingTheProviderSnapshot()
    {
        var usdQuote = new ExchangeRateQuote(TargetCurrency, 1.08m);
        var gbpQuote = new ExchangeRateQuote(BritishPound, 0.87m);
        var provider = CreateProviderWithQuotes(usdQuote, gbpQuote);
        var handler = CreateHandler(provider);
        var query = new GetExchangeRatesQuery(
            BaseCurrency, HistoricalDate, TargetCurrency: TargetCurrency.ToLowerInvariant());

        var filteredResult = await handler.HandleAsync(query, CancellationToken.None);
        var unfilteredResult = await handler.HandleAsync(
            new GetExchangeRatesQuery(BaseCurrency, HistoricalDate),
            CancellationToken.None);

        filteredResult.SuccessfulProviders[ProviderName].Rates.Should().Equal(usdQuote);
        unfilteredResult.SuccessfulProviders[ProviderName].Rates.Should().Equal(usdQuote, gbpQuote);
    }

    [Fact]
    public async Task HandleAsyncKeepsTheProviderSuccessfulWhenNoQuotesMatchTheFilter()
    {
        var provider = CreateProviderWithQuotes(new ExchangeRateQuote(TargetCurrency, 1.08m));
        var handler = CreateHandler(provider);
        var query = new GetExchangeRatesQuery(
            BaseCurrency, HistoricalDate, TargetCurrency: BritishPound);

        var result = await handler.HandleAsync(query, CancellationToken.None);

        result.ValidationErrors.Should().BeEmpty();
        result.SuccessfulProviders.Should().ContainKey(ProviderName);
        result.SuccessfulProviders[ProviderName].Rates.Should().BeEmpty();
        result.FailedProviders.Should().BeEmpty();
        result.AllProvidersFailed.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, TargetCurrency, AustralianDollar, BritishPound)]
    [InlineData("asc", AustralianDollar, BritishPound, TargetCurrency)]
    [InlineData("DESC", TargetCurrency, BritishPound, AustralianDollar)]
    public async Task HandleAsyncOrdersQuotesAccordingToSortDirection(
        string? sortDirection,
        string firstCurrency,
        string secondCurrency,
        string thirdCurrency)
    {
        var usdQuote = new ExchangeRateQuote(TargetCurrency, 1.08m);
        var audQuote = new ExchangeRateQuote(AustralianDollar, 1.65m);
        var gbpQuote = new ExchangeRateQuote(BritishPound, 0.87m);
        var provider = CreateProviderWithQuotes(usdQuote, audQuote, gbpQuote);
        var handler = CreateHandler(provider);
        var query = new GetExchangeRatesQuery(
            BaseCurrency, HistoricalDate, SortDirection: sortDirection);

        var result = await handler.HandleAsync(query, CancellationToken.None);
        var originalOrderResult = await handler.HandleAsync(
            new GetExchangeRatesQuery(BaseCurrency, HistoricalDate),
            CancellationToken.None);

        result.ValidationErrors.Should().BeEmpty();
        result.SuccessfulProviders[ProviderName].Rates
            .Select(quote => quote.TargetCurrency).Should()
            .Equal(firstCurrency, secondCurrency, thirdCurrency);
        originalOrderResult.SuccessfulProviders[ProviderName].Rates.Should()
            .Equal(usdQuote, audQuote, gbpQuote);
    }

    [Fact]
    public async Task HandleAsyncRejectsABlankTargetCurrencyBeforeCallingProviders()
    {
        var provider = CreateProvider();
        var handler = CreateHandler(provider);
        var query = new GetExchangeRatesQuery(BaseCurrency, HistoricalDate, TargetCurrency: " ");

        var result = await handler.HandleAsync(query, CancellationToken.None);

        result.ValidationErrors.Should().ContainSingle()
            .Which.Field.Should().Be(nameof(query.TargetCurrency));
        AssertProviderWasNotCalled(provider);
    }

    [Fact]
    public async Task HandleAsyncRejectsAnInvalidSortDirectionBeforeCallingProviders()
    {
        var provider = CreateProvider();
        var handler = CreateHandler(provider);
        var query = new GetExchangeRatesQuery(
            BaseCurrency, HistoricalDate, SortDirection: "descending");

        var result = await handler.HandleAsync(query, CancellationToken.None);

        result.ValidationErrors.Should().ContainSingle()
            .Which.Field.Should().Be(nameof(query.SortDirection));
        AssertProviderWasNotCalled(provider);
    }

    private static IExchangeRateProvider CreateProviderWithQuotes(params ExchangeRateQuote[] quotes)
    {
        var provider = CreateProvider();
        var providerResult = new ProviderRatesResult(
            Failed: false,
            BaseCurrency: BaseCurrency,
            PublishedDate: HistoricalDate,
            Rates: quotes);

        provider.GetRatesAsync(BaseCurrency, HistoricalDate, CancellationToken.None)
            .Returns(Task.FromResult(providerResult));

        return provider;
    }

    private static GetExchangeRatesQueryHandler CreateHandler(IExchangeRateProvider provider)
    {
        var aggregationService = new ExchangeRateAggregationService(
            new[] { provider },
            NullLogger<ExchangeRateAggregationService>.Instance);

        return new GetExchangeRatesQueryHandler(
            aggregationService,
            new GetExchangeRatesQueryValidator(),
            new ExchangeRatesTargetCurrencyFilter(),
            new ExchangeRatesSorter());
    }

    private static IExchangeRateProvider CreateProvider()
    {
        var provider = Substitute.For<IExchangeRateProvider>();
        provider.Name.Returns(ProviderName);

        return provider;
    }

    private static void AssertProviderWasNotCalled(IExchangeRateProvider provider)
    {
        _ = provider.DidNotReceive().GetRatesAsync(
            Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }
}