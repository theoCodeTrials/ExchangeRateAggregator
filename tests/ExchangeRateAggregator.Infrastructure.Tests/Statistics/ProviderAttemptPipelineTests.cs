using ExchangeRateAggregator.Infrastructure.Bootstrapping;
using ExchangeRateAggregator.Infrastructure.Providers;
using ExchangeRateAggregator.Infrastructure.Statistics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;

namespace ExchangeRateAggregator.Infrastructure.Tests.Statistics;

public class ProviderAttemptPipelineTests
{
    private static readonly Uri StubBaseAddress = new("https://example.test/");
    private static readonly DateOnly RequestedDate = new(2026, 11, 6);

    [Fact]
    public async Task GetRatesAsyncRecordsBothAttemptsWhenARetrySucceeds()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddExchangeRateProviderClients(
            StubBaseAddress,
            StubBaseAddress,
            StubBaseAddress,
            TimeSpan.FromSeconds(5),
            clientBuilder =>
            {
                clientBuilder.AddExchangeRateRetry(
                    maxRetryAttempts: 1, retryDelay: TimeSpan.Zero);
                clientBuilder.ConfigurePrimaryHttpMessageHandler(
                    () => new RetryThenSuccessHandler());
            });

        using var serviceProvider = services.BuildServiceProvider();
        var nbpProvider = serviceProvider.GetRequiredService<NbpExchangeRateProvider>();
        var statisticsStore =
            serviceProvider.GetRequiredService<InMemoryProviderStatisticsStore>();

        var result = await nbpProvider.GetRatesAsync(
            "EUR", RequestedDate, CancellationToken.None);

        result.Failed.Should().BeFalse();
        result.PublishedDate.Should().Be(RequestedDate);
        result.Rates.Should().NotBeEmpty();

        var statistics = statisticsStore.GetSnapshot().Should().ContainSingle().Which;
        statistics.ProviderName.Should().Be(nbpProvider.Name);
        statistics.TotalRequests.Should().Be(2);
    }

    [Fact]
    public async Task GetRatesAsyncReturnsFailedResultWhenRebasingOverflows()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddExchangeRateProviderClients(
            StubBaseAddress,
            StubBaseAddress,
            StubBaseAddress,
            TimeSpan.FromSeconds(5),
            clientBuilder => clientBuilder.ConfigurePrimaryHttpMessageHandler(
                () => new OverflowingNbpTableHandler()));

        using var serviceProvider = services.BuildServiceProvider();
        var nbpProvider = serviceProvider.GetRequiredService<NbpExchangeRateProvider>();

        var result = await nbpProvider.GetRatesAsync(
            "USD", RequestedDate, CancellationToken.None);

        result.Failed.Should().BeTrue();
        result.BaseCurrency.Should().Be("USD");
        result.PublishedDate.Should().BeNull();
        result.Rates.Should().BeEmpty();
    }

    private class OverflowingNbpTableHandler : HttpMessageHandler
    {
        private const string TablePayload = """
        [
          {
            "effectiveDate": "2026-11-06",
            "rates": [
              { "code": "USD", "mid": 8 },
              { "code": "AUD", "mid": 0.0000000000000000000000000001 }
            ]
          }
        ]
        """;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(TablePayload, Encoding.UTF8, "application/json")
            });
        }
    }

    private class RetryThenSuccessHandler : HttpMessageHandler
    {
        private const string JsonMediaType = "application/json";
        private const string TablePayload =
            """[{"effectiveDate":"2026-11-06","rates":[{"code":"EUR","mid":4.20}]}]""";

        private int _attemptCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attemptNumber = Interlocked.Increment(ref _attemptCount);

            return Task.FromResult(attemptNumber switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                2 => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(TablePayload, Encoding.UTF8, JsonMediaType)
                },
                _ => throw new InvalidOperationException(
                    $"Unexpected HTTP attempt {attemptNumber}.")
            });
        }
    }
}