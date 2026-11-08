using ExchangeRateAggregator.Infrastructure.Statistics;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Json;

namespace ExchangeRateAggregator.Api.Tests.Controllers;

public class ExchangeRateStatisticsEndpointTests
{
    private const string StatisticsPath = "/api/exchange-rates/statistics";
    private const string NbpProviderName = "NBP";
    private const string FrankfurterProviderName = "Frankfurter";

    [Fact]
    public async Task GetStatisticsReturnsOkWithAnEmptyArrayBeforeAnyAttempts()
    {
        var statisticsStore = new InMemoryProviderStatisticsStore(TimeProvider.System);
        using var factory = CreateFactory(statisticsStore);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(StatisticsPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var statistics = await ReadStatisticsAsync(response);
        statistics.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStatisticsReturnsCountsAndLatencyStatisticsForEachProvider()
    {
        var statisticsStore = new InMemoryProviderStatisticsStore(TimeProvider.System);
        statisticsStore.RecordAttempt(NbpProviderName, TimeSpan.FromMilliseconds(99));
        statisticsStore.RecordAttempt(NbpProviderName, TimeSpan.FromMilliseconds(100));
        statisticsStore.RecordAttempt(NbpProviderName, TimeSpan.FromMilliseconds(200));
        statisticsStore.RecordAttempt(NbpProviderName, TimeSpan.FromMilliseconds(201));
        statisticsStore.RecordAttempt(FrankfurterProviderName, TimeSpan.FromMilliseconds(250));
        using var factory = CreateFactory(statisticsStore);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(StatisticsPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var statistics = await ReadStatisticsAsync(response);

        statistics.Should().BeEquivalentTo(new[]
        {
            new ProviderRequestStatistics(
                ProviderName: NbpProviderName,
                TotalRequests: 4,
                AverageResponseTimeMilliseconds: 150,
                FastRequests: 1,
                AverageRequests: 2,
                SlowRequests: 1,
                RequestsInLastFiveMinutes: 4,
                AverageResponseTimeInLastFiveMinutesMilliseconds: 150),
            new ProviderRequestStatistics(
                ProviderName: FrankfurterProviderName,
                TotalRequests: 1,
                AverageResponseTimeMilliseconds: 250,
                FastRequests: 0,
                AverageRequests: 0,
                SlowRequests: 1,
                RequestsInLastFiveMinutes: 1,
                AverageResponseTimeInLastFiveMinutesMilliseconds: 250)
        });
    }

    private static WebApplicationFactory<Program> CreateFactory(
        InMemoryProviderStatisticsStore statisticsStore)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<InMemoryProviderStatisticsStore>();
                services.AddSingleton(statisticsStore);
            });
        });
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }

    private static async Task<ProviderRequestStatistics[]> ReadStatisticsAsync(
        HttpResponseMessage response)
    {
        var statistics =
            await response.Content.ReadFromJsonAsync<ProviderRequestStatistics[]>();

        statistics.Should().NotBeNull();

        return statistics!;
    }
}