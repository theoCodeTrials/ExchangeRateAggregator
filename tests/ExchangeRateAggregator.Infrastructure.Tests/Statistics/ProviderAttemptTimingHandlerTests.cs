using ExchangeRateAggregator.Infrastructure.Statistics;
using FluentAssertions;
using System.Net;

namespace ExchangeRateAggregator.Infrastructure.Tests.Statistics;

public class ProviderAttemptTimingHandlerTests
{
    private const string ProviderName = "NBP";

    private static readonly Uri RequestUri = new("https://example.test/rates");

    [Fact]
    public async Task SendAsyncReturnsTheInnerResponseAndRecordsOneAttempt()
    {
        var statisticsStore = new InMemoryProviderStatisticsStore(TimeProvider.System);
        using var expectedResponse = new HttpResponseMessage(HttpStatusCode.OK);
        using var invoker = CreateInvoker(
            statisticsStore, () => Task.FromResult(expectedResponse));
        using var request = new HttpRequestMessage(HttpMethod.Get, RequestUri);

        var response = await invoker.SendAsync(request, CancellationToken.None);

        response.Should().BeSameAs(expectedResponse);
        AssertSingleRecordedAttempt(statisticsStore);
    }

    [Fact]
    public async Task SendAsyncPropagatesTheInnerExceptionAndRecordsOneAttempt()
    {
        var statisticsStore = new InMemoryProviderStatisticsStore(TimeProvider.System);
        var expectedException = new HttpRequestException("The provider connection failed.");
        using var invoker = CreateInvoker(
            statisticsStore, () => Task.FromException<HttpResponseMessage>(expectedException));
        using var request = new HttpRequestMessage(HttpMethod.Get, RequestUri);

        Func<Task> sendRequest = () => invoker.SendAsync(request, CancellationToken.None);

        var exception = await sendRequest.Should().ThrowAsync<HttpRequestException>();
        exception.Which.Should().BeSameAs(expectedException);
        AssertSingleRecordedAttempt(statisticsStore);
    }

    private static HttpMessageInvoker CreateInvoker(
        InMemoryProviderStatisticsStore statisticsStore,
        Func<Task<HttpResponseMessage>> sendResponse)
    {
        var timingHandler = new ProviderAttemptTimingHandler(ProviderName, statisticsStore)
        {
            InnerHandler = new StubHttpMessageHandler(sendResponse)
        };

        return new HttpMessageInvoker(timingHandler);
    }

    private static void AssertSingleRecordedAttempt(
        InMemoryProviderStatisticsStore statisticsStore)
    {
        var statistics = statisticsStore.GetSnapshot().Should().ContainSingle().Which;

        statistics.ProviderName.Should().Be(ProviderName);
        statistics.TotalRequests.Should().Be(1);
    }

    private class StubHttpMessageHandler(
        Func<Task<HttpResponseMessage>> sendResponse) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return sendResponse();
        }
    }
}