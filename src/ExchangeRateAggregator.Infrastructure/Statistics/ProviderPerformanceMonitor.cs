using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ExchangeRateAggregator.Infrastructure.Statistics;

public class ProviderPerformanceMonitor : BackgroundService
{
    private const double AnomalyThresholdMultiplier = 1.5;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(0.5);

    private readonly InMemoryProviderStatisticsStore _statisticsStore;
    private readonly ILogger<ProviderPerformanceMonitor> _logger;

    public ProviderPerformanceMonitor(
        InMemoryProviderStatisticsStore statisticsStore,
        ILogger<ProviderPerformanceMonitor> logger)
    {
        _statisticsStore = statisticsStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            CheckProviderPerformance();
        }
    }

    internal void CheckProviderPerformance()
    {
        foreach (var statistics in _statisticsStore.GetSnapshot())
        {
            if (statistics.AverageResponseTimeInLastFiveMinutesMilliseconds
                    is not { } recentAverageMilliseconds ||
                recentAverageMilliseconds <=
                    statistics.AverageResponseTimeMilliseconds * AnomalyThresholdMultiplier)
            {
                continue;
            }

            _logger.LogWarning(
                "Provider {ProviderName} averaged {RecentAverageMs:F1} ms over the last five minutes " +
                "across {RecentRequests} attempts. Its overall average is {OverallAverageMs:F1} ms.",
                statistics.ProviderName,
                recentAverageMilliseconds,
                statistics.RequestsInLastFiveMinutes,
                statistics.AverageResponseTimeMilliseconds);
        }
    }
}