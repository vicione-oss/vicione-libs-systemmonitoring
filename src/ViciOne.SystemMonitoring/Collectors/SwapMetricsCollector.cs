using System.Globalization;
using System.IO.Abstractions;
using System.Runtime.Versioning;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring.Collectors;

[SupportedOSPlatform("linux")]
public sealed class SwapMetricsCollector(IFileSystem fileSystem, TimeProvider? timeProvider) : IScheduledMetricsCollector, ILiveMetricsCollector
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Baseline _intervalBaseline = new();
    private readonly Baseline _liveBaseline = new();
    private readonly List<List<LastMetricValue>> _fiveMinuteBuffer = [];
    private readonly List<List<LastMetricValue>> _fifteenMinuteBuffer = [];
    private readonly Lock _lock = new();

    public int IntervalInMinutes { get; } = 1;
    public string Name => "swap";

    /// <summary>
    /// The constructor of 1.0, kept for callers compiled against it.
    /// </summary>
    public SwapMetricsCollector(IFileSystem fileSystem)
        : this(fileSystem, null)
    {
    }

    public Task<List<Metric>> CollectMetrics(int interval, DateTimeOffset cycleStartTime, CancellationToken cancellationToken)
    {
        try
        {
            var currentSwapStats = CollectSwapMetrics();
            var now = _timeProvider.GetUtcNow();

            lock (_lock)
            {
                var values = CalculateValues(currentSwapStats, now, _intervalBaseline);
                _intervalBaseline.Update(currentSwapStats, now);

                var metricsData = CreateMetricsData(values);
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
            throw new InvalidOperationException("Error collecting SWAP metrics.", ex);
        }
    }

    public Task<List<Metric>> CollectLiveMetrics(CancellationToken cancellationToken)
    {
        try
        {
            var currentSwapStats = CollectSwapMetrics();
            var now = _timeProvider.GetUtcNow();

            lock (_lock)
            {
                var values = CalculateValues(currentSwapStats, now, _liveBaseline);
                _liveBaseline.Update(currentSwapStats, now);
                return Task.FromResult(CreateMetricsData(values).ToMetrics(Name, null, 0, m => m.Value).ToList());
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting live SWAP metrics.", ex);
        }
    }

    private Dictionary<string, double> CollectSwapMetrics()
    {
        var swapStats = new Dictionary<string, double>();

        try
        {
            var memInfoLines = fileSystem.File.ReadAllLines("/proc/meminfo");
            double totalSwap = 0;
            double freeSwap = 0;

            foreach (var line in memInfoLines)
            {
                if (line.StartsWith("SwapTotal:", StringComparison.InvariantCulture))
                    totalSwap = double.Parse(line.Split(':', StringSplitOptions.RemoveEmptyEntries)[1].Trim().Split(' ')[0], CultureInfo.InvariantCulture);

                if (line.StartsWith("SwapFree:", StringComparison.InvariantCulture))
                    freeSwap = double.Parse(line.Split(':', StringSplitOptions.RemoveEmptyEntries)[1].Trim().Split(' ')[0], CultureInfo.InvariantCulture);
            }

            var usedSwap = totalSwap - freeSwap;
            swapStats["total"] = totalSwap;
            swapStats["used"] = usedSwap;

            var vmStatLines = fileSystem.File.ReadAllLines("/proc/vmstat");
            double swapIn = 0;
            double swapOut = 0;

            foreach (var line in vmStatLines)
            {
                if (line.StartsWith("pswpin", StringComparison.InvariantCulture))
                    swapIn = double.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture);

                if (line.StartsWith("pswpout", StringComparison.InvariantCulture))
                    swapOut = double.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture);
            }

            swapStats["swap_in"] = swapIn;
            swapStats["swap_out"] = swapOut;
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
            throw new InvalidOperationException("Error reading SWAP metrics.", ex);
        }

        return swapStats;
    }

    /// <summary>
    /// <c>total</c> and <c>used</c> as read, the page counters <c>swap_in</c> and <c>swap_out</c> as the change since the baseline
    /// and, unless this is the first reading, as that change per second.
    /// </summary>
    private static Dictionary<string, double> CalculateValues(Dictionary<string, double> currentData, DateTimeOffset now, Baseline baseline)
    {
        var values = currentData.ToDictionary(kv => kv.Key, kv => !IsCounter(kv.Key)
            ? kv.Value
            : baseline.Values.TryGetValue(kv.Key, out var previous) ? kv.Value - previous : 0.0);

        var seconds = (now - baseline.Timestamp).TotalSeconds;
        if (baseline.Values.Count == 0 || seconds <= 0)
            return values;

        foreach (var counter in currentData.Keys.Where(IsCounter))
            values[$"{counter}_per_second"] = Math.Round(values[counter] / seconds, 2);

        return values;
    }

    private static bool IsCounter(string name)
        => name.StartsWith("swap", StringComparison.Ordinal);

    private static List<LastMetricValue> CreateMetricsData(Dictionary<string, double> swapData)
        => [.. swapData.Select(kv => new LastMetricValue(kv.Key, kv.Value, IsCounter(kv.Key) ? MetricUnit.None : MetricUnit.Kilobyte))];

    private static void UpdateBuffer(
        List<List<LastMetricValue>> buffer,
        List<LastMetricValue> metrics)
    {
        if (metrics.Count > 0)
            buffer.Add(metrics);
    }

    /// <summary>
    /// One reading and when it was taken, the reference for the next difference.
    /// </summary>
    private sealed class Baseline
    {
        public Dictionary<string, double> Values { get; } = [];

        public DateTimeOffset Timestamp { get; private set; }

        public void Update(Dictionary<string, double> values, DateTimeOffset timestamp)
        {
            Values.Clear();
            foreach (var (name, value) in values)
                Values[name] = value;

            Timestamp = timestamp;
        }
    }
}
