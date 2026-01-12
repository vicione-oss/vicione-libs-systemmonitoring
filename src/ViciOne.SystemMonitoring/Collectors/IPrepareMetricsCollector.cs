namespace ViciOne.SystemMonitoring.Collectors;

public interface IPrepareMetricsCollector
{
    Task PrepareMetrics(CancellationToken cancellationToken);
}
