namespace ExchangeRateAggregator.Infrastructure.Statistics;

public class InMemoryProviderStatisticsStore
{
    private const double FastThresholdMilliseconds = 100;
    private const double AverageThresholdMilliseconds = 200;
    private static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(5);
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, ProviderMeasurements> _measurementsByProvider =
        new(StringComparer.Ordinal);

    public InMemoryProviderStatisticsStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public void RecordAttempt(string providerName, TimeSpan elapsed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);

        lock (_gate)
        {
            var completedAt = _timeProvider.GetTimestamp();

            if (!_measurementsByProvider.TryGetValue(providerName, out var measurements))
            {
                measurements = new ProviderMeasurements();
                _measurementsByProvider.Add(providerName, measurements);
            }

            RemoveExpiredAttempts(measurements, completedAt);
            AddAttempt(measurements, completedAt, elapsed.TotalMilliseconds);
        }
    }

    public IReadOnlyList<ProviderRequestStatistics> GetSnapshot()
    {
        lock (_gate)
        {
            var now = _timeProvider.GetTimestamp();
            var snapshot = new List<ProviderRequestStatistics>(
                _measurementsByProvider.Count);

            foreach (var (providerName, measurements) in _measurementsByProvider)
            {
                RemoveExpiredAttempts(measurements, now);
                snapshot.Add(CreateSnapshot(providerName, measurements));
            }

            return snapshot.AsReadOnly();
        }
    }

    private static void AddAttempt(
        ProviderMeasurements measurements,
        long completedAt,
        double elapsedMilliseconds)
    {
        measurements.TotalRequests++;
        measurements.TotalMilliseconds += elapsedMilliseconds;

        if (elapsedMilliseconds < FastThresholdMilliseconds)
        {
            measurements.FastRequests++;
        }
        else if (elapsedMilliseconds <= AverageThresholdMilliseconds)
        {
            measurements.AverageRequests++;
        }
        else
        {
            measurements.SlowRequests++;
        }

        measurements.RecentAttempts.Enqueue(new CompletedAttempt(completedAt, elapsedMilliseconds));
        measurements.RecentMilliseconds += elapsedMilliseconds;
    }

    private void RemoveExpiredAttempts(ProviderMeasurements measurements, long now)
    {
        while (measurements.RecentAttempts.TryPeek(out var oldestAttempt) &&
               _timeProvider.GetElapsedTime(oldestAttempt.CompletedAt, now) >= RecentWindow)
        {
            measurements.RecentAttempts.Dequeue();
            measurements.RecentMilliseconds -= oldestAttempt.ElapsedMilliseconds;
        }

        if (measurements.RecentAttempts.Count == 0)
        {
            measurements.RecentMilliseconds = 0;
        }
    }

    private static ProviderRequestStatistics CreateSnapshot(
        string providerName,
        ProviderMeasurements measurements)
    {
        var recentRequestCount = measurements.RecentAttempts.Count;

        return new ProviderRequestStatistics(
            providerName,
            measurements.TotalRequests,
            measurements.TotalMilliseconds / measurements.TotalRequests,
            measurements.FastRequests,
            measurements.AverageRequests,
            measurements.SlowRequests,
            recentRequestCount,
            recentRequestCount == 0
                ? null
                : measurements.RecentMilliseconds / recentRequestCount);
    }

    private class ProviderMeasurements
    {
        public long TotalRequests;
        public double TotalMilliseconds;
        public long FastRequests;
        public long AverageRequests;
        public long SlowRequests;
        public double RecentMilliseconds;

        public Queue<CompletedAttempt> RecentAttempts { get; } = new();
    }

    private readonly record struct CompletedAttempt(
        long CompletedAt,
        double ElapsedMilliseconds);
}