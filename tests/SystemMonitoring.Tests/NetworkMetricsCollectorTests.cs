using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ViciOne.SystemMonitoring.Collectors;

namespace ViciOne.SystemMonitoring;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public class NetworkMetricsCollectorTests
{
    [Fact]
    public async Task Returns_metrics()
    {
        var fileSystem = Substitute.For<IFileSystem>();
#pragma warning disable CA1849 // Asynchrone Methoden in einer asynchronen Methode aufrufen
        fileSystem.File.ReadAllLines("/proc/net/dev").Returns(File.ReadAllLines("./EdgeS/proc/net/dev"));
        fileSystem.File.ReadAllText("/sys/class/net/end0/duplex").Returns(File.ReadAllText("./EdgeS/sys/class/net/end0/duplex"));
        fileSystem.File.ReadAllText("/sys/class/net/end0/speed").Returns(File.ReadAllText("./EdgeS/sys/class/net/end0/speed"));
        fileSystem.File.ReadAllText("/sys/class/net/lan2/duplex").Returns(File.ReadAllText("./EdgeS/sys/class/net/lan2/duplex"));
        fileSystem.File.ReadAllText("/sys/class/net/lan2/speed").Returns(File.ReadAllText("./EdgeS/sys/class/net/lan2/speed"));
        fileSystem.File.ReadAllText("/sys/class/net/lan1/duplex").Returns(File.ReadAllText("./EdgeS/sys/class/net/lan1/duplex"));
        fileSystem.File.ReadAllText("/sys/class/net/lan1/speed").Returns(File.ReadAllText("./EdgeS/sys/class/net/lan1/speed"));
        fileSystem.File.ReadAllText("/sys/class/net/end0/carrier").Returns(File.ReadAllText("./EdgeS/sys/class/net/end0/carrier"));
        fileSystem.File.ReadAllText("/sys/class/net/lan1/carrier").Returns(File.ReadAllText("./EdgeS/sys/class/net/lan1/carrier"));
        fileSystem.File.ReadAllText("/sys/class/net/lan2/carrier").Returns(File.ReadAllText("./EdgeS/sys/class/net/lan2/carrier"));
#pragma warning restore CA1849 // Asynchrone Methoden in einer asynchronen Methode aufrufen

        NetworkMetricsCollector collector = new(fileSystem, Substitute.For<ILogger<NetworkMetricsCollector>>());
        await collector.PrepareMetrics(CancellationToken.None);

        var metrics = await collector.CollectMetrics(1, DateTime.UtcNow, CancellationToken.None);

        metrics.Should().AllSatisfy(m =>
        {
            m.Group.Should().Be("network");
            m.IntervalInMinutes.Should().Be(1);
        });

        metrics.Should().SatisfyRespectively(
        /*0*/ m => // lo
        {
            m.SubGroup.Should().Be("lo");
            m.Name.Should().Be("collisions");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*1*/ m =>
        {
            m.SubGroup.Should().Be("lo");
            m.Name.Should().Be("carrier_errors");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*2*/ m =>
        {
            m.SubGroup.Should().Be("lo");
            m.Name.Should().Be("rx_packets");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*3*/ m =>
        {
            m.SubGroup.Should().Be("lo");
            m.Name.Should().Be("rx_bytes");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.Byte);
        },
        /*4*/ m =>
        {
            m.SubGroup.Should().Be("lo");
            m.Name.Should().Be("rx_dropped");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*5*/ m =>
        {
            m.SubGroup.Should().Be("lo");
            m.Name.Should().Be("rx_errors");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*6*/ m =>
        {
            m.SubGroup.Should().Be("lo");
            m.Name.Should().Be("tx_bytes");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.Byte);
        },
        /*7*/ m =>
        {
            m.SubGroup.Should().Be("lo");
            m.Name.Should().Be("tx_packets");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*8*/ m =>
        {
            m.SubGroup.Should().Be("lo");
            m.Name.Should().Be("tx_dropped");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*9*/ m =>
        {
            m.SubGroup.Should().Be("lo");
            m.Name.Should().Be("tx_errors");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*10*/ m =>
        {
            m.SubGroup.Should().Be("lo");
            m.Name.Should().Be("rx_bandwidth");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.BytePerSecond);
        },
        /*11*/ m =>
        {
            m.SubGroup.Should().Be("lo");
            m.Name.Should().Be("tx_bandwidth");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.BytePerSecond);
        },
        /*12*/ m => //end0
        {
            m.SubGroup.Should().Be("end0");
            m.Name.Should().Be("collisions");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*13*/ m =>
        {
            m.SubGroup.Should().Be("end0");
            m.Name.Should().Be("carrier_errors");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*14*/ m =>
        {
            m.SubGroup.Should().Be("end0");
            m.Name.Should().Be("rx_packets");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*15*/ m =>
        {
            m.SubGroup.Should().Be("end0");
            m.Name.Should().Be("rx_bytes");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.Byte);
        },
        /*16*/ m =>
        {
            m.SubGroup.Should().Be("end0");
            m.Name.Should().Be("rx_dropped");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*17*/ m =>
        {
            m.SubGroup.Should().Be("end0");
            m.Name.Should().Be("rx_errors");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*18*/ m =>
        {
            m.SubGroup.Should().Be("end0");
            m.Name.Should().Be("tx_bytes");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.Byte);
        },
        /*19*/ m =>
        {
            m.SubGroup.Should().Be("end0");
            m.Name.Should().Be("tx_packets");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*20*/ m =>
        {
            m.SubGroup.Should().Be("end0");
            m.Name.Should().Be("tx_dropped");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*21*/ m =>
        {
            m.SubGroup.Should().Be("end0");
            m.Name.Should().Be("tx_errors");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*22*/ m =>
        {
            m.SubGroup.Should().Be("end0");
            m.Name.Should().Be("rx_bandwidth");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.BytePerSecond);
        },
        /*23*/ m =>
        {
            m.SubGroup.Should().Be("end0");
            m.Name.Should().Be("tx_bandwidth");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.BytePerSecond);
        },
        /*24*/ m =>
        {
            m.SubGroup.Should().Be("end0");
            m.Name.Should().Be("duplex");
            m.Value.Should().Be("full");
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*25*/ m =>
        {
            m.SubGroup.Should().Be("end0");
            m.Name.Should().Be("carrier");
            m.Value.Should().Be(1d);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*26*/ m =>
        {
            m.SubGroup.Should().Be("end0");
            m.Name.Should().Be("rx_bandwidth_usage");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.Percent);
        },
        /*27*/ m =>
        {
            m.SubGroup.Should().Be("end0");
            m.Name.Should().Be("tx_bandwidth_usage");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.Percent);
        },
        /*28*/ m =>
        {
            m.SubGroup.Should().Be("end0");
            m.Name.Should().Be("speed");
            m.Value.Should().Be(1000);
            m.Unit.Should().Be(MetricUnit.MegabitPerSecond);
        },
        /*29*/ m =>
        {
            m.SubGroup.Should().Be("end0");
            m.Name.Should().Be("total_bandwidth_usage");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.Percent);
        },
        /*30*/ m => //lan1
        {
            m.SubGroup.Should().Be("lan1");
            m.Name.Should().Be("collisions");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*31*/ m =>
        {
            m.SubGroup.Should().Be("lan1");
            m.Name.Should().Be("carrier_errors");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*32*/ m =>
        {
            m.SubGroup.Should().Be("lan1");
            m.Name.Should().Be("rx_packets");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*33*/ m =>
        {
            m.SubGroup.Should().Be("lan1");
            m.Name.Should().Be("rx_bytes");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.Byte);
        },
        /*34*/ m =>
        {
            m.SubGroup.Should().Be("lan1");
            m.Name.Should().Be("rx_dropped");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*35*/ m =>
        {
            m.SubGroup.Should().Be("lan1");
            m.Name.Should().Be("rx_errors");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*36*/ m =>
        {
            m.SubGroup.Should().Be("lan1");
            m.Name.Should().Be("tx_bytes");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.Byte);
        },
        /*37*/ m =>
        {
            m.SubGroup.Should().Be("lan1");
            m.Name.Should().Be("tx_packets");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*38*/ m =>
        {
            m.SubGroup.Should().Be("lan1");
            m.Name.Should().Be("tx_dropped");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*39*/ m =>
        {
            m.SubGroup.Should().Be("lan1");
            m.Name.Should().Be("tx_errors");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*40*/ m =>
        {
            m.SubGroup.Should().Be("lan1");
            m.Name.Should().Be("rx_bandwidth");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.BytePerSecond);
        },
        /*41*/ m =>
        {
            m.SubGroup.Should().Be("lan1");
            m.Name.Should().Be("tx_bandwidth");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.BytePerSecond);
        },
        /*42*/ m =>
        {
            m.SubGroup.Should().Be("lan1");
            m.Name.Should().Be("duplex");
            m.Value.Should().Be("full");
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*43*/ m =>
        {
            m.SubGroup.Should().Be("lan1");
            m.Name.Should().Be("carrier");
            m.Value.Should().Be(1d);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*44*/ m =>
        {
            m.SubGroup.Should().Be("lan1");
            m.Name.Should().Be("rx_bandwidth_usage");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.Percent);
        },
        /*45*/ m =>
        {
            m.SubGroup.Should().Be("lan1");
            m.Name.Should().Be("tx_bandwidth_usage");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.Percent);
        },
        /*46*/ m =>
        {
            m.SubGroup.Should().Be("lan1");
            m.Name.Should().Be("speed");
            m.Value.Should().Be(1000);
            m.Unit.Should().Be(MetricUnit.MegabitPerSecond);
        },
        /*47*/ m =>
        {
            m.SubGroup.Should().Be("lan1");
            m.Name.Should().Be("total_bandwidth_usage");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.Percent);
        },
        /*48*/ m => //lan2
        {
            m.SubGroup.Should().Be("lan2");
            m.Name.Should().Be("collisions");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*49*/ m =>
        {
            m.SubGroup.Should().Be("lan2");
            m.Name.Should().Be("carrier_errors");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*50*/ m =>
        {
            m.SubGroup.Should().Be("lan2");
            m.Name.Should().Be("rx_packets");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*51*/ m =>
        {
            m.SubGroup.Should().Be("lan2");
            m.Name.Should().Be("rx_bytes");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.Byte);
        },
        /*52*/ m =>
        {
            m.SubGroup.Should().Be("lan2");
            m.Name.Should().Be("rx_dropped");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*53*/ m =>
        {
            m.SubGroup.Should().Be("lan2");
            m.Name.Should().Be("rx_errors");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*54*/ m =>
        {
            m.SubGroup.Should().Be("lan2");
            m.Name.Should().Be("tx_bytes");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.Byte);
        },
        /*55*/ m =>
        {
            m.SubGroup.Should().Be("lan2");
            m.Name.Should().Be("tx_packets");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*56*/ m =>
        {
            m.SubGroup.Should().Be("lan2");
            m.Name.Should().Be("tx_dropped");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*57*/ m =>
        {
            m.SubGroup.Should().Be("lan2");
            m.Name.Should().Be("tx_errors");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*58*/ m =>
        {
            m.SubGroup.Should().Be("lan2");
            m.Name.Should().Be("rx_bandwidth");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.BytePerSecond);
        },
        /*59*/ m =>
        {
            m.SubGroup.Should().Be("lan2");
            m.Name.Should().Be("tx_bandwidth");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.BytePerSecond);
        },
        /*60*/ m =>
        {
            m.SubGroup.Should().Be("lan2");
            m.Name.Should().Be("duplex");
            m.Value.Should().Be("full");
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*61*/ m =>
        {
            m.SubGroup.Should().Be("lan2");
            m.Name.Should().Be("carrier");
            m.Value.Should().Be(0d);
            m.Unit.Should().Be(MetricUnit.None);
        },
        /*62*/ m =>
        {
            m.SubGroup.Should().Be("lan2");
            m.Name.Should().Be("rx_bandwidth_usage");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.Percent);
        },
        /*63*/ m =>
        {
            m.SubGroup.Should().Be("lan2");
            m.Name.Should().Be("tx_bandwidth_usage");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.Percent);
        },
        /*64*/ m =>
        {
            m.SubGroup.Should().Be("lan2");
            m.Name.Should().Be("speed");
            m.Value.Should().Be(1000);
            m.Unit.Should().Be(MetricUnit.MegabitPerSecond);
        },
        /*65*/ m =>
        {
            m.SubGroup.Should().Be("lan2");
            m.Name.Should().Be("total_bandwidth_usage");
            m.Value.Should().Be(0);
            m.Unit.Should().Be(MetricUnit.Percent);
        });
    }
}
