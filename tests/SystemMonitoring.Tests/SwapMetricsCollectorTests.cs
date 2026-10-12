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
public class SwapMetricsCollector_CollectMetrics
{
    [Fact]
    public async Task Returns_the_current_size_and_the_pages_swapped_since_the_previous_reading()
    {
        var fileSystem = FileSystem(totalKb: 1_048_572, freeKb: 1_000_000, swapIn: 120, swapOut: 300);
        FakeTimeProvider timeProvider = new();
        SwapMetricsCollector collector = new(fileSystem, timeProvider);
        await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);
        Swap(fileSystem, totalKb: 1_048_572, freeKb: 990_000, swapIn: 125, swapOut: 340);
        timeProvider.Advance(60.Seconds());

        var metrics = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);

        Values(metrics).Should().BeEquivalentTo(new Dictionary<string, (double, string)>
        {
            ["total"] = (1_048_572, MetricUnit.Kilobyte),
            ["used"] = (58_572, MetricUnit.Kilobyte),
            ["swap_in"] = (5, MetricUnit.None),
            ["swap_out"] = (40, MetricUnit.None),
            ["swap_in_per_second"] = (0.08, MetricUnit.None),
            ["swap_out_per_second"] = (0.67, MetricUnit.None),
        });
    }

    [Fact]
    public async Task Reports_the_size_already_on_the_first_reading()
    {
        SwapMetricsCollector collector = new(FileSystem(totalKb: 1_048_572, freeKb: 1_000_000, swapIn: 120, swapOut: 300));

        var metrics = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);

        Values(metrics).Should().Contain("total", (1_048_572, MetricUnit.Kilobyte))
            .And.Contain("used", (48_572, MetricUnit.Kilobyte))
            .And.Contain("swap_in", (0, MetricUnit.None))
            .And.NotContainKeys("swap_in_per_second", "swap_out_per_second");
    }

    [Fact]
    public async Task Reports_a_device_without_swap_as_empty()
    {
        SwapMetricsCollector collector = new(FileSystem(totalKb: 0, freeKb: 0, swapIn: 0, swapOut: 0));

        var metrics = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);

        Values(metrics).Should().Contain("total", (0, MetricUnit.Kilobyte)).And.Contain("used", (0, MetricUnit.Kilobyte));
    }

    [Fact]
    public async Task Reports_the_pages_swapped_per_second_since_the_previous_live_reading()
    {
        var fileSystem = FileSystem(totalKb: 1_048_572, freeKb: 1_000_000, swapIn: 120, swapOut: 300);
        FakeTimeProvider timeProvider = new();
        SwapMetricsCollector collector = new(fileSystem, timeProvider);
        await collector.CollectLiveMetrics(CancellationToken.None);
        Swap(fileSystem, totalKb: 1_048_572, freeKb: 990_000, swapIn: 125, swapOut: 340);
        timeProvider.Advance(2.Seconds());

        var live = await collector.CollectLiveMetrics(CancellationToken.None);

        live.Single(m => m.Name == "swap_out_per_second").Value.Should().Be(20d);
    }

    [Fact]
    public async Task Keeps_the_pages_of_the_interval_while_read_live()
    {
        var fileSystem = FileSystem(totalKb: 1_048_572, freeKb: 1_000_000, swapIn: 120, swapOut: 300);
        SwapMetricsCollector collector = new(fileSystem);
        await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);
        await collector.CollectLiveMetrics(CancellationToken.None);
        Swap(fileSystem, totalKb: 1_048_572, freeKb: 990_000, swapIn: 125, swapOut: 340);

        var live = await collector.CollectLiveMetrics(CancellationToken.None);
        var regular = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);

        live.Should().AllSatisfy(m => m.IntervalInMinutes.Should().Be(0));
        live.Single(m => m.Name == "swap_out").Value.Should().Be(40d);
        Values(regular)["swap_out"].Should().Be((40, MetricUnit.None));
    }

    private static IFileSystem FileSystem(double totalKb, double freeKb, int swapIn, int swapOut)
    {
        var fileSystem = Substitute.For<IFileSystem>();
        Swap(fileSystem, totalKb, freeKb, swapIn, swapOut);
        return fileSystem;
    }

    private static void Swap(IFileSystem fileSystem, double totalKb, double freeKb, int swapIn, int swapOut)
    {
        fileSystem.File.ReadAllLines("/proc/meminfo").Returns([$"SwapTotal:     {totalKb} kB", $"SwapFree:      {freeKb} kB"]);
        fileSystem.File.ReadAllLines("/proc/vmstat").Returns([$"pswpin {swapIn}", $"pswpout {swapOut}"]);
    }

    private static Dictionary<string, (double, string)> Values(IEnumerable<Metric> metrics)
        => metrics.Where(m => m.IntervalInMinutes == 1).ToDictionary(m => m.Name, m => ((double)m.Value, m.Unit));
}
