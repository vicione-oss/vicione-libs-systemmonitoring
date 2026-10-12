using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ViciOne.SystemMonitoring.Collectors;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public class NetworkMetricsCollector_Ports
{
    private readonly IFileSystem _fileSystem = Substitute.For<IFileSystem>();

    public NetworkMetricsCollector_Ports()
    {
        _fileSystem.Path.GetFileName(Arg.Any<string>()).Returns(call => Path.GetFileName(call.Arg<string>()));
        _fileSystem.File.ReadAllLines("/proc/net/dev").Returns(
        [
            "Inter-|   Receive                                                |  Transmit",
            " face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed",
            " end0: 1000 10 0 0 0 0 0 0 500 5 0 0 0 0 0 0",
            " lan1: 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0",
            " lan2: 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0",
        ]);
        _fileSystem.File.ReadAllText("/sys/class/net/end0/carrier").Returns("1\n");
        _fileSystem.File.ReadAllText("/sys/class/net/lan1/carrier").Returns("1\n");
        _fileSystem.File.ReadAllText("/sys/class/net/lan2/carrier").Throws(new IOException("Invalid argument"));
        _fileSystem.Directory.GetFileSystemEntries("/sys/class/net/lan1", "lower_*").Returns(["/sys/class/net/lan1/lower_end0"]);
        _fileSystem.Directory.GetFileSystemEntries("/sys/class/net/lan2", "lower_*").Returns(["/sys/class/net/lan2/lower_end0"]);
        _fileSystem.Directory.Exists("/sys/class/net/end0/dsa").Returns(true);
    }

    [Fact]
    public async Task Reports_whether_a_cable_is_plugged_in()
    {
        var metrics = await Collect();

        Value(metrics, "lan1", "carrier").Should().Be(1d);
        Value(metrics, "lan2", "carrier").Should().Be(0d);
    }

    [Fact]
    public async Task Names_the_interface_that_carries_the_traffic_of_a_switch_port()
    {
        var metrics = await Collect();

        Value(metrics, "lan1", "conduit").Should().Be("end0");
        Value(metrics, "lan2", "conduit").Should().Be("end0");
        metrics.Should().NotContain(m => m.SubGroup == "end0" && m.Name == "conduit");
    }

    [Fact]
    public async Task Names_no_conduit_for_an_interface_on_top_of_one_without_a_switch()
    {
        _fileSystem.Directory.Exists("/sys/class/net/end0/dsa").Returns(false);

        var metrics = await Collect();

        metrics.Should().NotContain(m => m.Name == "conduit");
    }

    private async Task<List<Metric>> Collect()
    {
        NetworkMetricsCollector collector = new(_fileSystem, Substitute.For<ILogger<NetworkMetricsCollector>>());
        await collector.PrepareMetrics(CancellationToken.None);
        return await collector.CollectMetrics(1, DateTime.UtcNow, CancellationToken.None);
    }

    private static object? Value(List<Metric> metrics, string networkInterface, string name)
        => metrics.Single(m => m.SubGroup == networkInterface && m.Name == name && m.IntervalInMinutes == 1).Value;
}
