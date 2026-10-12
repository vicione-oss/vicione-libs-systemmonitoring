using System.Globalization;
using System.IO.Abstractions;
using System.Runtime.Versioning;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring.Collectors;

[SupportedOSPlatform("linux")]
public sealed class CpuMetricsCollector(IFileSystem fileSystem) : IScheduledMetricsCollector, IPrepareMetricsCollector, ILiveMetricsCollector
{
    /// <summary>
    /// The share of <c>idle</c> the CPU waited for I/O; it is not added to the total time a second time.
    /// </summary>
    private const string PartOfIdle = "iowait";

    private readonly Lock _lock = new();

    private readonly CpuTimes _intervalBaseline = new();
    private readonly CpuTimes _liveBaseline = new();

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
                _intervalBaseline.Update(totalCpuData, perCoreCpuData);
                _liveBaseline.Update(totalCpuData, perCoreCpuData);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting CPU metrics.", ex);
        }

        return Task.CompletedTask;
    }

    public Task<List<Metric>> CollectLiveMetrics(CancellationToken cancellationToken)
    {
        try
        {
            var (totalCpuData, perCoreCpuData) = CollectCpuMetrics();

            lock (_lock)
            {
                var (totalPercentages, perCorePercentages) = CalculatePercentages(totalCpuData, perCoreCpuData, _liveBaseline);
                _liveBaseline.Update(totalCpuData, perCoreCpuData);

                List<Metric> metrics = [];
                metrics.AddRange(CreateMetricsData(totalPercentages).ToMetrics(Name, "total", 0, m => m.Value));
                metrics.AddRange(CreatePerCoreMetrics(0, CreatePerCoreMetricsData(perCorePercentages), m => m.Value));
                return Task.FromResult(metrics);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting live CPU metrics.", ex);
        }
    }

    public Task<List<Metric>> CollectMetrics(int interval, DateTimeOffset cycleStartTime, CancellationToken cancellationToken)
    {
        try
        {
            var (totalCpuData, perCoreCpuData) = CollectCpuMetrics();

            lock (_lock)
            {
                var (totalPercentages, perCorePercentages) = CalculatePercentages(totalCpuData, perCoreCpuData, _intervalBaseline);
                _intervalBaseline.Update(totalCpuData, perCoreCpuData);

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
                // User = user + user nice, without the guest time the kernel counts in both as well
                ["user"] = long.Parse(parts[1], CultureInfo.InvariantCulture) + long.Parse(parts[2], CultureInfo.InvariantCulture)
                    - long.Parse(parts[9], CultureInfo.InvariantCulture) - long.Parse(parts[10], CultureInfo.InvariantCulture),

                // System = system + irq + softirq
                ["system"] = long.Parse(parts[3], CultureInfo.InvariantCulture) + long.Parse(parts[6], CultureInfo.InvariantCulture) + long.Parse(parts[7], CultureInfo.InvariantCulture),

                // Idle = idle + iowait
                ["idle"] = long.Parse(parts[4], CultureInfo.InvariantCulture) + long.Parse(parts[5], CultureInfo.InvariantCulture),

                // Iowait, already counted in idle
                [PartOfIdle] = long.Parse(parts[5], CultureInfo.InvariantCulture),

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

    private static (Dictionary<string, double> Total, List<Dictionary<string, double>> PerCore) CalculatePercentages(
        Dictionary<string, double> totalCpuData, List<Dictionary<string, double>> perCoreCpuData, CpuTimes baseline)
    {
        var totalPercentages = CalculateUsagePercentages(CalculateTotalDifferences(totalCpuData, baseline.Total));
        var perCorePercentages = CalculatePerCoreCpuUsagePercentage(CalculatePerCoreDifferences(perCoreCpuData, baseline.PerCore));
        totalPercentages.Add("total_system", totalPercentages["user"] + totalPercentages["system"] + totalPercentages["steal"] + totalPercentages["guest"]);
        return (totalPercentages, perCorePercentages);
    }

    private static Dictionary<string, double> CalculateUsagePercentages(Dictionary<string, double> differences)
    {
        var totalJiffies = differences.Where(kv => kv.Key != PartOfIdle).Sum(kv => kv.Value);
        if (totalJiffies == 0) return differences.ToDictionary(kv => kv.Key, kv => 0.0);

        // The kernel may move idle time from iowait to idle between two reads, so the iowait counter can go backwards.
        var iowait = Math.Max(0, differences.GetValueOrDefault(PartOfIdle));
        return differences.ToDictionary(kv => kv.Key, kv => (kv.Key == PartOfIdle ? iowait : kv.Value) / totalJiffies * 100);
    }

    private static List<Dictionary<string, double>> CalculatePerCoreCpuUsagePercentage(List<Dictionary<string, double>> differences)
        => [.. differences.Select(CalculateUsagePercentages)];

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

    /// <summary>
    /// CPU times of one reading of /proc/stat, the reference for the next difference.
    /// </summary>
    private sealed class CpuTimes
    {
        public Dictionary<string, double> Total { get; } = [];

        public List<Dictionary<string, double>> PerCore { get; } = [];

        public void Update(Dictionary<string, double> total, List<Dictionary<string, double>> perCore)
        {
            Total.Clear();
            foreach (var (key, value) in total)
                Total[key] = value;

            PerCore.Clear();
            PerCore.AddRange(perCore.Select(data => new Dictionary<string, double>(data)));
        }
    }
}
