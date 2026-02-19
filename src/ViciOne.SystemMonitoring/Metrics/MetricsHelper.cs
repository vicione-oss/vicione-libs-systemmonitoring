namespace ViciOne.SystemMonitoring.Metrics;

public static class MetricsHelper
{
    /// <summary>
    /// Aggregates the buffer for a given number of minutes (e.g. 5 or 15 minutes).
    /// The buffer is cleared after aggregation.
    /// </summary>
    /// <param name="buffer">The buffer containing metrics (list of lists with MetricsData).</param>
    /// <returns>A list of aggregated metric values.</returns>
    public static IEnumerable<AggregatedMetricValue> AggregateBuffer<T>(this IEnumerable<T> buffer)
        where T : IEnumerable<LastMetricValue>
    {
        return buffer
            .SelectMany(e => e)
            .GroupBy(e => (e.Name, e.Unit))
            .Select(Calculate);

        static AggregatedMetricValue Calculate(IGrouping<(string Name, string unit), LastMetricValue> values)
        {
            var min = 0d;
            var max = 0d;
            var sum = 0d;
            var count = 0;
            var first = true;

            foreach (var value in values)
            {
                if (value.Value is not double v)
                    throw new InvalidOperationException($"Invalid metric value '{value.Name}'.");

                if (first)
                {
                    min = v;
                    max = v;
                    first = false;
                }
                else
                {
                    min = Math.Min(min, v);
                    max = Math.Max(max, v);
                }

                sum += v;
                count++;
            }

            return new(values.Key.Name, values.Key.unit, min, sum / count, max);
        }
    }

    public static IEnumerable<Metric> ToMetrics<T>(this IEnumerable<T> metricValues, string group, string? subGroup, int intervalInMinutes, Func<T, object>? getValue = null) where T : IMetricValue
    {
        getValue ??= m => m;

        foreach (var metric in metricValues)
        {
            yield return new()
            {
                Group = group,
                SubGroup = subGroup,
                Name = metric.Name,
                IntervalInMinutes = intervalInMinutes,
                Value = getValue(metric),
                Unit = metric.Unit,
            };
        }
    }
}
