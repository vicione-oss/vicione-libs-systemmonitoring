using System.Globalization;
using System.IO.Abstractions;
using System.Runtime.Versioning;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring.Collectors;

[SupportedOSPlatform("linux")]
public sealed class RamMetricsCollector(IFileSystem fileSystem, TimeProvider? timeProvider) : IScheduledMetricsCollector, ILiveMetricsCollector
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly List<List<LastMetricValue>> _fiveMinuteBuffer = [];
    private readonly List<List<LastMetricValue>> _fifteenMinuteBuffer = [];
    private readonly Lock _lock = new();

    private RefaultReading? _intervalBaseline;

    public int IntervalInMinutes { get; } = 1;
    public string Name => "ram";

    /// <summary>
    /// The constructor of 1.0, kept for callers compiled against it.
    /// </summary>
    public RamMetricsCollector(IFileSystem fileSystem)
        : this(fileSystem, null)
    {
    }

    public Task<List<Metric>> CollectMetrics(int interval, DateTimeOffset cycleStartTime, CancellationToken cancellationToken)
    {
        try
        {
            var ramData = CollectRamMetrics();
            var refaults = ReadRefaults();

            lock (_lock)
            {
                var metricsData = CreateMetricsData(ramData);
                metricsData.AddRange(CalculateUsage(ramData));
                metricsData.AddRange(RefaultsPerSecond(_intervalBaseline, refaults));
                _intervalBaseline = refaults;
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

    public Task<List<Metric>> CollectLiveMetrics(LiveWindow window, CancellationToken cancellationToken)
    {
        try
        {
            var ramData = CollectRamMetrics();
            var refaults = ReadRefaults();
            var baseline = window.Baseline<RefaultBaseline>(this);

            lock (_lock)
            {
                var metricsData = CreateMetricsData(ramData);
                metricsData.AddRange(CalculateUsage(ramData));
                metricsData.AddRange(RefaultsPerSecond(baseline.Reading, refaults));
                baseline.Reading = refaults;
                return Task.FromResult(metricsData.ToMetrics(Name, null, 0, m => m.Value).ToList());
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting live RAM metrics.", ex);
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

    /// <summary>
    /// Page cache pages per second since the baseline that were read back shortly after they had been evicted, the sign that
    /// memory is too small for the working set; nothing on the first reading.
    /// </summary>
    /// <remarks>
    /// Major page faults would also count programs loading their files for the first time, and miss reads through
    /// <c>read()</c>.
    /// </remarks>
    private static IEnumerable<LastMetricValue> RefaultsPerSecond(RefaultReading? baseline, RefaultReading? current)
    {
        if (baseline is null || current is null || current.Timestamp <= baseline.Timestamp)
            yield break;

        var seconds = (current.Timestamp - baseline.Timestamp).TotalSeconds;
        yield return new LastMetricValue("page_cache_refaults_per_second", Math.Round((current.Refaults - baseline.Refaults) / seconds, 2), MetricUnit.None);
    }

    /// <summary>
    /// Null before kernel 5.9, which has no <c>workingset_refault_file</c>, and when <c>/proc/vmstat</c> cannot be read, so that
    /// the other RAM values are still reported.
    /// </summary>
    private RefaultReading? ReadRefaults()
    {
        const string Counter = "workingset_refault_file ";

        try
        {
            var line = fileSystem.File.ReadAllLines("/proc/vmstat").FirstOrDefault(l => l.StartsWith(Counter, StringComparison.Ordinal));
            return line is null ? null : new(double.Parse(line.AsSpan(Counter.Length), CultureInfo.InvariantCulture), _timeProvider.GetUtcNow());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            return null;
        }
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

    private sealed record RefaultReading(double Refaults, DateTimeOffset Timestamp);

    /// <summary>
    /// The previous refault reading of a live window.
    /// </summary>
    private sealed class RefaultBaseline
    {
        public RefaultReading? Reading { get; set; }
    }
}
