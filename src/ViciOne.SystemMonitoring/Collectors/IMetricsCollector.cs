using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring.Collectors;

/// <summary>
/// Interface for metrics collectors, defining methods for collecting and processing metrics.
/// </summary>
public interface IMetricsCollector
{
    string Name { get; }

    /// <summary>
    /// Collects metrics at a given interval (1, 5, or 15 minutes).
    /// </summary>
    /// <param name="interval">The collection interval: 1, 5, or 15 minutes.</param>
    /// <returns>A list of topic-payload pairs to be published.</returns>
    Task<List<Metric>> CollectMetrics(int interval, DateTimeOffset cycleStartTime, CancellationToken cancellationToken);
}
