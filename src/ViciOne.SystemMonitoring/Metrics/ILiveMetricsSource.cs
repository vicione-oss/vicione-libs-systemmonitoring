using ViciOne.SystemMonitoring.Collectors;

namespace ViciOne.SystemMonitoring.Metrics;

internal interface ILiveMetricsSource
{
    Task<List<Metric>> CollectLiveMetrics(LiveWindow window, CancellationToken cancellationToken);
}
