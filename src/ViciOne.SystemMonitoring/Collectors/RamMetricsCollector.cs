using System.Globalization;
using System.IO.Abstractions;
using System.Runtime.Versioning;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring.Collectors;

[SupportedOSPlatform("linux")]
public sealed class RamMetricsCollector(IFileSystem fileSystem) : IScheduledMetricsCollector
{
    private readonly List<List<LastMetricValue>> _fiveMinuteBuffer = [];
    private readonly List<List<LastMetricValue>> _fifteenMinuteBuffer = [];
    private readonly Lock _lock = new();

    public int IntervalInMinutes { get; } = 1;
    public string Name => "ram";

    public Task<List<Metric>> CollectMetrics(int interval, DateTimeOffset cycleStartTime, CancellationToken cancellationToken)
    {
        try
        {
            var ramData = CollectRamMetrics();

            lock (_lock)
            {
                var metricsData = CreateMetricsData(ramData);
                metricsData.AddRange(CalculateUsage(ramData));
                List<Metric> metrics = [];

                metrics.AddRange(metricsData.ToMetrics(Name, null, IntervalInMinutes, m => m.Value));

                UpdateBuffer(_fiveMinuteBuffer, metricsData);
                UpdateBuffer(_fifteenMinuteBuffer, metricsData);

                if (interval % 5 == 0)
                {
                    metrics.AddRange(_fiveMinuteBuffer.AggregateBuffer().ToMetrics(Name, null, 5));
                    _fiveMinuteBuffer.Clear();
                }

                if (interval % 15 == 0)
                {
                    metrics.AddRange(_fifteenMinuteBuffer.AggregateBuffer().ToMetrics(Name, null, 15));
                    _fifteenMinuteBuffer.Clear();
                }

                return Task.FromResult(metrics);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting RAM metrics.", ex);
        }
    }

    private static IEnumerable<LastMetricValue> CalculateUsage(Dictionary<string, double> ramData)
    {
        var total = ramData["total"];
        var free = ramData["free"];
        var available = ramData["available"];
        var buffer = ramData["buffer"];
        var cached = ramData["cached"];
        var used = total - free - buffer - cached;
        yield return new LastMetricValue("actual_memory_usage", Math.Round(used / total * 100, 2), MetricUnit.Percent); // like htop
        yield return new LastMetricValue("used_memory_usage", Math.Round((total - free) / total * 100, 2), MetricUnit.Percent); // like top
        yield return new LastMetricValue("total_memory_usage", Math.Round((total - available) / total * 100, 2), MetricUnit.Percent); // like free
    }

    private Dictionary<string, double> CollectRamMetrics()
    {
        var ramData = new Dictionary<string, double>();

        try
        {
            var lines = fileSystem.File.ReadAllLines("/proc/meminfo");
            if (lines.Length == 0) throw new InvalidOperationException("Empty RAM stats.");

            foreach (var line in lines)
            {
                var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;

                var key = parts[0].Trim();
                var value = double.Parse(parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], CultureInfo.InvariantCulture);

                switch (key)
                {
                    case "MemTotal":
                        ramData["total"] = value;
                        break;
                    case "MemFree":
                        ramData["free"] = value;
                        break;
                    case "MemAvailable":
                        ramData["available"] = value;
                        break;
                    case "Buffers":
                        ramData["buffer"] = value;
                        break;
                    case "Cached":
                        ramData["cached"] = value;
                        break;
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException("Permission error reading RAM metrics.", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException("I/O error reading RAM metrics.", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error reading RAM metrics.", ex);
        }

        return ramData;
    }

    private static List<LastMetricValue> CreateMetricsData(Dictionary<string, double> ramData)
        => [.. ramData.Select(kv => new LastMetricValue(kv.Key, kv.Value, MetricUnit.Kilobyte))];

    private static void UpdateBuffer(
        List<List<LastMetricValue>> buffer,
        List<LastMetricValue> metrics)
    {
        if (metrics.Count > 0)
            buffer.Add(metrics);
    }
}
