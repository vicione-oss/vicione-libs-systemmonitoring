namespace ViciOne.SystemMonitoring.Metrics;

public sealed class Metric
{
    public required string Name { get; init; }
    public required string Group { get; init; }
    public string? SubGroup { get; init; }
    public required int IntervalInMinutes { get; init; }
    public required object Value { get; init; }
    public required string Unit { get; init; }
}
