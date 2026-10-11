namespace ViciOne.SystemMonitoring.Metrics;

internal interface ILiveMetricsSource
{
    Task<List<Metric>> CollectLiveMetrics(CancellationToken cancellationToken);
}
