using ExchangeRateAggregator.Api.Models;
using ExchangeRateAggregator.Application.Models;
using ExchangeRateAggregator.Application.Providers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;

namespace ExchangeRateAggregator.Api.Tests;

public class ExchangeRatesEndpointTests
{
    private const string RatesPath = "/api/exchange-rates";
    private const string HistoricalDateQueryValue = "2024-03-06";
    private const string BaseCurrency = "EUR";
    private const string NbpProviderName = "NBP";
    private const string FrankfurterProviderName = "Frankfurter";

    private static readonly DateOnly HistoricalDate = new(2024, 3, 6);

    [Fact]
    public async Task GetRatesReturnsFilteredQuotesAndReportsPartialProviderFailure()
    {
        var nbpProvider = CreateSuccessfulProvider(
            NbpProviderName,
            new ExchangeRateQuote("GBP", 0.87m),
            new ExchangeRateQuote("USD", 1.08m));
        var frankfurterProvider = CreateFailedProvider(FrankfurterProviderName);
        using var factory = CreateFactory(nbpProvider, frankfurterProvider);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(
            $"{RatesPath}?baseCurrency=eur&date={HistoricalDateQueryValue}&targetCurrency=usd");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await ReadRatesResponseAsync(response);
        payload.BaseCurrency.Should().Be(BaseCurrency);
        payload.RequestedDate.Should().Be(HistoricalDate);
        payload.FailedProviders.Should().Equal(FrankfurterProviderName);
        payload.AllProvidersFailed.Should().BeFalse();

        var provider = payload.Providers.Should().ContainSingle().Which;
        provider.ProviderName.Should().Be(NbpProviderName);
        provider.Rates.Should().ContainSingle().Which.Should().Be(
            new ExchangeRateQuoteResponse("USD", 1.08m));
    }

    [Fact]
    public async Task GetRatesSortsQuotesByTargetCurrencyInDescendingOrder()
    {
        var provider = CreateSuccessfulProvider(
            NbpProviderName,
            new ExchangeRateQuote("GBP", 0.87m),
            new ExchangeRateQuote("AUD", 1.65m),
            new ExchangeRateQuote("USD", 1.08m));
        using var factory = CreateFactory(provider);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(
            $"{RatesPath}?baseCurrency=EUR&date={HistoricalDateQueryValue}&sortDirection=DESC");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await ReadRatesResponseAsync(response);
        payload.FailedProviders.Should().BeEmpty();
        payload.Providers.Should().ContainSingle().Which.Rates.Should().Equal(
            new ExchangeRateQuoteResponse("USD", 1.08m),
            new ExchangeRateQuoteResponse("GBP", 0.87m),
            new ExchangeRateQuoteResponse("AUD", 1.65m));
    }

    [Fact]
    public async Task GetRatesReturnsBadRequestWhenTheDateCannotBeBound()
    {
        var provider = CreateProvider(NbpProviderName);
        using var factory = CreateFactory(provider);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(
            $"{RatesPath}?baseCurrency=EUR&date=invalid");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Errors.Should().ContainKey("date");
        AssertProviderWasNotCalled(provider);
    }

    [Fact]
    public async Task GetRatesReturnsAllApplicationValidationErrorsInOneResponse()
    {
        var provider = CreateProvider(NbpProviderName);
        using var factory = CreateFactory(provider);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(
            $"{RatesPath}?baseCurrency=EYT&date=9999-12-31&sortDirection=descending");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Errors.Keys.Should().BeEquivalentTo(
            new[] { "baseCurrency", "date", "sortDirection" });
        AssertProviderWasNotCalled(provider);
    }

    [Fact]
    public async Task GetRatesReturnsServiceUnavailableWhenAllProvidersFail()
    {
        using var factory = CreateFactory(
            CreateFailedProvider(NbpProviderName),
            CreateFailedProvider(FrankfurterProviderName));
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(
            $"{RatesPath}?baseCurrency=EUR&date={HistoricalDateQueryValue}");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var payload = await ReadRatesResponseAsync(response);
        payload.Providers.Should().BeEmpty();
        payload.FailedProviders.Should().BeEquivalentTo(
            new[] { NbpProviderName, FrankfurterProviderName });
        payload.AllProvidersFailed.Should().BeTrue();
    }

    [Theory]
    [InlineData("targetCurrency", "")]
    [InlineData("targetCurrency", "%20")]
    [InlineData("sortDirection", "")]
    [InlineData("sortDirection", "%20")]
    public async Task GetRatesReturnsBadRequestWhenAnOptionalQueryParameterIsBlank(
        string parameterName,
        string encodedValue)
    {
        var provider = CreateSuccessfulProvider(
            NbpProviderName,
            new ExchangeRateQuote("USD", 1.08m));
        using var factory = CreateFactory(provider);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(
            $"{RatesPath}?baseCurrency={BaseCurrency}&date={HistoricalDateQueryValue}" +
            $"&{parameterName}={encodedValue}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Errors.Keys.Should().Equal(parameterName);
        AssertProviderWasNotCalled(provider);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        params IExchangeRateProvider[] providers)
    {
        return new ExchangeRatesApiFactory(providers);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }

    private static async Task<ExchangeRatesResponse> ReadRatesResponseAsync(
        HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<ExchangeRatesResponse>();

        payload.Should().NotBeNull();

        return payload!;
    }

    private static IExchangeRateProvider CreateSuccessfulProvider(
        string providerName,
        params ExchangeRateQuote[] quotes)
    {
        return CreateProviderReturning(
            providerName,
            new ProviderRatesResult(
                Failed: false,
                BaseCurrency: BaseCurrency,
                PublishedDate: HistoricalDate,
                Rates: quotes));
    }

    private static IExchangeRateProvider CreateFailedProvider(string providerName)
    {
        return CreateProviderReturning(
            providerName,
            new ProviderRatesResult(
                Failed: true,
                BaseCurrency: BaseCurrency,
                PublishedDate: null,
                Rates: Array.Empty<ExchangeRateQuote>()));
    }

    private static IExchangeRateProvider CreateProviderReturning(
        string providerName,
        ProviderRatesResult result)
    {
        var provider = CreateProvider(providerName);
        provider.GetRatesAsync(BaseCurrency, HistoricalDate, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));

        return provider;
    }

    private static IExchangeRateProvider CreateProvider(string providerName)
    {
        var provider = Substitute.For<IExchangeRateProvider>();
        provider.Name.Returns(providerName);

        return provider;
    }

    private static void AssertProviderWasNotCalled(IExchangeRateProvider provider)
    {
        _ = provider.DidNotReceive().GetRatesAsync(
            Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    private class ExchangeRatesApiFactory(
        IExchangeRateProvider[] providers) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(
            Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IExchangeRateProvider>();

                foreach (var provider in providers)
                {
                    services.AddSingleton<IExchangeRateProvider>(provider);
                }
            });
        }
    }
}