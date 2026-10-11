using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using AwesomeAssertions;
using NSubstitute;
using ViciOne.SystemMonitoring.Collectors;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring;

[SuppressMessage("Interoperability", "CA1416:Plattformkompatibilität überprüfen")]
public class SwapMetricsCollector_CollectMetrics
{
    [Fact]
    public async Task Returns_the_current_size_and_the_pages_swapped_since_the_previous_reading()
    {
        var fileSystem = FileSystem(totalKb: 1_048_572, freeKb: 1_000_000, swapIn: 120, swapOut: 300);
        SwapMetricsCollector collector = new(fileSystem);
        await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);
        Swap(fileSystem, totalKb: 1_048_572, freeKb: 990_000, swapIn: 125, swapOut: 340);

        var metrics = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);

        Values(metrics).Should().BeEquivalentTo(new Dictionary<string, (double, string)>
        {
            ["total"] = (1_048_572, MetricUnit.Kilobyte),
            ["used"] = (58_572, MetricUnit.Kilobyte),
            ["swap_in"] = (5, MetricUnit.None),
            ["swap_out"] = (40, MetricUnit.None),
        });
    }

    [Fact]
    public async Task Reports_the_size_already_on_the_first_reading()
    {
        SwapMetricsCollector collector = new(FileSystem(totalKb: 1_048_572, freeKb: 1_000_000, swapIn: 120, swapOut: 300));

        var metrics = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);

        Values(metrics).Should().Contain("total", (1_048_572, MetricUnit.Kilobyte))
            .And.Contain("used", (48_572, MetricUnit.Kilobyte))
            .And.Contain("swap_in", (0, MetricUnit.None));
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
