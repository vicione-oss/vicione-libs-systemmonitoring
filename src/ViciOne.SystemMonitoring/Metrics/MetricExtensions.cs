using System.Text;

namespace ViciOne.SystemMonitoring.Metrics;

public static class MetricExtensions
{
    public static string ToIdString(this Metric metric)
    {
        StringBuilder sb = new();

        sb.Append(metric.Group);
        if (!string.IsNullOrEmpty(metric.SubGroup))
            sb.Append('.').Append(metric.SubGroup);
        sb.Append('.').Append(metric.Name);
        sb.Append('.').Append(metric.IntervalInMinutes);

        return sb.ToString();
    }
}
