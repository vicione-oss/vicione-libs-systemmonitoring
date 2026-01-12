namespace ViciOne.SystemMonitoring.Metrics;

public interface IMetricsManager
{
    Task Initialize(CancellationToken cancellationToken);
    Task<List<Metric>> CollectAllMetrics(int currentInterval, DateTimeOffset cycleStartTime, CancellationToken cancellationToken);
}
