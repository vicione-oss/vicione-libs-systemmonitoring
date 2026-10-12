using System.IO.Abstractions;
using System.Runtime.Versioning;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring.Collectors;

[SupportedOSPlatform("linux")]
public sealed class LoadAverageMetricsCollector(IFileSystem fileSystem) : IScheduledMetricsCollector, ILiveMetricsCollector
{
    public int IntervalInMinutes { get; } = 1;
    public string Name => "loadavg";

    public async Task<List<Metric>> CollectMetrics(int interval, DateTimeOffset cycleStartTime, CancellationToken cancellationToken)
    {
        try
        {
            var (load1, load5, load15, running, total) = await ReadLoadAverageAsync();

            List<Metric> metrics =
            [
                new()
                {
                    Group = Name,
                    Name = "load1",
                    IntervalInMinutes = IntervalInMinutes,
                    Value = load1,
                    Unit = MetricUnit.None,
                },
                new()
                {
                    Group = Name,
                    Name = "load5",
                    IntervalInMinutes = IntervalInMinutes,
                    Value = load5,
                    Unit = MetricUnit.None,
                },
                new()
                {
                    Group = Name,
                    Name = "load15",
                    IntervalInMinutes = IntervalInMinutes,
                    Value = load15,
                    Unit = MetricUnit.None,
                },
                new()
                {
                    Group = Name,
                    Name = "running_kernel_scheduling_entities",
                    IntervalInMinutes = IntervalInMinutes,
                    Value = running,
                    Unit = MetricUnit.None,
                },
                new()
                {
                    Group = Name,
                    Name = "total_kernel_scheduling_entities",
                    IntervalInMinutes = IntervalInMinutes,
                    Value = total,
                    Unit = MetricUnit.None,
                }
            ];

            if (await ReadOnlineCpusAsync() is { } onlineCpus)
            {
                metrics.Add(new()
                {
                    Group = Name,
                    Name = "online_cpus",
                    IntervalInMinutes = IntervalInMinutes,
                    Value = onlineCpus,
                    Unit = MetricUnit.None,
                });
            }

            return metrics;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting load average metrics.", ex);
        }
    }

    public async Task<List<Metric>> CollectLiveMetrics(CancellationToken cancellationToken)
        => [.. (await CollectMetrics(IntervalInMinutes, DateTimeOffset.UtcNow, cancellationToken)).Select(MetricsHelper.WithoutInterval)];

    /// <summary>
    /// The number of CPUs the kernel schedules on, which the load values relate to; null when the list cannot be read.
    /// </summary>
    /// <remarks>
    /// Not <see cref="Environment.ProcessorCount"/>: it is limited by the cgroup and the affinity of this process, while the load
    /// counts the whole host.
    /// </remarks>
    private async Task<int?> ReadOnlineCpusAsync()
    {
        try
        {
            return CountCpus(await fileSystem.File.ReadAllTextAsync("/sys/devices/system/cpu/online"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Counts a kernel CPU list such as <c>0-3,6,8-9</c>.
    /// </summary>
    private static int? CountCpus(string? list)
    {
        var count = 0;
        foreach (var range in (list ?? "").Trim().Split(','))
        {
            var bounds = range.Split('-');
            if (bounds.Length > 2
                || !int.TryParse(bounds[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var first)
                || !int.TryParse(bounds[^1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var last)
                || last < first)
            {
                return null;
            }

            count += last - first + 1;
        }

        return count;
    }

    private async Task<(double Load1, double Load5, double Load15, int Running, int Total)> ReadLoadAverageAsync()
    {
        try
        {
            var lines = await fileSystem.File.ReadAllLinesAsync("/proc/loadavg");
            
            if (lines.Length == 0)
                throw new InvalidOperationException($"Empty {Name} file.");

            var parts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 4)
                throw new FormatException($"Invalid {Name} format.");

            if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var load1))
                throw new FormatException($"Invalid {nameof(load1)} value.");

            if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var load5))
                throw new FormatException($"Invalid {nameof(load5)} value.");

            if (!double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var load15))
                throw new FormatException($"Invalid {nameof(load15)} value.");

            var processParts = parts[3].Split('/');

            if (processParts.Length != 2
                || !int.TryParse(processParts[0], out var running)
                || !int.TryParse(processParts[1], out var total))
            {
                throw new FormatException("Invalid processes format.");
            }

            return (load1, load5, load15, running, total);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException($"Permission error reading {Name}.", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"I/O error reading {Name}.", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"An unexpected error occurred while reading {Name}.", ex);
        }
    }
}
