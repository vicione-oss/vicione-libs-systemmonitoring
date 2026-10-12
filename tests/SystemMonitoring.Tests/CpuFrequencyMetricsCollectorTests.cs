using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using AwesomeAssertions;
using NSubstitute;
using ViciOne.SystemMonitoring.Collectors;

namespace ViciOne.SystemMonitoring;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public class CpuFrequencyMetricsCollector_CollectMetrics
{
    private const string CpuDirectory = "/sys/devices/system/cpu";

    [Fact]
    public async Task Returns_the_clock_per_core_and_in_total()
    {
        CpuFrequencyMetricsCollector collector = new(EdgeS(cpu0Khz: "1400000", cpu1Khz: "1200000"));

        var metrics = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);

        metrics.Should().AllSatisfy(m => m.Group.Should().Be("cpu_frequency"));
        metrics.Select(m => (m.SubGroup, m.Name, (double)m.Value, m.Unit)).Should().BeEquivalentTo(
        [
            ("core0", "current", 1400d, MetricUnit.Megahertz),
            ("core0", "limit", 1400d, MetricUnit.Megahertz),
            ("core0", "maximum", 1400d, MetricUnit.Megahertz),
            ("core1", "current", 1200d, MetricUnit.Megahertz),
            ("core1", "limit", 1400d, MetricUnit.Megahertz),
            ("core1", "maximum", 1400d, MetricUnit.Megahertz),
            ("total", "current", 1300d, MetricUnit.Megahertz),
            ("total", "maximum", 1400d, MetricUnit.Megahertz),
            ("total", "share_of_maximum", (100 + (1200 / 1400d * 100)) / 2, MetricUnit.Percent),
            ("total", "limit_share", 100d, MetricUnit.Percent),
        ]);
    }

    [Fact]
    public async Task Reports_the_lowered_limit_of_a_core()
    {
        var fileSystem = EdgeS(cpu0Khz: "600000", cpu1Khz: "600000");
        fileSystem.File.ReadAllText($"{CpuDirectory}/cpu1/cpufreq/scaling_max_freq").Returns("800000\n");
        CpuFrequencyMetricsCollector collector = new(fileSystem);

        var metrics = await collector.CollectLiveMetrics(CancellationToken.None);

        metrics.Single(m => m.SubGroup == "core1" && m.Name == "limit").Value.Should().Be(800d);
        metrics.Single(m => m.SubGroup == "total" && m.Name == "limit_share").Value.Should().Be(800 / 1400d * 100);
    }

    [Fact]
    public async Task Compares_fast_and_slow_cores_each_with_its_own_maximum()
    {
        var fileSystem = EdgeS(cpu0Khz: "1000000", cpu1Khz: "1000000");
        fileSystem.File.ReadAllText($"{CpuDirectory}/cpu1/cpufreq/scaling_max_freq").Returns("1000000\n");
        fileSystem.File.ReadAllText($"{CpuDirectory}/cpu1/cpufreq/cpuinfo_max_freq").Returns("1000000\n");
        CpuFrequencyMetricsCollector collector = new(fileSystem);

        var metrics = await collector.CollectLiveMetrics(CancellationToken.None);

        metrics.Single(m => m.SubGroup == "total" && m.Name == "limit_share").Value.Should().Be(100d);
        metrics.Single(m => m.SubGroup == "total" && m.Name == "share_of_maximum").Value.Should().Be(((1000 / 1400d * 100) + 100) / 2);
    }

    [Fact]
    public async Task Returns_nothing_without_cpufreq()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.Directory.Exists(CpuDirectory).Returns(true);
        fileSystem.Directory.GetDirectories(CpuDirectory, "cpu*").Returns([$"{CpuDirectory}/cpu0"]);
        CpuFrequencyMetricsCollector collector = new(fileSystem);

        var metrics = await collector.CollectLiveMetrics(CancellationToken.None);

        metrics.Should().BeEmpty();
    }

    [Fact]
    public async Task Returns_live_metrics_without_interval()
    {
        CpuFrequencyMetricsCollector collector = new(EdgeS(cpu0Khz: "1400000", cpu1Khz: "1400000"));

        var metrics = await collector.CollectLiveMetrics(CancellationToken.None);

        metrics.Should().HaveCount(10).And.AllSatisfy(m => m.IntervalInMinutes.Should().Be(0));
    }

    /// <summary>
    /// The cpufreq layout of an Edge-S, two cores between 1.2 and 1.4 GHz besides the cpufreq and cpuidle directories.
    /// </summary>
    private static IFileSystem EdgeS(string cpu0Khz, string cpu1Khz)
    {
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.Directory.Exists(CpuDirectory).Returns(true);
        fileSystem.Directory.GetDirectories(CpuDirectory, "cpu*")
            .Returns([$"{CpuDirectory}/cpu1", $"{CpuDirectory}/cpu0", $"{CpuDirectory}/cpufreq", $"{CpuDirectory}/cpuidle"]);

        foreach (var (core, current) in new[] { ("cpu0", cpu0Khz), ("cpu1", cpu1Khz) })
        {
            fileSystem.File.Exists($"{CpuDirectory}/{core}/cpufreq/scaling_cur_freq").Returns(true);
            fileSystem.File.ReadAllText($"{CpuDirectory}/{core}/cpufreq/scaling_cur_freq").Returns(current + "\n");
            fileSystem.File.ReadAllText($"{CpuDirectory}/{core}/cpufreq/scaling_max_freq").Returns("1400000\n");
            fileSystem.File.ReadAllText($"{CpuDirectory}/{core}/cpufreq/cpuinfo_max_freq").Returns("1400000\n");
        }

        return fileSystem;
    }
}
