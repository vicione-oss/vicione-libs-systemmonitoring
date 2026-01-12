using System.Globalization;
using System.IO.Abstractions;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring.Collectors;

[SupportedOSPlatform("linux")]
public sealed class NetworkMetricsCollector(IFileSystem fileSystem, ILogger<NetworkMetricsCollector> logger) : IScheduledMetricsCollector, IPrepareMetricsCollector
{
    private const string LoopbackInterfaceName = "lo";

    private readonly Dictionary<string, (RawNetworkStats Stats, DateTime LastUpdate)> _previousNetworkStats = [];
    private readonly Dictionary<string, List<LastMetricValue[]>> _fiveMinuteBuffers = [];
    private readonly Dictionary<string, List<LastMetricValue[]>> _fifteenMinuteBuffers = [];
    private readonly Lock _lock = new();

    public int IntervalInMinutes { get; } = 1;
    public string Name => "network";

    public Task<List<Metric>> CollectMetrics(int interval, DateTimeOffset cycleStartTime, CancellationToken cancellationToken)
    {
        try
        {
            var currentNetworkStats = CollectNetworkMetrics();
            var currentTimeStamp = DateTime.UtcNow;
            List<Metric> metrics = [];

            lock (_lock)
            {
                foreach (var (interfaceName, currentStats) in currentNetworkStats)
                {
                    if (!_previousNetworkStats.TryGetValue(interfaceName, out var value))
                    {
                        value = (currentStats, currentTimeStamp);
                        _previousNetworkStats[interfaceName] = value;
                        continue;
                    }

                    var (previousStats, lastUpdate) = value;
                    var timeDifference = (currentTimeStamp - lastUpdate).TotalSeconds;

                    var metric = NetworkStatsFactory.CreateNetworkStats(currentStats, previousStats, timeDifference);

                    UpdatePreviousValues(interfaceName, currentStats, currentTimeStamp);

                    var metricsData = CreateMetricsData(metric);
                    var bufferData = metricsData.ToArray();
                    UpdateBuffer(_fiveMinuteBuffers, interfaceName, bufferData);
                    UpdateBuffer(_fifteenMinuteBuffers, interfaceName, bufferData);
                    AppendAdditionalMetrics(metricsData, metric);
                    metrics.AddRange(metricsData.ToMetrics(Name, interfaceName, IntervalInMinutes, m => m.Value));

                    if (interval % 5 == 0)
                    {
                        metrics.AddRange(_fiveMinuteBuffers[interfaceName].AggregateBuffer().ToMetrics(Name, interfaceName, 5));
                        _fiveMinuteBuffers[interfaceName].Clear();
                    }

                    if (interval % 15 == 0)
                    {
                        metrics.AddRange(_fifteenMinuteBuffers[interfaceName].AggregateBuffer().ToMetrics(Name, interfaceName, 15));
                        _fifteenMinuteBuffers[interfaceName].Clear();
                    }
                }
            }

            return Task.FromResult(metrics);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting Network metrics.", ex);
        }
    }

