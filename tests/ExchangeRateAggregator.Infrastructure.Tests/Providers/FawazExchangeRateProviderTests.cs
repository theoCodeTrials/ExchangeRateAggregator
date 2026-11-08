using ExchangeRateAggregator.Application.Models;
using ExchangeRateAggregator.Infrastructure.Providers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;

namespace ExchangeRateAggregator.Infrastructure.Tests.Providers;

public  class FawazExchangeRateProviderTests
{
    private const string BaseCurrency = "EUR";
    private const string LowercaseBaseCurrency = "eur";
    private const string TargetCurrency = "USD";
    private const decimal ExpectedRate = 1.08m;
    private const string JsonMediaType = "application/json";
    private const string EmptyPayload = "{}";

    private const string ValidSnapshot =
        """{"date":"2026-11-06","eur":{"usd":1.08}}""";

    private static readonly DateOnly RequestedDate = new(2026, 11, 6);
    private static readonly Uri BaseAddress = new("https://cdn.jsdelivr.net/");

    [Fact]
    public async Task GetRatesAsyncUsesTheRequestedDateAndLowercaseBaseCurrencyInTheRequest()
    {
        var expectedRequestUri = new Uri(
            BaseAddress, "npm/@fawazahmed0/currency-api@2026-11-06/v1/currencies/eur.json");
        var handler = new StubHttpMessageHandler(ValidSnapshot);
        using var client = CreateClient(handler);
        var provider = CreateProvider(client);

        await provider.GetRatesAsync(BaseCurrency, RequestedDate, CancellationToken.None);

        handler.RequestUri.Should().Be(expectedRequestUri);
    }

    [Fact]
    public async Task GetRatesAsyncMapsTheQuoteAndSnapshotMetadata()
    {
        using var client = CreateClient(new StubHttpMessageHandler(ValidSnapshot));
        var provider = CreateProvider(client);
        var expectedQuote = new ExchangeRateQuote(TargetCurrency, ExpectedRate);

        var providerResult = await provider.GetRatesAsync(
            LowercaseBaseCurrency, RequestedDate, CancellationToken.None);

        providerResult.Failed.Should().BeFalse();
        providerResult.BaseCurrency.Should().Be(BaseCurrency);
        providerResult.PublishedDate.Should().Be(RequestedDate);
        providerResult.Rates.Should().ContainSingle().Which.Should().Be(expectedQuote);
    }

    [Fact]
    public async Task GetRatesAsyncExcludesTheBaseCurrencyFromTheResults()
    {
        const string snapshotWithBaseRate =
            """{"date":"2026-11-06","eur":{"eur":1,"usd":1.08}}""";
        using var client = CreateClient(new StubHttpMessageHandler(snapshotWithBaseRate));
        var provider = CreateProvider(client);

        var providerResult = await provider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        providerResult.Failed.Should().BeFalse();
        providerResult.Rates.Should().ContainSingle().Which.TargetCurrency.Should().Be(TargetCurrency);
    }

    [Fact]
    public async Task GetRatesAsyncReturnsFailureWhenThePublicationDateIsInvalid()
    {
        const string snapshotWithInvalidDate =
            """{"date":"invalid","eur":{"usd":1.08}}""";
        using var client = CreateClient(new StubHttpMessageHandler(snapshotWithInvalidDate));
        var provider = CreateProvider(client);

        var providerResult = await provider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        AssertFailedResult(providerResult);
    }

    [Fact]
    public async Task GetRatesAsyncReturnsFailureWhenThePublicationDateDiffersFromTheRequestedDate()
    {
        const string snapshotForAnotherDate =
            """{"date":"2026-11-05","eur":{"usd":1.08}}""";
        using var client = CreateClient(new StubHttpMessageHandler(snapshotForAnotherDate));
        var provider = CreateProvider(client);

        var providerResult = await provider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        AssertFailedResult(providerResult);
    }

    [Fact]
    public async Task GetRatesAsyncReturnsFailureWhenTheRequestedBaseIsMissing()
    {
        const string snapshotWithoutRequestedBase =
            """{"date":"2026-11-06","usd":{"eur":0.92}}""";
        using var client = CreateClient(new StubHttpMessageHandler(snapshotWithoutRequestedBase));
        var provider = CreateProvider(client);

        var providerResult = await provider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        AssertFailedResult(providerResult);
    }

    [Fact]
    public async Task GetRatesAsyncReturnsFailureWhenThereAreNoTargetRates()
    {
        const string snapshotWithoutTargetRates =
            """{"date":"2026-11-06","eur":{"eur":1}}""";
        using var client = CreateClient(new StubHttpMessageHandler(snapshotWithoutTargetRates));
        var provider = CreateProvider(client);

        var providerResult = await provider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        AssertFailedResult(providerResult);
    }

    [Fact]
    public async Task GetRatesAsyncReturnsFailureWhenATargetRateIsZero()
    {
        const string snapshotWithZeroRate =
            """{"date":"2026-11-06","eur":{"usd":0}}""";
        using var client = CreateClient(new StubHttpMessageHandler(snapshotWithZeroRate));
        var provider = CreateProvider(client);

        var providerResult = await provider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        AssertFailedResult(providerResult);
    }

    [Fact]
    public async Task GetRatesAsyncReturnsFailureWhenTheSnapshotIsNotFound()
    {
        using var client = CreateClient(
            new StubHttpMessageHandler(EmptyPayload, HttpStatusCode.NotFound));
        var provider = CreateProvider(client);

        var providerResult = await provider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        AssertFailedResult(providerResult);
    }

    [Fact]
    public async Task GetRatesAsyncReturnsFailureWhenTheApiReturnsServiceUnavailable()
    {
        using var client = CreateClient(
            new StubHttpMessageHandler(EmptyPayload, HttpStatusCode.ServiceUnavailable));
        var provider = CreateProvider(client);

        var providerResult = await provider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        AssertFailedResult(providerResult);
    }

    private static void AssertFailedResult(ProviderRatesResult providerResult)
    {
        providerResult.Failed.Should().BeTrue();
        providerResult.BaseCurrency.Should().Be(BaseCurrency);
        providerResult.PublishedDate.Should().BeNull();
        providerResult.Rates.Should().BeEmpty();
    }

    private static FawazExchangeRateProvider CreateProvider(HttpClient client) =>
        new(client, NullLogger<FawazExchangeRateProvider>.Instance);

    private static HttpClient CreateClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = BaseAddress };

    private class StubHttpMessageHandler(
        string responseJson,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, JsonMediaType)
            });
        }
    }
}