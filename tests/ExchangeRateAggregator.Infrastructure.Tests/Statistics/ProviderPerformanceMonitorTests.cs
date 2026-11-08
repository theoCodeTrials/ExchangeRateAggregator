using ExchangeRateAggregator.Infrastructure.Statistics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ExchangeRateAggregator.Infrastructure.Tests.Statistics;

public class ProviderPerformanceMonitorTests
{
    private const string ProviderName = "NBP";

    [Theory]
    [InlineData(299, 0)]
    [InlineData(300, 0)]
    [InlineData(301, 1)]
    public void CheckProviderPerformanceWarnsOnlyWhenRecentAverageExceedsTheThreshold(
        int recentDurationMilliseconds,
        int expectedWarningCount)
    {
        var clock = new TestTimeProvider();
        var statisticsStore = new InMemoryProviderStatisticsStore(clock);
        var logger = Substitute.For<ILogger<ProviderPerformanceMonitor>>();
        using var monitor = new ProviderPerformanceMonitor(statisticsStore, logger);

        statisticsStore.RecordAttempt(ProviderName, TimeSpan.FromMilliseconds(100));
        clock.Advance(TimeSpan.FromMinutes(5));
        statisticsStore.RecordAttempt(
            ProviderName, TimeSpan.FromMilliseconds(recentDurationMilliseconds));

        monitor.CheckProviderPerformance();

        var warnings = logger.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log) &&
                           call.GetArguments()[0] is LogLevel.Warning)
            .ToArray();

        warnings.Should().HaveCount(expectedWarningCount);
        foreach (var warning in warnings)
        {
            warning.GetArguments()[2]!.ToString().Should().Contain(ProviderName);
        }
    }

    [Fact]
    public void CheckProviderPerformanceSkipsProvidersWithoutRecentAttempts()
    {
        var clock = new TestTimeProvider();
        var statisticsStore = new InMemoryProviderStatisticsStore(clock);
        var logger = Substitute.For<ILogger<ProviderPerformanceMonitor>>();
        using var monitor = new ProviderPerformanceMonitor(statisticsStore, logger);

        statisticsStore.RecordAttempt(ProviderName, TimeSpan.FromMilliseconds(100));
        clock.Advance(TimeSpan.FromMinutes(5));

        monitor.CheckProviderPerformance();

        logger.ReceivedCalls().Should().BeEmpty();
    }

    private class TestTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan elapsed)
        {
            _timestamp += elapsed.Ticks;
        }
    }
}