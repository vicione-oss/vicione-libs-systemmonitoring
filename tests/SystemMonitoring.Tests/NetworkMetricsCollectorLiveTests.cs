using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ViciOne.SystemMonitoring.Collectors;

namespace ViciOne.SystemMonitoring;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public class NetworkMetricsCollector_CollectLiveMetrics
{
    [Fact]
    public async Task Leaves_the_regular_bytes_covering_the_whole_interval()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        NetworkMetricsCollector collector = new(fileSystem, Substitute.For<ILogger<NetworkMetricsCollector>>());
        Dev(fileSystem, 1_000_000);
        await collector.PrepareMetrics(CancellationToken.None);
        Dev(fileSystem, 1_300_000);
        await collector.CollectLiveMetrics(new(), CancellationToken.None);
        Dev(fileSystem, 1_600_000);

        var metrics = await collector.CollectMetrics(1, DateTime.UtcNow, CancellationToken.None);

        metrics.Single(m => m.SubGroup == "wlan0" && m.Name == "rx_bytes").Value.Should().Be(600_000d);
    }

    [Fact]
    public async Task Reports_the_bytes_since_the_previous_read_of_the_same_window()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        NetworkMetricsCollector collector = new(fileSystem, Substitute.For<ILogger<NetworkMetricsCollector>>());
        LiveWindow often = new();
        LiveWindow seldom = new();
        Dev(fileSystem, 1_000_000);
        await collector.CollectLiveMetrics(often, CancellationToken.None);
        await collector.CollectLiveMetrics(seldom, CancellationToken.None);
        Dev(fileSystem, 1_300_000);
        await collector.CollectLiveMetrics(often, CancellationToken.None);
        Dev(fileSystem, 1_600_000);

        var oftenMetrics = await collector.CollectLiveMetrics(often, CancellationToken.None);
        var seldomMetrics = await collector.CollectLiveMetrics(seldom, CancellationToken.None);

        oftenMetrics.Single(m => m.SubGroup == "wlan0" && m.Name == "rx_bytes").Value.Should().Be(300_000d);
        seldomMetrics.Single(m => m.SubGroup == "wlan0" && m.Name == "rx_bytes").Value.Should().Be(600_000d);
    }

    private static void Dev(IFileSystem fileSystem, long rx)
        => fileSystem.File.ReadAllLines("/proc/net/dev").Returns(
        [
            "Inter-|   Receive                                                |  Transmit",
            " face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed",
            $" wlan0: {rx} 100 0 5 0 0 0 0 {rx / 2} 80 0 0 0 0 0 0",
        ]);
}
