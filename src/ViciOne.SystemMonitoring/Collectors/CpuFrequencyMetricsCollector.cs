using System.Globalization;
using System.IO.Abstractions;
using System.Runtime.Versioning;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring.Collectors;

/// <summary>
/// Current, allowed and maximum clock of each core from cpufreq.
/// </summary>
/// <remarks>
/// The current clock follows the load under the ondemand and schedutil governors, so a low clock alone means an idle core.
/// <c>limit</c> is the clock the kernel currently allows; firmware that throttles on its own, as on a Raspberry Pi, leaves it at
/// the maximum. The shares in <c>total</c> are taken per core, so cores with different maximum clocks compare correctly.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class CpuFrequencyMetricsCollector(IFileSystem fileSystem) : IScheduledMetricsCollector, ILiveMetricsCollector
{
    private const string CpuDirectory = "/sys/devices/system/cpu";

    private readonly Lock _lock = new();
    private readonly AggregationBuffers _buffers = new("cpu_frequency");

    public int IntervalInMinutes { get; } = 1;
    public string Name => "cpu_frequency";

    public Task<List<Metric>> CollectMetrics(int interval, DateTimeOffset cycleStartTime, CancellationToken cancellationToken)
    {
        try
        {
            var valuesBySubGroup = ReadFrequencies();

            lock (_lock)
            {
                List<Metric> metrics = [.. valuesBySubGroup.SelectMany(v => v.Value.ToMetrics(Name, v.Key, IntervalInMinutes, m => m.Value))];
                metrics.AddRange(_buffers.Add(interval, valuesBySubGroup));
                return Task.FromResult(metrics);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting CPU frequency metrics.", ex);
        }
    }

    public Task<List<Metric>> CollectLiveMetrics(LiveWindow window, CancellationToken cancellationToken)
    {
        try
        {
            return Task.FromResult<List<Metric>>([.. ReadFrequencies().SelectMany(v => v.Value.ToMetrics(Name, v.Key, 0, m => m.Value))]);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting live CPU frequency metrics.", ex);
        }
    }

    /// <summary>
    /// Values per core (<c>core0</c>, <c>core1</c>, …) and a <c>total</c> with the average current clock, the highest maximum,
    /// the average clock utilisation and the lowest allowed share; empty without cpufreq.
    /// </summary>
    private Dictionary<string, List<LastMetricValue>> ReadFrequencies()
    {
        try
        {
            var cores = fileSystem.Directory.Exists(CpuDirectory)
                ? fileSystem.Directory.GetDirectories(CpuDirectory, "cpu*")
                    .Select(path => (Path: path, Number: CoreNumber(Path.GetFileName(path))))
                    .Where(core => core.Number is not null && fileSystem.File.Exists($"{core.Path}/cpufreq/scaling_cur_freq"))
                    .OrderBy(core => core.Number)
                    .Select(core => (Name: $"core{core.Number}",
                        Current: ReadMegahertz($"{core.Path}/cpufreq/scaling_cur_freq"),
                        Limit: ReadMegahertz($"{core.Path}/cpufreq/scaling_max_freq"),
                        Maximum: ReadMegahertz($"{core.Path}/cpufreq/cpuinfo_max_freq")))
                    .ToList()
                : [];

            Dictionary<string, List<LastMetricValue>> valuesBySubGroup = [];
            if (cores.Count == 0)
                return valuesBySubGroup;

            foreach (var (name, current, limit, maximum) in cores)
            {
                valuesBySubGroup[name] =
                [
                    new("current", current, MetricUnit.Megahertz),
                    new("limit", limit, MetricUnit.Megahertz),
                    new("maximum", maximum, MetricUnit.Megahertz),
                ];
            }

            static double Share(double value, double maximum) => maximum > 0 ? value / maximum * 100 : 0;

            valuesBySubGroup["total"] =
            [
                new("current", cores.Average(c => c.Current), MetricUnit.Megahertz),
                new("maximum", cores.Max(c => c.Maximum), MetricUnit.Megahertz),
                new("share_of_maximum", cores.Average(c => Share(c.Current, c.Maximum)), MetricUnit.Percent),
                new("limit_share", cores.Min(c => Share(c.Limit, c.Maximum)), MetricUnit.Percent),
            ];

            return valuesBySubGroup;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException("Permission error reading cpufreq.", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException("I/O error reading cpufreq.", ex);
        }
    }

    private static int? CoreNumber(string directoryName)
        => int.TryParse(directoryName.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;

    /// <summary>
    /// cpufreq reports kilohertz.
    /// </summary>
    private double ReadMegahertz(string path)
        => double.Parse(fileSystem.File.ReadAllText(path).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture) / 1000;
}
