using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using AwesomeAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ViciOne.SystemMonitoring.Collectors;

namespace ViciOne.SystemMonitoring;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public class ThermalZoneMetricsCollector_CollectMetrics
{
    private const string ThermalDirectory = "/sys/class/thermal";
    private const string Zone0 = $"{ThermalDirectory}/thermal_zone0";

    [Fact]
    public async Task Returns_the_temperature_trip_points_and_throttling_of_each_zone()
    {
        ThermalZoneMetricsCollector collector = new(EdgeS());

        var metrics = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);

        metrics.Should().AllSatisfy(m =>
        {
            m.Group.Should().Be("thermal_zones");
            m.IntervalInMinutes.Should().Be(1);
        });
        metrics.Select(m => (m.SubGroup, m.Name, (double)m.Value, m.Unit)).Should().BeEquivalentTo(
        [
            ("cpu-thermal", "temperature", 42d, MetricUnit.DegreeCelsius),
            ("cpu-thermal", "passive_trip", 85d, MetricUnit.DegreeCelsius),
            ("cpu-thermal", "critical_trip", 95d, MetricUnit.DegreeCelsius),
            ("cpu-thermal", "throttling", 0d, MetricUnit.Percent),
        ]);
    }

    [Fact]
    public async Task Reports_the_state_of_a_passive_cooling_device_as_its_share_of_the_maximum()
    {
        var fileSystem = EdgeS();
        fileSystem.File.ReadAllText($"{Zone0}/cdev0/cur_state").Returns("1\n");
        ThermalZoneMetricsCollector collector = new(fileSystem);

        var metrics = await collector.CollectLiveMetrics(CancellationToken.None);

        metrics.Single(m => m.Name == "throttling").Value.Should().Be(100d);
    }

    [Fact]
    public async Task Does_not_count_a_fan_on_an_active_trip_as_throttling()
    {
        var fileSystem = EdgeS();
        Trip(fileSystem, Zone0, 2, "active", "60000");
        fileSystem.Directory.GetFiles(Zone0, "cdev*_trip_point").Returns([$"{Zone0}/cdev0_trip_point", $"{Zone0}/cdev1_trip_point"]);
        CoolingDevice(fileSystem, Zone0, 1, trip: "2", state: "3", maximum: "4");
        ThermalZoneMetricsCollector collector = new(fileSystem);

        var metrics = await collector.CollectLiveMetrics(CancellationToken.None);

        metrics.Single(m => m.Name == "throttling").Value.Should().Be(0d);
    }

    [Fact]
    public async Task Leaves_out_throttling_of_a_cooling_device_without_states()
    {
        var fileSystem = EdgeS();
        fileSystem.File.ReadAllText($"{Zone0}/cdev0/max_state").Returns("0\n");
        ThermalZoneMetricsCollector collector = new(fileSystem);

        var metrics = await collector.CollectLiveMetrics(CancellationToken.None);

        metrics.Select(m => m.Name).Should().BeEquivalentTo(["temperature", "passive_trip", "critical_trip"]);
    }

    [Fact]
    public async Task Ignores_a_trip_point_the_kernel_marks_as_unset()
    {
        var fileSystem = EdgeS();
        Trip(fileSystem, Zone0, 2, "passive", "-274000");
        ThermalZoneMetricsCollector collector = new(fileSystem);

        var metrics = await collector.CollectLiveMetrics(CancellationToken.None);

        metrics.Single(m => m.Name == "passive_trip").Value.Should().Be(85d);
    }

    [Fact]
    public async Task Leaves_out_what_a_zone_does_not_have()
    {
        var fileSystem = Zones(Zone0);
        Zone(fileSystem, Zone0, "cpu-thermal", "58426");
        Trip(fileSystem, Zone0, 0, "critical", "110000");
        ThermalZoneMetricsCollector collector = new(fileSystem);

        var metrics = await collector.CollectLiveMetrics(CancellationToken.None);

        metrics.Select(m => m.Name).Should().BeEquivalentTo(["temperature", "critical_trip"]);
    }

    [Fact]
    public async Task Skips_a_disabled_zone()
    {
        var fileSystem = Zones(Zone0, $"{ThermalDirectory}/thermal_zone1");
        Zone(fileSystem, Zone0, "cpu-thermal", "42000");
        Zone(fileSystem, $"{ThermalDirectory}/thermal_zone1", "gpu-thermal", "41000", mode: "disabled");
        ThermalZoneMetricsCollector collector = new(fileSystem);

        var metrics = await collector.CollectLiveMetrics(CancellationToken.None);

        metrics.Select(m => m.SubGroup).Should().OnlyContain(s => s == "cpu-thermal");
    }

    [Fact]
    public async Task Skips_a_zone_whose_temperature_cannot_be_read()
    {
        var fileSystem = Zones(Zone0, $"{ThermalDirectory}/thermal_zone1");
        Zone(fileSystem, Zone0, "cpu-thermal", "42000");
        Zone(fileSystem, $"{ThermalDirectory}/thermal_zone1", "iwlwifi_1", "0");
        fileSystem.File.ReadAllText($"{ThermalDirectory}/thermal_zone1/temp").Throws(new IOException("No data available"));
        ThermalZoneMetricsCollector collector = new(fileSystem);

        var metrics = await collector.CollectLiveMetrics(CancellationToken.None);

        metrics.Select(m => m.SubGroup).Should().OnlyContain(s => s == "cpu-thermal");
    }

    [Fact]
    public async Task Skips_a_zone_whose_cooling_device_cannot_be_read()
    {
        var fileSystem = EdgeS();
        var zone1 = $"{ThermalDirectory}/thermal_zone1";
        fileSystem.Directory.GetDirectories(ThermalDirectory, "thermal_zone*").Returns([Zone0, zone1]);
        Zone(fileSystem, zone1, "gpu-thermal", "41000");
        Trip(fileSystem, zone1, 0, "passive", "80000");
        fileSystem.Directory.GetFiles(zone1, "cdev*_trip_point").Returns([$"{zone1}/cdev0_trip_point"]);
        CoolingDevice(fileSystem, zone1, 0, trip: "0", state: "0", maximum: "1");
        fileSystem.File.ReadAllText($"{zone1}/cdev0/cur_state").Throws(new IOException("Input/output error"));
        ThermalZoneMetricsCollector collector = new(fileSystem);

        var metrics = await collector.CollectLiveMetrics(CancellationToken.None);

        metrics.Select(m => m.SubGroup).Should().OnlyContain(s => s == "cpu-thermal");
    }

    [Fact]
    public async Task Skips_a_zone_that_went_away_since_the_listing()
    {
        var fileSystem = EdgeS();
        var zone1 = $"{ThermalDirectory}/thermal_zone1";
        fileSystem.Directory.GetDirectories(ThermalDirectory, "thermal_zone*").Returns([Zone0, zone1]);
        fileSystem.File.ReadAllText($"{zone1}/type").Throws(new FileNotFoundException());
        ThermalZoneMetricsCollector collector = new(fileSystem);

        var metrics = await collector.CollectLiveMetrics(CancellationToken.None);

        metrics.Select(m => m.SubGroup).Should().OnlyContain(s => s == "cpu-thermal");
    }

    [Fact]
    public async Task Keeps_the_zone_number_when_a_zone_of_the_same_type_is_disabled()
    {
        var fileSystem = Zones(Zone0, $"{ThermalDirectory}/thermal_zone1");
        Zone(fileSystem, Zone0, "acpitz", "40000", mode: "disabled");
        Zone(fileSystem, $"{ThermalDirectory}/thermal_zone1", "acpitz", "45000");
        ThermalZoneMetricsCollector collector = new(fileSystem);

        var metrics = await collector.CollectLiveMetrics(CancellationToken.None);

        metrics.Select(m => m.SubGroup).Should().Equal("acpitz_1");
    }

    [Fact]
    public async Task Numbers_zones_that_share_a_type()
    {
        var fileSystem = Zones($"{ThermalDirectory}/thermal_zone1", Zone0, $"{ThermalDirectory}/cooling_device0");
        Zone(fileSystem, Zone0, "acpitz", "40000");
        Zone(fileSystem, $"{ThermalDirectory}/thermal_zone1", "acpitz", "45000");
        ThermalZoneMetricsCollector collector = new(fileSystem);

        var metrics = await collector.CollectLiveMetrics(CancellationToken.None);

        metrics.Select(m => (m.SubGroup, (double)m.Value)).Should().Equal(("acpitz_0", 40d), ("acpitz_1", 45d));
    }

    [Fact]
    public async Task Returns_nothing_without_thermal_zones()
    {
        ThermalZoneMetricsCollector collector = new(Substitute.For<IFileSystem>());

        var metrics = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);

        metrics.Should().BeEmpty();
    }

    [Fact]
    public async Task Returns_live_metrics_without_interval()
    {
        ThermalZoneMetricsCollector collector = new(EdgeS());

        var metrics = await collector.CollectLiveMetrics(CancellationToken.None);

        metrics.Should().HaveCount(4).And.AllSatisfy(m => m.IntervalInMinutes.Should().Be(0));
    }

    [Fact]
    public async Task Adds_the_five_minute_aggregate_when_due()
    {
        ThermalZoneMetricsCollector collector = new(EdgeS());

        var metrics = await collector.CollectMetrics(5, DateTimeOffset.UtcNow, CancellationToken.None);

        metrics.Where(m => m.IntervalInMinutes == 5).Should().HaveCount(4);
    }

    /// <summary>
    /// The thermal zone of an Edge-S: passive trip at 85 °C with the cpufreq cooling device bound to it, critical at 95 °C.
    /// </summary>
    private static IFileSystem EdgeS()
    {
        var fileSystem = Zones(Zone0);
        Zone(fileSystem, Zone0, "cpu-thermal", "42000");
        Trip(fileSystem, Zone0, 0, "passive", "85000");
        Trip(fileSystem, Zone0, 1, "critical", "95000");
        fileSystem.Directory.GetFiles(Zone0, "cdev*_trip_point").Returns([$"{Zone0}/cdev0_trip_point"]);
        CoolingDevice(fileSystem, Zone0, 0, trip: "0", state: "0", maximum: "1");
        return fileSystem;
    }

    private static IFileSystem Zones(params string[] directories)
    {
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.Directory.Exists(ThermalDirectory).Returns(true);
        fileSystem.Directory.GetDirectories(ThermalDirectory, "thermal_zone*").Returns(directories);
        return fileSystem;
    }

    private static void Zone(IFileSystem fileSystem, string zone, string type, string milliCelsius, string mode = "enabled")
    {
        fileSystem.File.Exists($"{zone}/mode").Returns(true);
        fileSystem.File.ReadAllText($"{zone}/mode").Returns(mode + "\n");
        fileSystem.File.ReadAllText($"{zone}/type").Returns(type + "\n");
        fileSystem.File.ReadAllText($"{zone}/temp").Returns(milliCelsius + "\n");
    }

    private static void Trip(IFileSystem fileSystem, string zone, int number, string type, string milliCelsius)
    {
        var types = fileSystem.Directory.GetFiles(zone, "trip_point_*_type");
        fileSystem.Directory.GetFiles(zone, "trip_point_*_type").Returns([.. types, $"{zone}/trip_point_{number}_type"]);
        fileSystem.File.ReadAllText($"{zone}/trip_point_{number}_type").Returns(type + "\n");
        fileSystem.File.ReadAllText($"{zone}/trip_point_{number}_temp").Returns(milliCelsius + "\n");
    }

    private static void CoolingDevice(IFileSystem fileSystem, string zone, int number, string trip, string state, string maximum)
    {
        fileSystem.File.ReadAllText($"{zone}/cdev{number}_trip_point").Returns(trip + "\n");
        fileSystem.File.ReadAllText($"{zone}/cdev{number}/cur_state").Returns(state + "\n");
        fileSystem.File.ReadAllText($"{zone}/cdev{number}/max_state").Returns(maximum + "\n");
    }
}
