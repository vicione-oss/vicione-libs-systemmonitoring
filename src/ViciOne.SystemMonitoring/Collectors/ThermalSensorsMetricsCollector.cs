using System.Globalization;
using System.IO.Abstractions;
using System.Runtime.Versioning;
using Microsoft.Extensions.Options;
using ViciOne.SystemMonitoring.Configuration;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring.Collectors;

[SupportedOSPlatform("linux")]
public sealed class ThermalSensorsMetricsCollector(IOptionsMonitor<MonitoringConfig> options, IFileSystem fileSystem) : IScheduledMetricsCollector, ILiveMetricsCollector
{
    private readonly List<List<LastMetricValue>> _fiveMinuteBuffer = [];
    private readonly List<List<LastMetricValue>> _fifteenMinuteBuffer = [];
    private readonly Lock _lock = new();

    public int IntervalInMinutes { get; } = 1;
    public string Name => "thermal_sensors";

    public Task<List<Metric>> CollectMetrics(int interval, DateTimeOffset cycleStartTime, CancellationToken cancellationToken)
    {
        try
        {
            var sensorData = CollectThermalAndFanMetrics(options.CurrentValue.NodeType);

            lock (_lock)
            {
                var metricsData = CreateMetricsData(sensorData);
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
            throw new InvalidOperationException("Error collecting Thermal Sensor metrics.", ex);
        }
    }

    public Task<List<Metric>> CollectLiveMetrics(LiveWindow window, CancellationToken cancellationToken)
    {
        try
        {
            var sensorData = CollectThermalAndFanMetrics(options.CurrentValue.NodeType);
            return Task.FromResult(CreateMetricsData(sensorData).ToMetrics(Name, null, 0, m => m.Value).ToList());
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting live Thermal Sensor metrics.", ex);
        }
    }

    private Dictionary<string, double> CollectThermalAndFanMetrics(NodeType nodeType)
    {
        var metrics = new Dictionary<string, double>();

        try
        {
            switch (nodeType)
            {
                case NodeType.Unknown:
                case NodeType.DefaultDebian:
                    var hwmonPaths = fileSystem.Directory.GetDirectories("/sys/class/hwmon");

                    foreach (var hwmonPath in hwmonPaths)
                    {
                        var nameFilePath = fileSystem.Path.Combine(hwmonPath, "name");
                        var sensorName = fileSystem.File.Exists(nameFilePath) ? File.ReadAllText(nameFilePath).Trim() : "unknown";

                        var tempFiles = fileSystem.Directory.GetFiles(hwmonPath, "temp*_input");
                        foreach (var tempFile in tempFiles)
                        {
                            var tempName = tempFile.Replace("_input", "_label", StringComparison.InvariantCulture);
                            var tempLabel = fileSystem.File.Exists(tempName) ? fileSystem.File.ReadAllText(tempName).Trim() : "temp";

                            if (double.TryParse(fileSystem.File.ReadAllText(tempFile).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var tempValue))
                                metrics[$"{sensorName}_{tempLabel}"] = tempValue / 1000.0; // Convert millidegree to degree
                        }

                        var fanFiles = fileSystem.Directory.GetFiles(hwmonPath, "fan*_input");
                        foreach (var fanFile in fanFiles)
                        {
                            var fanLabel = fanFile.Replace("_input", "_label", StringComparison.InvariantCulture);
                            var fanName = fileSystem.File.Exists(fanLabel) ? fileSystem.File.ReadAllText(fanLabel).Trim() : "fan";

                            if (double.TryParse(fileSystem.File.ReadAllText(fanFile).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var fanValue))
                                metrics[$"{sensorName}_{fanName}"] = fanValue;
                        }
                    }
                    break;

                case NodeType.RaspberryPi3:
                case NodeType.RaspberryPi4:
                case NodeType.RaspberryPi5:

                    var cpuTempPath = "/sys/class/thermal/thermal_zone0/temp";
                    if (fileSystem.File.Exists(cpuTempPath) && double.TryParse(fileSystem.File.ReadAllText(cpuTempPath).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var cpuTemp))
                        metrics["RaspberryPi_CPU_Temperature"] = cpuTemp / 1000.0;
                    else
                        metrics["RaspberryPi_CPU_Temperature"] = -1;


                    var mainboardTempPath = "/sys/class/thermal/thermal_zone1/temp";
                    if (fileSystem.File.Exists(mainboardTempPath) && double.TryParse(fileSystem.File.ReadAllText(mainboardTempPath).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var mainboardTemp))
                        metrics["RaspberryPi_Mainboard_Temperature"] = mainboardTemp / 1000.0;
                    else
                        metrics["RaspberryPi_Mainboard_Temperature"] = -1;


                    var fanSpeedPath = "/sys/class/hwmon/hwmon0/fan1_input";
                    if (fileSystem.File.Exists(fanSpeedPath) && double.TryParse(fileSystem.File.ReadAllText(fanSpeedPath).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var fanSpeed))
                        metrics["RaspberryPi_CPU_Fan_Speed"] = fanSpeed;
                    else
                        metrics["RaspberryPi_CPU_Fan_Speed"] = -1;

                    break;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error reading thermal or fan metrics.", ex);
        }

        return metrics;
    }

    private static List<LastMetricValue> CreateMetricsData(Dictionary<string, double> sensorData)
        => [.. sensorData.Select(kv => new LastMetricValue(kv.Key, kv.Value, kv.Key.Contains("temp", StringComparison.InvariantCultureIgnoreCase) ? MetricUnit.DegreeCelsius : MetricUnit.None))];

    private static void UpdateBuffer(List<List<LastMetricValue>> buffer, List<LastMetricValue> metrics)
    {
        if (metrics.Count > 0)
            buffer.Add(metrics);
    }
}
