using System.Globalization;
using System.IO.Abstractions;
using System.Runtime.Versioning;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring.Collectors;

[SupportedOSPlatform("linux")]
public sealed class CpuMetricsCollector(IFileSystem fileSystem) : IScheduledMetricsCollector, IPrepareMetricsCollector
{
    private readonly Lock _lock = new();

    private readonly Dictionary<string, double> _previousTotalCpuData = [];
    private readonly List<Dictionary<string, double>> _previousPerCoreCpuData = [];

    private readonly List<List<LastMetricValue>> _fiveMinuteBuffer = [];
    private readonly List<List<List<LastMetricValue>>> _perCoreFiveMinuteBuffer = [];
    private readonly List<List<LastMetricValue>> _fifteenMinuteBuffer = [];
    private readonly List<List<List<LastMetricValue>>> _perCoreFifteenMinuteBuffer = [];

    public int IntervalInMinutes { get; } = 1;
    public string Name => "cpu";

    public Task PrepareMetrics(CancellationToken cancellationToken)
    {
        try
        {
            var (totalCpuData, perCoreCpuData) = CollectCpuMetrics();

            lock (_lock)
            {
                UpdatePreviousTotalValues(totalCpuData);
                UpdatePreviousPerCoreValues(perCoreCpuData);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting CPU metrics.", ex);
        }

        return Task.CompletedTask;
    }

    public Task<List<Metric>> CollectMetrics(int interval, DateTimeOffset cycleStartTime, CancellationToken cancellationToken)
    {
        try
        {
            var (totalCpuData, perCoreCpuData) = CollectCpuMetrics();

            lock (_lock)
            {
                // /proc/stat has Jiffies, not percentages - we need to calculate the diff's and convert to percentages
                var totalPercentages = CalculateTotalCpuUsagePercentage(CalculateTotalDifferences(totalCpuData, _previousTotalCpuData));
                var perCorePercentages = CalculatePerCoreCpuUsagePercentage(CalculatePerCoreDifferences(perCoreCpuData, _previousPerCoreCpuData));
                totalPercentages.Add("total_system", totalPercentages["user"] + totalPercentages["system"] + totalPercentages["steal"]);

                UpdatePreviousTotalValues(totalCpuData);
                UpdatePreviousPerCoreValues(perCoreCpuData);

                var totalMetrics = CreateMetricsData(totalPercentages);
                var perCoreMetrics = CreatePerCoreMetricsData(perCorePercentages);

                List<Metric> metrics = [];
                metrics.AddRange(totalMetrics.ToMetrics(Name, "total", IntervalInMinutes, m => m.Value));
                metrics.AddRange(CreatePerCoreMetrics(IntervalInMinutes, perCoreMetrics, m => m.Value));

                UpdateBuffer(_fiveMinuteBuffer, _perCoreFiveMinuteBuffer, totalMetrics, perCoreMetrics);
                UpdateBuffer(_fifteenMinuteBuffer, _perCoreFifteenMinuteBuffer, totalMetrics, perCoreMetrics);

                if (interval % 5 == 0)
                {
                    metrics.AddRange(_fiveMinuteBuffer.AggregateBuffer().ToMetrics(Name, "total", 5));
                    metrics.AddRange(CreatePerCoreMetrics(5, _perCoreFiveMinuteBuffer.Select(b => MetricsHelper.AggregateBuffer(b).ToList()).ToList()));

                    _fiveMinuteBuffer.Clear();
                    foreach (var coreBuffer in _perCoreFiveMinuteBuffer)
                        coreBuffer.Clear();
                }

                if (interval % 15 == 0)
                {
                    metrics.AddRange(_fifteenMinuteBuffer.AggregateBuffer().ToMetrics(Name, "total", 15));
                    metrics.AddRange(CreatePerCoreMetrics(15, _perCoreFifteenMinuteBuffer.Select(b => MetricsHelper.AggregateBuffer(b).ToList()).ToList()));

                    _fifteenMinuteBuffer.Clear();
                    foreach (var coreBuffer in _perCoreFifteenMinuteBuffer)
                        coreBuffer.Clear();
                }

                return Task.FromResult(metrics);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting CPU metrics.", ex);
        }
    }

    private (Dictionary<string, double>, List<Dictionary<string, double>>) CollectCpuMetrics()
    {
        var totalCpuData = new Dictionary<string, double>();
        var perCoreData = new List<Dictionary<string, double>>();

        try
        {
            var lines = fileSystem.File.ReadAllLines("/proc/stat");
            if (lines.Length == 0) throw new InvalidOperationException("Empty CPU stats.");

            foreach (var line in lines.Where(l => l.StartsWith("cpu", StringComparison.InvariantCulture)))
            {
                if (!line.StartsWith("cpu", StringComparison.InvariantCulture))
                    break;

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 11)
                    continue;

                var metrics = ParseCpuMetrics(parts);
                if (parts[0] == "cpu")
                    totalCpuData = metrics;
                else
                    perCoreData.Add(metrics);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException("Permission error reading CPU metrics.", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException("I/O error reading CPU metrics.", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error reading CPU metrics.", ex);
        }

        return (totalCpuData, perCoreData);
    }

    private static Dictionary<string, double> ParseCpuMetrics(string[] parts)
    {
        try
        {
            if (!parts.Skip(1).Take(10).All(p => long.TryParse(p, out _)))
                throw new FormatException("Error parsing CPU data.");

            return new Dictionary<string, double>
            {
                // User = user + user nice
                ["user"] = long.Parse(parts[1], CultureInfo.InvariantCulture) + long.Parse(parts[2], CultureInfo.InvariantCulture),

                // System = system + irq + softirq
                ["system"] = long.Parse(parts[3], CultureInfo.InvariantCulture) + long.Parse(parts[6], CultureInfo.InvariantCulture) + long.Parse(parts[7], CultureInfo.InvariantCulture),

                // Idle = idle + iowait
                ["idle"] = long.Parse(parts[4], CultureInfo.InvariantCulture) + long.Parse(parts[5], CultureInfo.InvariantCulture),

                // Steal = steal
                ["steal"] = long.Parse(parts[8], CultureInfo.InvariantCulture),

                // Guest = guest + guest_nice
                ["guest"] = long.Parse(parts[9], CultureInfo.InvariantCulture) + long.Parse(parts[10], CultureInfo.InvariantCulture)
            };
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Error parsing CPU data.", ex);
        }
    }

    private static Dictionary<string, double> CalculateTotalCpuUsagePercentage(Dictionary<string, double> differences)
    {
        var totalJiffies = differences.Values.Sum();
        if (totalJiffies == 0) return differences.ToDictionary(kv => kv.Key, kv => 0.0);

        return differences.ToDictionary(kv => kv.Key, kv => kv.Value / totalJiffies * 100);
    }

    private static List<Dictionary<string, double>> CalculatePerCoreCpuUsagePercentage(List<Dictionary<string, double>> differences)
        => [.. differences.Select(coreDifferences =>
        {
            var totalJiffies = coreDifferences.Values.Sum();
            if (totalJiffies == 0)
                return coreDifferences.ToDictionary(kv => kv.Key, kv => 0.0);

            return coreDifferences.ToDictionary(kv => kv.Key, kv => kv.Value / totalJiffies * 100);
        })];

    private static Dictionary<string, double> CalculateTotalDifferences(
        Dictionary<string, double> currentData,
        Dictionary<string, double> previousData)
    {
        if (previousData.Count == 0)
            return currentData.ToDictionary(kv => kv.Key, kv => 0.0);

        return currentData.ToDictionary(kv => kv.Key, kv => kv.Value - previousData.GetValueOrDefault(kv.Key, 0));
    }

    private static List<Dictionary<string, double>> CalculatePerCoreDifferences(
        List<Dictionary<string, double>> currentData,
        List<Dictionary<string, double>> previousData)
    {
        var result = new List<Dictionary<string, double>>();

        for (var i = 0; i < currentData.Count; i++)
        {
            if (i >= previousData.Count)
                result.Add(currentData[i].ToDictionary(kv => kv.Key, kv => 0.0));
            else
                result.Add(currentData[i].ToDictionary(kv => kv.Key, kv => kv.Value - previousData[i].GetValueOrDefault(kv.Key, 0)));
        }

        return result;
    }

    private void UpdatePreviousTotalValues(Dictionary<string, double> totalData)
    {
        _previousTotalCpuData.Clear();
        foreach (var kv in totalData)
            _previousTotalCpuData[kv.Key] = kv.Value;
    }

    private void UpdatePreviousPerCoreValues(List<Dictionary<string, double>> perCoreData)
    {
        _previousPerCoreCpuData.Clear();
        _previousPerCoreCpuData.AddRange(perCoreData.Select(data => new Dictionary<string, double>(data)));
    }

    private static List<LastMetricValue> CreateMetricsData(Dictionary<string, double> cpuData)
        => [.. cpuData.Select(kv => new LastMetricValue(kv.Key, kv.Value, MetricUnit.Percent))];

    private static List<List<LastMetricValue>> CreatePerCoreMetricsData(List<Dictionary<string, double>> perCoreData)
        => [.. perCoreData.Select(coreData => coreData.Select(kv => new LastMetricValue(kv.Key, kv.Value, MetricUnit.Percent)).ToList())];

    private static void UpdateBuffer(
        List<List<LastMetricValue>> totalBuffer,
        List<List<List<LastMetricValue>>> perCoreBuffer,
        List<LastMetricValue> totalMetrics,
        List<List<LastMetricValue>> perCoreMetrics)
    {
        if (totalMetrics.Count > 0) totalBuffer.Add(totalMetrics);

        for (var i = 0; i < perCoreMetrics.Count; i++)
        {
            if (perCoreBuffer.Count <= i)
                perCoreBuffer.Add([]);

            if (perCoreMetrics[i].Count > 0)
                perCoreBuffer[i].Add(perCoreMetrics[i]);
        }
    }

    private IEnumerable<Metric> CreatePerCoreMetrics<T>(int intervalInMinutes, List<List<T>> perCoreMetrics, Func<T, object>? getValue = null) where T : IMetricValue
    {
        for (var i = 0; i < perCoreMetrics.Count; i++)
        {
            foreach (var metric in perCoreMetrics[i].ToMetrics(Name, $"core{i}", intervalInMinutes, getValue))
                yield return metric;
        }
    }
}
