using System.IO.Abstractions;
using System.Runtime.Versioning;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring.Collectors;

[SupportedOSPlatform("linux")]
public sealed class UptimeMetricsCollector(IFileSystem fileSystem) : IScheduledMetricsCollector
{
    public int IntervalInMinutes { get; } = 1;
    public string Name => "uptime";

    public Task<List<Metric>> CollectMetrics(int interval, DateTimeOffset cycleStartTime, CancellationToken cancellationToken)
    {
        try
        {
            var uptime = CollectUptime();

            return Task.FromResult<List<Metric>>([new()
            {
                Group = Name,
                Name = "uptime_seconds",
                IntervalInMinutes = IntervalInMinutes,
                Value = uptime,
                Unit = MetricUnit.Second,
            }]);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting Uptime metrics.", ex);
        }
    }

    private double CollectUptime()
    {
        try
        {
            var lines = fileSystem.File.ReadAllLines("/proc/uptime");
            if (lines.Length == 0)
                throw new InvalidOperationException("Empty uptime file.");

            var parts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || !double.TryParse(parts[0], out var uptime))
                throw new FormatException("Invalid uptime format.");

            return uptime;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException("Permission error reading uptime.", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException("I/O error reading uptime.", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error reading uptime.", ex);
        }
    }
}
