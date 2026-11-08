using ExchangeRateAggregator.Application.Models;
using ExchangeRateAggregator.Infrastructure.Providers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;

namespace ExchangeRateAggregator.Infrastructure.Tests.Providers;

public class FrankfurterExchangeRateProviderTests
{
    private const string BaseCurrency = "USD";
    private const string LowercaseBaseCurrency = "usd";
    private const string TargetCurrency = "EUR";
    private const decimal ExpectedRate = 0.92m;
    private const string JsonMediaType = "application/json";

    private const string ValidRatesPayload =
        """[{"date":"2026-11-06","base":"USD","quote":"EUR","rate":0.92}]""";

    private static readonly DateOnly RequestedDate = new(2026, 11, 6);
    private static readonly Uri BaseAddress = new("https://api.frankfurter.dev/");

    [Fact]
    public async Task GetRatesAsyncRequestsEcbRatesForTheNormalizedBaseCurrencyAndDate()
    {
        var expectedRequestUri = new Uri(
            BaseAddress, "v2/rates?base=USD&date=2026-11-06&providers=ecb");
        var handler = new StubHttpMessageHandler(ValidRatesPayload);
        using var client = CreateClient(handler);
        var provider = CreateProvider(client);

        await provider.GetRatesAsync(
            LowercaseBaseCurrency, RequestedDate, CancellationToken.None);

        handler.RequestUri.Should().Be(expectedRequestUri);
    }

    [Fact]
    public async Task GetRatesAsyncMapsTheQuoteAndSnapshotMetadata()
    {
        using var client = CreateClient(new StubHttpMessageHandler(ValidRatesPayload));
        var provider = CreateProvider(client);
        var expectedQuote = new ExchangeRateQuote(TargetCurrency, ExpectedRate);

        var providerResult = await provider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        providerResult.Failed.Should().BeFalse();
        providerResult.BaseCurrency.Should().Be(BaseCurrency);
        providerResult.PublishedDate.Should().Be(RequestedDate);
        providerResult.Rates.Should().ContainSingle().Which.Should().Be(expectedQuote);
    }

    [Fact]
    public async Task GetRatesAsyncExcludesTheBaseCurrencyFromTheResults()
    {
        const string payloadWithBaseRate =
            """
            [
                {"date":"2026-11-06","base":"USD","quote":"USD","rate":1},
                {"date":"2026-11-06","base":"USD","quote":"EUR","rate":0.92}
            ]
            """;
        using var client = CreateClient(new StubHttpMessageHandler(payloadWithBaseRate));
        var provider = CreateProvider(client);

        var providerResult = await provider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        providerResult.Failed.Should().BeFalse();
        providerResult.Rates.Should().ContainSingle()
            .Which.TargetCurrency.Should().Be(TargetCurrency);
    }

    [Fact]
    public async Task GetRatesAsyncReturnsFailureWhenTheResponseIsEmpty()
    {
        const string emptyRatesPayload = "[]";
        using var client = CreateClient(new StubHttpMessageHandler(emptyRatesPayload));
        var provider = CreateProvider(client);

        var providerResult = await provider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        AssertFailedResult(providerResult);
    }

    [Fact]
    public async Task GetRatesAsyncReturnsFailureWhenAllRatesAreForADifferentDate()
    {
        const string payloadForAnotherDate =
            """[{"date":"2026-11-05","base":"USD","quote":"EUR","rate":0.92}]""";
        using var client = CreateClient(new StubHttpMessageHandler(payloadForAnotherDate));
        var provider = CreateProvider(client);

        var providerResult = await provider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        AssertFailedResult(providerResult);
    }

    [Fact]
    public async Task GetRatesAsyncReturnsFailureWhenTheResponseContainsAZeroRate()
    {
        const string payloadWithZeroRate =
            """[{"date":"2026-11-06","base":"USD","quote":"EUR","rate":0}]""";
        using var client = CreateClient(new StubHttpMessageHandler(payloadWithZeroRate));
        var provider = CreateProvider(client);

        var providerResult = await provider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        AssertFailedResult(providerResult);
    }

    [Fact]
    public async Task GetRatesAsyncReturnsFailureWhenTheApiReturnsServiceUnavailable()
    {
        const string errorPayload = "{}";
        using var client = CreateClient(
            new StubHttpMessageHandler(errorPayload, HttpStatusCode.ServiceUnavailable));
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

    private static FrankfurterExchangeRateProvider CreateProvider(HttpClient client) =>
        new(client, NullLogger<FrankfurterExchangeRateProvider>.Instance);

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