    public Task PrepareMetrics(CancellationToken cancellationToken)
    {
        try
        {
            var metric = CollectNetworkMetrics();
            var timestamp = DateTime.UtcNow;

            lock (_lock)
            {
                foreach (var metricData in metric)
                    UpdatePreviousValues(metricData.Key, metricData.Value, timestamp);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting network metrics.", ex);
        }

        return Task.CompletedTask;
    }

    private Dictionary<string, RawNetworkStats> CollectNetworkMetrics()
    {
        var networkStats = new Dictionary<string, RawNetworkStats>();

        var lines = fileSystem.File.ReadAllLines("/proc/net/dev");

        foreach (var line in lines.Skip(2)) // Skip headers
        {
            var parts = line.Split([':', ' '], StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 17)
                continue;

            var interfaceName = parts[0];
            var stats = CatchIOException(() => ParseNetStats(parts), ex => throw ex);
            stats!.Duplex = CatchIOException(() => ReadDuplex(interfaceName), logger.LogCouldNotReadSpecificNetworkMetric);
            stats.Speed = CatchIOException(() => ReadSpeed(interfaceName), logger.LogCouldNotReadSpecificNetworkMetric);
            networkStats[interfaceName] = stats;
        }

        return networkStats;
    }

    private static T? CatchIOException<T>(Func<T> func, Action<Exception> onError)
    {
        try
        {
            return func();
        }
        catch (UnauthorizedAccessException ex)
        {
            onError(new InvalidOperationException("Permission error reading CPU metrics.", ex));
        }
        catch (IOException ex)
        {
            onError(new InvalidOperationException("I/O error reading CPU metrics.", ex));
        }
        catch (Exception ex)
        {
            onError(new InvalidOperationException("Error reading Network metrics.", ex));
        }

        return default;
    }

    private static RawNetworkStats ParseNetStats(string[] parts)
        => new()
        {
            RxBytes = ulong.Parse(parts[1], CultureInfo.InvariantCulture),
            RxPackets = ulong.Parse(parts[2], CultureInfo.InvariantCulture),
            RxErrors = ulong.Parse(parts[3], CultureInfo.InvariantCulture),
            RxDropped = ulong.Parse(parts[4], CultureInfo.InvariantCulture),
            TxBytes = ulong.Parse(parts[9], CultureInfo.InvariantCulture),
            TxPackets = ulong.Parse(parts[10], CultureInfo.InvariantCulture),
            TxErrors = ulong.Parse(parts[11], CultureInfo.InvariantCulture),
            TxDropped = ulong.Parse(parts[12], CultureInfo.InvariantCulture),
            Collisions = ulong.Parse(parts[14], CultureInfo.InvariantCulture),
            CarrierErrors = ulong.Parse(parts[15], CultureInfo.InvariantCulture)
        };

    private string? ReadDuplex(string interfaceName)
    {
        if (interfaceName == LoopbackInterfaceName)
            return null;

        return fileSystem.File.ReadAllText($"/sys/class/net/{interfaceName}/duplex").Replace('\n', ' ').Trim();
    }

    private int? ReadSpeed(string interfaceName)
    {
        if (interfaceName == LoopbackInterfaceName)
            return null;

        var speed = int.Parse(fileSystem.File.ReadAllText($"/sys/class/net/{interfaceName}/speed").Replace('\n', ' ').Trim(), CultureInfo.InvariantCulture);

        if (speed < 0)
            return null;

        return speed;
    }

    private void UpdatePreviousValues(string interfaceName, RawNetworkStats currentData, DateTime dateTime)
        => _previousNetworkStats[interfaceName] = (currentData, dateTime);

    private static List<LastMetricValue> CreateMetricsData(NetworkStats data)
        => new()
        {
            {new("collisions", data.Collisions, MetricUnit.None)},
            {new("carrier_errors", data.CarrierErrors, MetricUnit.None)},
            {new("rx_packets", data.ReceivedPackets, MetricUnit.None)},
            {new("rx_bytes", data.ReceivedBytes, MetricUnit.Byte)},
            {new("rx_dropped", data.ReceivedPacketsDropped, MetricUnit.None)},
            {new("rx_errors", data.ReceivedErrors, MetricUnit.None)},
            {new("tx_bytes", data.TransmittedBytes, MetricUnit.Byte)},
            {new("tx_packets", data.TransmittedPackets, MetricUnit.None)},
            {new("tx_dropped", data.TransmittedPacketsDropped, MetricUnit.None)},
            {new("tx_errors", data.TransmittedErrors, MetricUnit.None)},
            {new("rx_bandwidth", data.ReceiveBandwidth ?? throw new InvalidOperationException("Bandwidth was null"), MetricUnit.BytePerSecond) },
            {new("tx_bandwidth", data.TransmittBandwidth ?? throw new InvalidOperationException("Bandwidth was null"), MetricUnit.BytePerSecond) },
        };

    private static void AppendAdditionalMetrics(List<LastMetricValue> metrics, NetworkStats data)
    {
        if (data.Duplex != null)
            metrics.Add(new("duplex", data.Duplex, MetricUnit.None));
        if (data.ReceiveBandwidthUsage != null)
            metrics.Add(new("rx_bandwidth_usage", data.ReceiveBandwidthUsage, MetricUnit.Percent));
        if (data.TransmittBandwidthUsage != null)
            metrics.Add(new("tx_bandwidth_usage", data.TransmittBandwidthUsage, MetricUnit.Percent));
        if (data.SpeedMBps != null)
            metrics.Add(new("speed", data.SpeedMBps, MetricUnit.MegabitPerSecond));
        if (data.TotalBandwidthUsage != null)
            metrics.Add(new("total_bandwidth_usage", data.TotalBandwidthUsage, MetricUnit.Percent));
    }

    private static void UpdateBuffer(
        Dictionary<string, List<LastMetricValue[]>> buffers,
        string interfaceName,
        LastMetricValue[] metrics)
    {
        if (!buffers.ContainsKey(interfaceName))
            buffers[interfaceName] = [];

        buffers[interfaceName].Add(metrics);
    }
}
