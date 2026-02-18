namespace ViciOne.SystemMonitoring.Metrics;

public interface IMetricValue
{
    string Name { get; }
    string Unit { get; }
}
