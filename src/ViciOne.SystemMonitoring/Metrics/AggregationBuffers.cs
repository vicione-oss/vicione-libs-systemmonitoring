namespace ViciOne.SystemMonitoring.Metrics;

/// <summary>
/// Collects the one-minute values per subgroup and returns their 5- and 15-minute aggregates when those intervals are due.
/// </summary>
internal sealed class AggregationBuffers(string group)
{
    private readonly Dictionary<string, List<List<LastMetricValue>>> _fiveMinutes = [];
    private readonly Dictionary<string, List<List<LastMetricValue>>> _fifteenMinutes = [];

    public List<Metric> Add(int interval, IEnumerable<KeyValuePair<string, List<LastMetricValue>>> valuesBySubGroup)
    {
        foreach (var (subGroup, values) in valuesBySubGroup)
        {
            Buffer(_fiveMinutes, subGroup).Add(values);
            Buffer(_fifteenMinutes, subGroup).Add(values);
        }

        List<Metric> metrics = [];
        if (interval % 5 == 0)
            metrics.AddRange(Flush(_fiveMinutes, 5));

        if (interval % 15 == 0)
            metrics.AddRange(Flush(_fifteenMinutes, 15));

        return metrics;
    }

    private static List<List<LastMetricValue>> Buffer(Dictionary<string, List<List<LastMetricValue>>> buffers, string subGroup)
    {
        if (!buffers.TryGetValue(subGroup, out var buffer))
            buffers[subGroup] = buffer = [];

        return buffer;
    }

    private List<Metric> Flush(Dictionary<string, List<List<LastMetricValue>>> buffers, int intervalInMinutes)
    {
        List<Metric> metrics = [.. buffers.SelectMany(b => b.Value.AggregateBuffer().ToMetrics(group, b.Key, intervalInMinutes))];
        buffers.Clear();
        return metrics;
    }
}
