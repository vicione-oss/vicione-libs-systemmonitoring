using System.Globalization;
using System.IO.Abstractions;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring.Collectors;

/// <summary>
/// Reads and write load of the block devices from /proc/diskstats, one subgroup per whole device.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class DiskIoMetricsCollector(IFileSystem fileSystem, TimeProvider? timeProvider = null)
    : IScheduledMetricsCollector, IPrepareMetricsCollector, ILiveMetricsCollector
{
    // The kernel counts diskstats sectors in 512 bytes regardless of the device's sector size.
    private const int SectorSize = 512;

    private const string BlockDirectory = "/sys/class/block";

    private static readonly Regex s_emmcArea = new(@"^mmcblk\d+(boot\d+|gp\d+|rpmb)$", RegexOptions.CultureInvariant);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Lock _lock = new();

    private readonly Dictionary<string, Reading> _intervalBaseline = [];
    private readonly Dictionary<string, Reading> _liveBaseline = [];
    private readonly AggregationBuffers _buffers = new("disk_io");

    public int IntervalInMinutes { get; } = 1;
    public string Name => "disk_io";

    public Task PrepareMetrics(CancellationToken cancellationToken)
    {
        try
        {
            var readings = ReadDevices();

            lock (_lock)
            {
                foreach (var (device, reading) in readings)
                {
                    _intervalBaseline[device] = reading;
                    _liveBaseline[device] = reading;
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting disk I/O metrics.", ex);
        }

        return Task.CompletedTask;
    }

    public Task<List<Metric>> CollectMetrics(int interval, DateTimeOffset cycleStartTime, CancellationToken cancellationToken)
    {
        try
        {
            var readings = ReadDevices();

            lock (_lock)
            {
                var valuesByDevice = Differences(_intervalBaseline, readings);
                List<Metric> metrics = [.. valuesByDevice.SelectMany(d => d.Value.ToMetrics(Name, d.Key, IntervalInMinutes, m => m.Value))];
                metrics.AddRange(_buffers.Add(interval, valuesByDevice));
                return Task.FromResult(metrics);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting disk I/O metrics.", ex);
        }
    }

    public Task<List<Metric>> CollectLiveMetrics(CancellationToken cancellationToken)
    {
        try
        {
            var readings = ReadDevices();

            lock (_lock)
            {
                return Task.FromResult<List<Metric>>(
                    [.. Differences(_liveBaseline, readings).SelectMany(d => d.Value.ToMetrics(Name, d.Key, 0, m => m.Value))]);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting live disk I/O metrics.", ex);
        }
    }

    /// <summary>
    /// The load since the baseline per device, which then moves to the current readings; a device seen the first time has none.
    /// </summary>
    private static Dictionary<string, List<LastMetricValue>> Differences(Dictionary<string, Reading> baseline, Dictionary<string, Reading> readings)
    {
        Dictionary<string, List<LastMetricValue>> valuesByDevice = [];

        foreach (var (device, current) in readings)
        {
            if (baseline.TryGetValue(device, out var previous) && current.Timestamp > previous.Timestamp)
                valuesByDevice[device] = Load(previous, current);

            baseline[device] = current;
        }

        foreach (var device in baseline.Keys.Where(d => !readings.ContainsKey(d)).ToList())
            baseline.Remove(device);

        return valuesByDevice;
    }

    private static List<LastMetricValue> Load(Reading previous, Reading current)
    {
        var seconds = (current.Timestamp - previous.Timestamp).TotalSeconds;
        var writtenBytes = (double)Delta(previous.SectorsWritten, current.SectorsWritten) * SectorSize;

        return
        [
            new("read_bytes_per_second", Delta(previous.SectorsRead, current.SectorsRead) * SectorSize / seconds, MetricUnit.BytePerSecond),
            new("write_bytes_per_second", writtenBytes / seconds, MetricUnit.BytePerSecond),
            new("written_bytes", writtenBytes, MetricUnit.Byte),
            new("writes_per_second", Delta(previous.WritesCompleted, current.WritesCompleted) / seconds, MetricUnit.None),
            new("busy", Math.Min(100d, Delta(previous.MillisecondsBusy, current.MillisecondsBusy) / (seconds * 10)), MetricUnit.Percent),
        ];
    }

    /// <summary>
    /// A counter that wrapped counts as unchanged.
    /// </summary>
    private static double Delta(ulong previous, ulong current)
        => current >= previous ? current - previous : 0;

    private Dictionary<string, Reading> ReadDevices()
    {
        try
        {
            var timestamp = _timeProvider.GetUtcNow();
            var devices = fileSystem.File.ReadAllLines("/proc/diskstats")
                .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(parts => parts.Length >= 14)
                .ToList();
            return devices
                .Where(parts => IsWholePhysicalDisk(parts[2]))
                .ToDictionary(parts => parts[2], parts => new Reading(
                    timestamp,
                    SectorsRead: Parse(parts[5]),
                    WritesCompleted: Parse(parts[7]),
                    SectorsWritten: Parse(parts[9]),
                    MillisecondsBusy: Parse(parts[12])));
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException("Permission error reading /proc/diskstats.", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException("I/O error reading /proc/diskstats.", ex);
        }
    }

    /// <summary>
    /// A disk backed by hardware (it has a <c>device</c> link), neither a partition nor one of the boot and general purpose
    /// areas of an eMMC such as <c>mmcblk0boot0</c>.
    /// </summary>
    /// <remarks>
    /// Loop, RAM, device mapper (<c>dm-*</c>) and software RAID (<c>md*</c>) devices have no <c>device</c> link.
    /// </remarks>
    private bool IsWholePhysicalDisk(string name)
        => fileSystem.Directory.Exists($"{BlockDirectory}/{name}/device")
            && !fileSystem.File.Exists($"{BlockDirectory}/{name}/partition")
            && !s_emmcArea.IsMatch(name);

    private static ulong Parse(string value)
        => ulong.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);

    private sealed record Reading(DateTimeOffset Timestamp, ulong SectorsRead, ulong WritesCompleted, ulong SectorsWritten, ulong MillisecondsBusy);
}
