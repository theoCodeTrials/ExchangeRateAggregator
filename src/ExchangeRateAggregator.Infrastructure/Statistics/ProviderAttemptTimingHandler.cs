using System.Diagnostics;

namespace ExchangeRateAggregator.Infrastructure.Statistics;

public class ProviderAttemptTimingHandler : DelegatingHandler
{
    private readonly string _providerName;
    private readonly InMemoryProviderStatisticsStore _statisticsStore;

    public ProviderAttemptTimingHandler(
        string providerName,
        InMemoryProviderStatisticsStore statisticsStore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(statisticsStore);

        _providerName = providerName;
        _statisticsStore = statisticsStore;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        finally
        {
            _statisticsStore.RecordAttempt(_providerName, Stopwatch.GetElapsedTime(startedAt));
        }
    }
}