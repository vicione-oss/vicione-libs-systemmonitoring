namespace ViciOne.SystemMonitoring.Metrics;

public sealed class LastMetricValue(string name, object value, string unit) : IMetricValue
{
    public string Name { get; } = name;
    public object Value { get; set; } = value;
    public string Unit { get; } = unit;
}
