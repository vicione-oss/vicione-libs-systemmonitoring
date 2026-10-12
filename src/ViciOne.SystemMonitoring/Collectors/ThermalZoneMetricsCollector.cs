using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Abstractions;
using System.Runtime.Versioning;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring.Collectors;

/// <summary>
/// Temperature, trip points and throttling of each kernel thermal zone.
/// </summary>
/// <remarks>
/// <c>throttling</c> covers only the cooling devices bound to a passive trip, such as cpufreq; a fan on an active trip does not
/// throttle. Firmware that throttles on its own, as on a Raspberry Pi, binds no cooling device, so the value is missing there.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class ThermalZoneMetricsCollector(IFileSystem fileSystem) : IScheduledMetricsCollector, ILiveMetricsCollector
{
    private const string ThermalDirectory = "/sys/class/thermal";

    /// <summary>
    /// The kernel reports an unset trip point below it, as <c>THERMAL_TEMP_INVALID</c> (-274 °C).
    /// </summary>
    private const double AbsoluteZero = -273.15;

    private readonly Lock _lock = new();
    private readonly AggregationBuffers _buffers = new("thermal_zones");

    /// <summary>
    /// The last type read per zone number; regular and live readings may run at the same time.
    /// </summary>
    private readonly ConcurrentDictionary<int, string> _types = new();

    public int IntervalInMinutes { get; } = 1;
    public string Name => "thermal_zones";

    public Task<List<Metric>> CollectMetrics(int interval, DateTimeOffset cycleStartTime, CancellationToken cancellationToken)
    {
        try
        {
            var valuesBySubGroup = ReadZones();

            lock (_lock)
            {
                List<Metric> metrics = [.. valuesBySubGroup.SelectMany(v => v.Value.ToMetrics(Name, v.Key, IntervalInMinutes, m => m.Value))];
                metrics.AddRange(_buffers.Add(interval, valuesBySubGroup));
                return Task.FromResult(metrics);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting thermal zone metrics.", ex);
        }
    }

    public Task<List<Metric>> CollectLiveMetrics(CancellationToken cancellationToken)
    {
        try
        {
            return Task.FromResult<List<Metric>>([.. ReadZones().SelectMany(v => v.Value.ToMetrics(Name, v.Key, 0, m => m.Value))]);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting live thermal zone metrics.", ex);
        }
    }

    /// <summary>
    /// Values per enabled zone, named by its type; zones that share a type get their number appended, decided over all zones so
    /// that disabling one does not rename another.
    /// </summary>
    private Dictionary<string, List<LastMetricValue>> ReadZones()
    {
        try
        {
            if (!fileSystem.Directory.Exists(ThermalDirectory))
                return [];

            var zones = fileSystem.Directory.GetDirectories(ThermalDirectory, "thermal_zone*")
                .Select(path => (Path: path, Number: Number(Path.GetFileName(path), "thermal_zone", "")))
                .Where(zone => zone.Number is not null)
                .OrderBy(zone => zone.Number)
                .Select(zone => (zone.Path, Number: zone.Number!.Value, Type: TypeOf(zone.Path, zone.Number.Value)))
                .ToList();

            Dictionary<string, List<LastMetricValue>> valuesBySubGroup = [];
            foreach (var (path, number, type) in zones)
            {
                if (type is null || ReadZone(path) is not { } values)
                    continue;

                var name = zones.Count(z => z.Type == type) > 1 ? $"{type}_{number}" : type;
                valuesBySubGroup[ToSubGroup(name)] = values;
            }

            return valuesBySubGroup;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException("Permission error reading the thermal zones.", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException("I/O error reading the thermal zones.", ex);
        }
    }

    /// <summary>
    /// The type of the zone, or the one read before when it cannot be read now, so that a zone whose type fails once does not
    /// rename the others of its type; null when it was never read.
    /// </summary>
    private string? TypeOf(string zone, int number)
    {
        if (ReadType(zone) is { } type)
            return _types[number] = type;

        return _types.GetValueOrDefault(number);
    }

    /// <summary>
    /// The zone type as a subgroup, which becomes a level of the MQTT topic: <c>/</c> is replaced by <c>\</c> as for a mount
    /// point, and the wildcards <c>+</c> and <c>#</c> by <c>_</c>.
    /// </summary>
    private static string ToSubGroup(string name)
        => name.Replace('/', '\\').Replace('+', '_').Replace('#', '_');

    /// <summary>
    /// Null for a zone that went away since the directory was listed or whose type may not be read.
    /// </summary>
    private string? ReadType(string zone)
    {
        try
        {
            return ReadText($"{zone}/type");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Null for a disabled zone and for one that cannot be read, such as the zone of a switched-off Wi-Fi adapter, one that
    /// went away since the directory was listed or one that may not be read.
    /// </summary>
    private List<LastMetricValue>? ReadZone(string zone)
    {
        try
        {
            if (IsDisabled(zone))
                return null;

            List<LastMetricValue> values = [new("temperature", ReadCelsius($"{zone}/temp"), MetricUnit.DegreeCelsius)];

            var trips = ReadTrips(zone);
            if (LowestTrip(trips, "passive") is { } passive)
                values.Add(new("passive_trip", passive, MetricUnit.DegreeCelsius));

            if (LowestTrip(trips, "critical") is { } critical)
                values.Add(new("critical_trip", critical, MetricUnit.DegreeCelsius));

            if (ReadThrottling(zone, trips) is { } throttling)
                values.Add(new("throttling", throttling, MetricUnit.Percent));

            return values;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// Type and temperature of each trip point by its number.
    /// </summary>
    private Dictionary<int, (string Type, double Celsius)> ReadTrips(string zone)
        => fileSystem.Directory.GetFiles(zone, "trip_point_*_type")
            .Select(path => Number(Path.GetFileName(path), "trip_point_", "_type"))
            .OfType<int>()
            .ToDictionary(number => number, number => (
                ReadText($"{zone}/trip_point_{number}_type"),
                ReadCelsius($"{zone}/trip_point_{number}_temp")));

    private static double? LowestTrip(Dictionary<int, (string Type, double Celsius)> trips, string type)
        => trips.Values.Where(t => t.Type == type && t.Celsius >= AbsoluteZero).Select(t => (double?)t.Celsius).Min();

    /// <summary>
    /// The highest state, as a share of its maximum, among the cooling devices bound to a passive trip; null when none is bound
    /// or none has more than one state.
    /// </summary>
    /// <remarks>
    /// A state is a step of the device, for cpufreq an operating point, not a share of the clock. A device bound to several
    /// zones has one state for all of them, set by the hottest.
    /// </remarks>
    private double? ReadThrottling(string zone, Dictionary<int, (string Type, double Celsius)> trips)
        => fileSystem.Directory.GetFiles(zone, "cdev*_trip_point")
            .Where(binding => trips.TryGetValue(ReadInteger(binding), out var trip) && trip.Type == "passive")
            .Select(binding => binding[..^"_trip_point".Length])
            .Select(device => (State: ReadInteger($"{device}/cur_state"), Maximum: ReadInteger($"{device}/max_state")))
            .Where(device => device.Maximum > 0)
            .Max(device => (double?)device.State / device.Maximum * 100);

    private bool IsDisabled(string zone)
        => fileSystem.File.Exists($"{zone}/mode") && ReadText($"{zone}/mode") == "disabled";

    private static int? Number(string name, string prefix, string suffix)
        => name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(suffix, StringComparison.Ordinal)
            && int.TryParse(name.AsSpan(prefix.Length, name.Length - prefix.Length - suffix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;

    private string ReadText(string path)
        => fileSystem.File.ReadAllText(path).Trim();

    private int ReadInteger(string path)
        => int.Parse(ReadText(path), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    /// <summary>
    /// The thermal sysfs reports millidegrees.
    /// </summary>
    private double ReadCelsius(string path)
        => ReadInteger(path) / 1000d;
}
