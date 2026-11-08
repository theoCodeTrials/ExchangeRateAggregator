using ExchangeRateAggregator.Infrastructure.Statistics;
using FluentAssertions;

namespace ExchangeRateAggregator.Infrastructure.Tests.Statistics;

public class InMemoryProviderStatisticsStoreTests
{
    private const string Nbp = "NBP";
    private const string Frankfurter = "Frankfurter";

    [Fact]
    public void GetSnapshotCountsAttemptsAcrossLatencyBucketBoundaries()
    {
        var store = new InMemoryProviderStatisticsStore(new TestTimeProvider());

        store.RecordAttempt(Nbp, TimeSpan.FromMilliseconds(99));
        store.RecordAttempt(Nbp, TimeSpan.FromMilliseconds(100));
        store.RecordAttempt(Nbp, TimeSpan.FromMilliseconds(200));
        store.RecordAttempt(Nbp, TimeSpan.FromMilliseconds(201));

        var statistics = store.GetSnapshot().Should().ContainSingle().Which;

        statistics.TotalRequests.Should().Be(4);
        statistics.AverageResponseTimeMilliseconds.Should().Be(150);
        statistics.FastRequests.Should().Be(1);
        statistics.AverageRequests.Should().Be(2);
        statistics.SlowRequests.Should().Be(1);
    }

    [Fact]
    public void GetSnapshotExpiresSamplesAtFiveMinutesAndPreservesLifetimeStatistics()
    {
        var clock = new TestTimeProvider();
        var store = new InMemoryProviderStatisticsStore(clock);
        store.RecordAttempt(Nbp, TimeSpan.FromMilliseconds(100));

        clock.Advance(TimeSpan.FromMinutes(4));
        store.RecordAttempt(Nbp, TimeSpan.FromMilliseconds(300));
        var beforeExpiry = store.GetSnapshot().Should().ContainSingle().Which;

        clock.Advance(TimeSpan.FromMinutes(1));
        var afterFirstExpiry = store.GetSnapshot().Should().ContainSingle().Which;

        clock.Advance(TimeSpan.FromMinutes(4));
        var afterAllExpire = store.GetSnapshot().Should().ContainSingle().Which;

        beforeExpiry.RequestsInLastFiveMinutes.Should().Be(2);
        beforeExpiry.AverageResponseTimeInLastFiveMinutesMilliseconds.Should().Be(200);

        afterFirstExpiry.RequestsInLastFiveMinutes.Should().Be(1);
        afterFirstExpiry.AverageResponseTimeInLastFiveMinutesMilliseconds.Should().Be(300);

        afterAllExpire.RequestsInLastFiveMinutes.Should().Be(0);
        afterAllExpire.AverageResponseTimeInLastFiveMinutesMilliseconds.Should().BeNull();
        afterAllExpire.TotalRequests.Should().Be(2);
        afterAllExpire.AverageResponseTimeMilliseconds.Should().Be(200);
        afterAllExpire.AverageRequests.Should().Be(1);
        afterAllExpire.SlowRequests.Should().Be(1);
    }

    [Fact]
    public void GetSnapshotKeepsMeasurementsSeparateForEachProvider()
    {
        var store = new InMemoryProviderStatisticsStore(new TestTimeProvider());
        store.RecordAttempt(Nbp, TimeSpan.FromMilliseconds(80));
        store.RecordAttempt(Nbp, TimeSpan.FromMilliseconds(100));
        store.RecordAttempt(Frankfurter, TimeSpan.FromMilliseconds(300));

        var statisticsByProvider = store.GetSnapshot()
            .ToDictionary(statistics => statistics.ProviderName);

        statisticsByProvider.Should().HaveCount(2);
        statisticsByProvider[Nbp].TotalRequests.Should().Be(2);
        statisticsByProvider[Nbp].AverageResponseTimeMilliseconds.Should().Be(90);
        statisticsByProvider[Frankfurter].TotalRequests.Should().Be(1);
        statisticsByProvider[Frankfurter].AverageResponseTimeMilliseconds.Should().Be(300);
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