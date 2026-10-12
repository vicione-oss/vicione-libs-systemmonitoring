using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring.Collectors;

/// <summary>
/// A collector that can also be read between its regular intervals without changing what those intervals report.
/// </summary>
/// <remarks>
/// Live metrics have no interval (<see cref="Metric.IntervalInMinutes"/> is 0) and are never aggregated. Values computed from
/// counters, such as CPU time, cover the time since the previous live call.
/// </remarks>
public interface ILiveMetricsCollector : IMetricsCollector
{
    Task<List<Metric>> CollectLiveMetrics(CancellationToken cancellationToken);
}
