namespace ViciOne.SystemMonitoring.Collectors;

public interface IScheduledMetricsCollector : IMetricsCollector
{
    int IntervalInMinutes { get; }
}
