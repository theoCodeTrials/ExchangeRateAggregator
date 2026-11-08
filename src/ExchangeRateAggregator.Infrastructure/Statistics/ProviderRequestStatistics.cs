namespace ExchangeRateAggregator.Infrastructure.Statistics;

public record ProviderRequestStatistics(
    string ProviderName,
    long TotalRequests,
    double AverageResponseTimeMilliseconds,
    long FastRequests,
    long AverageRequests,
    long SlowRequests,
    int RequestsInLastFiveMinutes,
    double? AverageResponseTimeInLastFiveMinutesMilliseconds);