using System.IO.Abstractions;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring.Collectors;

[SupportedOSPlatform("linux")]
public sealed class DiskMetricsCollector(ILogger<DiskMetricsCollector> logger, IFileSystem fileSystem) : IScheduledMetricsCollector
{
    private readonly Dictionary<string, List<List<LastMetricValue>>> _buffers = [];
    private readonly Lock _lock = new();

    public int IntervalInMinutes { get; } = 5;
    public string Name => "disk";

    public Task<List<Metric>> CollectMetrics(int interval, DateTimeOffset cycleStartTime, CancellationToken cancellationToken)
    {
        try
        {
            var diskMetrics = CollectDiskMetrics();

            lock (_lock)
            {
                List<Metric> metrics = [];

                foreach (var (name, currentMetrics) in diskMetrics)
                {
                    if (!_buffers.TryGetValue(name, out var buffer))
                    {
                        buffer = [];
                        _buffers[name] = buffer;
                    }

                    metrics.AddRange(currentMetrics.ToMetrics(Name, name, IntervalInMinutes, m => m.Value));
                    UpdateBuffer(buffer, [.. currentMetrics.Where(m => m.Value is double)]);
                }

                if (interval % 15 == 0)
                {
                    List<string> buffersToRemove = [];

                    foreach (var (name, buffer) in _buffers)
                    {
                        if (buffer.Count == 0)
                        {
                            buffersToRemove.Add(name);
                            continue;
                        }

                        metrics.AddRange(buffer.AggregateBuffer().ToMetrics(Name, name, 15));
                        buffer.Clear();
                    }

                    foreach (var name in buffersToRemove)
                        _buffers.Remove(name);
                }

                return Task.FromResult(metrics);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting Disk metrics.", ex);
        }
    }

    private Dictionary<string, List<LastMetricValue>> CollectDiskMetrics()
    {
        Dictionary<string, List<LastMetricValue>> diskMetrics = [];

        try
        {
            foreach (var drive in fileSystem.DriveInfo.GetDrives())
            {
                if (drive.DriveType is not DriveType.Fixed or DriveType.Removable or DriveType.Ram)
                    continue;

                if (!drive.IsReady)
                    continue;

                var name = drive.Name.Replace("/", "\\", StringComparison.OrdinalIgnoreCase);
                List<LastMetricValue> metrics = [];

                try
                {
                    var driveType = drive.DriveType.ToString();
                    var driveFormat = drive.DriveFormat;
                    var totalSizeGB = drive.TotalSize / 1073741824.0; // Umrechnung in GB
                    var totalFreeSpaceGB = drive.TotalFreeSpace / 1073741824.0; // Umrechnung in GB

                    // Werte zu den Metriken hinzufügen
                    metrics.Add(new("driveType", driveType, MetricUnit.None));
                    metrics.Add(new("driveFormat", driveFormat, MetricUnit.None));
                    metrics.Add(new("totalSizeGB", totalSizeGB, MetricUnit.Gigabyte));
                    metrics.Add(new("totalFreeSpaceGB", totalFreeSpaceGB, MetricUnit.Gigabyte));
                    metrics.Add(new("usage", (totalSizeGB - totalFreeSpaceGB) / totalSizeGB * 100, MetricUnit.Percent));
                }
                catch (Exception ex)
                {
                    logger.LogErrorProcessingDrive(drive.Name, ex);
                }

                diskMetrics[name] = metrics;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error retrieving drives.", ex);
        }

        return diskMetrics;
    }

    private static void UpdateBuffer(
        List<List<LastMetricValue>> buffer,
        List<LastMetricValue> currentMetrics)
    {
        buffer.Add(currentMetrics);

        if (buffer.Count > 3)
            buffer.RemoveAt(0);
    }
}
