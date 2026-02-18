using System.Text.Json.Serialization;

namespace ViciOne.SystemMonitoring.Metrics;

public sealed class AggregatedMetricValue(string metric, string unit, double min, double avg, double max) : IMetricValue
{
    [JsonIgnore]
    public string Name { get; } = metric;
    [JsonIgnore]
    public string Unit { get; } = unit;

    public double Min { get; set; } = min;
    public double Avg { get; set; } = avg;
    public double Max { get; set; } = max;
}
