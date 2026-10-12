using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using AwesomeAssertions;
using AwesomeAssertions.Extensions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ViciOne.SystemMonitoring.Collectors;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public class DiskIoMetricsCollector_CollectMetrics
{
    [Fact]
    public async Task Returns_the_load_of_whole_physical_disks_over_the_interval()
    {
        var (collector, _) = await PreparedCollector();

        var metrics = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);

        metrics.Should().AllSatisfy(m =>
        {
            m.Group.Should().Be("disk_io");
            m.SubGroup.Should().Be("mmcblk0");
            m.IntervalInMinutes.Should().Be(1);
        });
        Values(metrics).Should().BeEquivalentTo(new Dictionary<string, double>
        {
            ["read_bytes_per_second"] = 1200 * 512 / 60d,
            ["write_bytes_per_second"] = 24000 * 512 / 60d,
            ["written_bytes"] = 24000 * 512d,
            ["writes_per_second"] = 2,
            ["busy"] = 1,
        });
    }

    [Fact]
    public async Task Tells_disks_from_partitions_and_virtual_devices_by_sysfs()
    {
        string[] names = ["sda", "sda1", "sdaa", "sdaa1", "dm-1", "dm-10", "md0", "loop0", "nvme0n1", "nvme0n1p1"];
        var fileSystem = Substitute.For<IFileSystem>();
        Sysfs(fileSystem, disks: ["sda", "sdaa", "nvme0n1"], partitions: ["sda1", "sdaa1", "nvme0n1p1"]);
        fileSystem.File.ReadAllLines("/proc/diskstats").Returns(
            [.. names.Select((name, i) => $"   8      {i} {name} 10 0 100 0 20 0 200 0 0 30 0 0 0 0 0 0 0")]);
        DiskIoMetricsCollector collector = new(fileSystem);
        await collector.PrepareMetrics(CancellationToken.None);

        var metrics = await collector.CollectLiveMetrics(CancellationToken.None);

        metrics.Select(m => m.SubGroup).Distinct().Should().BeEquivalentTo(["sda", "sdaa", "nvme0n1"]);
    }

    [Fact]
    public async Task Keeps_the_regular_value_while_read_live()
    {
        var (collector, timeProvider) = await PreparedCollector();
        await collector.CollectLiveMetrics(CancellationToken.None);
        timeProvider.Advance(1.Seconds());

        var metrics = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);

        Values(metrics)["written_bytes"].Should().Be(24000 * 512d);
    }

    [Fact]
    public async Task Returns_live_metrics_without_interval()
    {
        var (collector, _) = await PreparedCollector();

        var metrics = await collector.CollectLiveMetrics(CancellationToken.None);

        metrics.Should().NotBeEmpty().And.AllSatisfy(m => m.IntervalInMinutes.Should().Be(0));
    }

    [Fact]
    public async Task Adds_the_five_minute_aggregate_when_due()
    {
        var (collector, _) = await PreparedCollector();

        var metrics = await collector.CollectMetrics(5, DateTimeOffset.UtcNow, CancellationToken.None);

        metrics.Where(m => m.IntervalInMinutes == 5).Should().HaveCount(5)
            .And.Contain(m => m.Name == "busy" && ((AggregatedMetricValue)m.Value).Max == 1);
    }

    private static async Task<(DiskIoMetricsCollector Collector, FakeTimeProvider TimeProvider)> PreparedCollector()
    {
        var first = await File.ReadAllLinesAsync("./EdgeS/proc/diskstats", TestContext.Current.CancellationToken);
        var second = await File.ReadAllLinesAsync("./EdgeS/proc/diskstats2", TestContext.Current.CancellationToken);
        var fileSystem = Substitute.For<IFileSystem>();
        Sysfs(fileSystem, disks: ["mmcblk0", "mmcblk0boot0", "mmcblk0boot1", "mmcblk0gp0"], partitions: ["mmcblk0p1", "mmcblk0p2", "mmcblk0p3"]);
        fileSystem.File.ReadAllLines("/proc/diskstats").Returns(first);
        FakeTimeProvider timeProvider = new();
        DiskIoMetricsCollector collector = new(fileSystem, timeProvider);
        await collector.PrepareMetrics(CancellationToken.None);

        fileSystem.File.ReadAllLines("/proc/diskstats").Returns(second);
        timeProvider.Advance(60.Seconds());
        return (collector, timeProvider);
    }

    private static void Sysfs(IFileSystem fileSystem, string[] disks, string[] partitions)
    {
        foreach (var disk in disks.Concat(partitions))
            fileSystem.Directory.Exists($"/sys/class/block/{disk}/device").Returns(disks.Contains(disk));
        foreach (var partition in partitions)
            fileSystem.File.Exists($"/sys/class/block/{partition}/partition").Returns(true);
    }

    private static Dictionary<string, double> Values(IEnumerable<Metric> metrics)
        => metrics.Where(m => m.IntervalInMinutes == 1).ToDictionary(m => m.Name, m => (double)m.Value);
}
