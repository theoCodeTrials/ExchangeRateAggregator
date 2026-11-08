using ExchangeRateAggregator.Application.Models;
using ExchangeRateAggregator.Infrastructure.Providers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;

namespace ExchangeRateAggregator.Infrastructure.Tests.Providers;

public class NbpExchangeRateProviderTests
{
    private const string BaseCurrency = "EUR";
    private const string UsDollar = "USD";
    private const string PolishZloty = "PLN";
    private const string JsonMediaType = "application/json";
    private const string ErrorPayload = "[]";

    private const string ValidTablePayload =
        """
        [
            {
                "effectiveDate": "2026-11-06",
                "rates": [
                    {"code": "EUR", "mid": 4.20},
                    {"code": "USD", "mid": 4.00}
                ]
            }
        ]
        """;

    private static readonly DateOnly RequestedDate = new(2026, 11, 6);
    private static readonly Uri BaseAddress = new("https://api.nbp.pl/");

    [Fact]
    public async Task GetRatesAsyncRequestsTableAForTheSpecifiedDate()
    {
        var expectedRequestUri = new Uri(
            BaseAddress, "api/exchangerates/tables/A/2026-11-06/?format=json");
        var handler = new StubHttpMessageHandler(ValidTablePayload);
        using var client = CreateClient(handler);
        var provider = CreateProvider(client);

        await provider.GetRatesAsync(BaseCurrency, RequestedDate, CancellationToken.None);

        handler.RequestUri.Should().Be(expectedRequestUri);
    }

    [Fact]
    public async Task GetRatesAsyncReturnsRebasedQuotesAndSnapshotMetadata()
    {
        using var client = CreateClient(new StubHttpMessageHandler(ValidTablePayload));
        var provider = CreateProvider(client);
        var expectedQuotes = new[]
        {
            new ExchangeRateQuote(UsDollar, 1.05m),
            new ExchangeRateQuote(PolishZloty, 4.20m)
        };

        var providerResult = await provider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        providerResult.Failed.Should().BeFalse();
        providerResult.BaseCurrency.Should().Be(BaseCurrency);
        providerResult.PublishedDate.Should().Be(RequestedDate);
        providerResult.Rates.Should().BeEquivalentTo(expectedQuotes);
    }

    [Fact]
    public async Task GetRatesAsyncReturnsFailureWhenTheDatedTableIsNotFound()
    {
        using var client = CreateClient(
            new StubHttpMessageHandler(ErrorPayload, HttpStatusCode.NotFound));
        var provider = CreateProvider(client);

        var providerResult = await provider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        AssertFailedResult(providerResult, BaseCurrency);
    }

    [Fact]
    public async Task GetRatesAsyncReturnsFailureWhenTheTableDateDiffersFromTheRequestedDate()
    {
        var differentRequestedDate = RequestedDate.AddDays(-1);
        using var client = CreateClient(new StubHttpMessageHandler(ValidTablePayload));
        var provider = CreateProvider(client);

        var providerResult = await provider.GetRatesAsync(
            BaseCurrency, differentRequestedDate, CancellationToken.None);

        AssertFailedResult(providerResult, BaseCurrency);
    }

    [Fact]
    public async Task GetRatesAsyncReturnsFailureWhenTheTableDoesNotContainTheBaseCurrency()
    {
        const string missingBaseCurrency = "GBP";
        using var client = CreateClient(new StubHttpMessageHandler(ValidTablePayload));
        var provider = CreateProvider(client);

        var providerResult = await provider.GetRatesAsync(
            missingBaseCurrency, RequestedDate, CancellationToken.None);

        AssertFailedResult(providerResult, missingBaseCurrency);
    }

    [Fact]
    public async Task GetRatesAsyncReturnsFailureWhenTheTableContainsAZeroRate()
    {
        const string tableWithZeroRate =
            """
            [
                {
                    "effectiveDate": "2026-11-06",
                    "rates": [
                        {"code": "EUR", "mid": 4.20},
                        {"code": "USD", "mid": 0}
                    ]
                }
            ]
            """;
        using var client = CreateClient(new StubHttpMessageHandler(tableWithZeroRate));
        var provider = CreateProvider(client);

        var providerResult = await provider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        AssertFailedResult(providerResult, BaseCurrency);
    }

    [Fact]
    public async Task GetRatesAsyncReturnsFailureWhenTheApiReturnsServiceUnavailable()
    {
        using var client = CreateClient(
            new StubHttpMessageHandler(ErrorPayload, HttpStatusCode.ServiceUnavailable));
        var provider = CreateProvider(client);

        var providerResult = await provider.GetRatesAsync(
            BaseCurrency, RequestedDate, CancellationToken.None);

        AssertFailedResult(providerResult, BaseCurrency);
    }

    private static void AssertFailedResult(
        ProviderRatesResult providerResult,
        string expectedBaseCurrency)
    {
        providerResult.Failed.Should().BeTrue();
        providerResult.BaseCurrency.Should().Be(expectedBaseCurrency);
        providerResult.PublishedDate.Should().BeNull();
        providerResult.Rates.Should().BeEmpty();
    }

    private static NbpExchangeRateProvider CreateProvider(HttpClient client) =>
        new(client, NullLogger<NbpExchangeRateProvider>.Instance);

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