using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using AwesomeAssertions;
using NSubstitute;
using ViciOne.SystemMonitoring.Collectors;

namespace ViciOne.SystemMonitoring;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public class CpuMetricsCollector_CollectMetrics
{
    [Fact]
    public async Task Returns_metrics()
    {
        var fileSystem = Substitute.For<IFileSystem>();
#pragma warning disable CA1849 // Asynchrone Methoden in einer asynchronen Methode aufrufen
        fileSystem.File.ReadAllLines("/proc/stat").Returns(File.ReadAllLines("./EdgeS/proc/stat"));
        CpuMetricsCollector collector = new(fileSystem);
        await collector.PrepareMetrics(CancellationToken.None);
        fileSystem.File.ReadAllLines("/proc/stat").Returns(File.ReadAllLines("./EdgeS/proc/stat2"));
#pragma warning restore CA1849 // Asynchrone Methoden in einer asynchronen Methode aufrufen

        var metrics = await collector.CollectMetrics(1, DateTime.UtcNow, CancellationToken.None);

        metrics.Should().AllSatisfy(m =>
        {
            m.Group.Should().Be("cpu");
            m.IntervalInMinutes.Should().Be(1);
        });
        metrics.Should().SatisfyRespectively(
            // total
            m =>
            {
                m.SubGroup.Should().Be("total");
                m.Name.Should().Be("user");
                m.Value.Should().Be(26.95035460992908);
                m.Unit.Should().Be(MetricUnit.Percent);
            },
            m =>
            {
                m.SubGroup.Should().Be("total");
                m.Name.Should().Be("system");
                m.Value.Should().Be(19.50354609929078);
                m.Unit.Should().Be(MetricUnit.Percent);
            },
            m =>
            {
                m.SubGroup.Should().Be("total");
                m.Name.Should().Be("idle");
                m.Value.Should().Be(53.54609929078015);
                m.Unit.Should().Be(MetricUnit.Percent);
            },
            m =>
            {
                m.SubGroup.Should().Be("total");
                m.Name.Should().Be("iowait");
                m.Value.Should().Be(2d / 564 * 100);
                m.Unit.Should().Be(MetricUnit.Percent);
            },
            m =>
            {
                m.SubGroup.Should().Be("total");
                m.Name.Should().Be("steal");
                m.Value.Should().Be(0);
                m.Unit.Should().Be(MetricUnit.Percent);
            },
            m =>
            {
                m.SubGroup.Should().Be("total");
                m.Name.Should().Be("guest");
                m.Value.Should().Be(0);
                m.Unit.Should().Be(MetricUnit.Percent);
            },
            m =>
            {
                m.SubGroup.Should().Be("total");
                m.Name.Should().Be("total_system");
                m.Value.Should().Be(46.45390070921986);
                m.Unit.Should().Be(MetricUnit.Percent);
            },
            // core0
            m =>
            {
                m.SubGroup.Should().Be("core0");
                m.Name.Should().Be("user");
                m.Value.Should().Be(25.26690391459075);
                m.Unit.Should().Be(MetricUnit.Percent);
            },
            m =>
            {
                m.SubGroup.Should().Be("core0");
                m.Name.Should().Be("system");
                m.Value.Should().Be(21.352313167259787);
                m.Unit.Should().Be(MetricUnit.Percent);
            },
            m =>
            {
                m.SubGroup.Should().Be("core0");
                m.Name.Should().Be("idle");
                m.Value.Should().Be(53.380782918149464);
                m.Unit.Should().Be(MetricUnit.Percent);
            },
            m =>
            {
                m.SubGroup.Should().Be("core0");
                m.Name.Should().Be("iowait");
                m.Value.Should().Be(0d);
                m.Unit.Should().Be(MetricUnit.Percent);
            },
            m =>
            {
                m.SubGroup.Should().Be("core0");
                m.Name.Should().Be("steal");
                m.Value.Should().Be(0);
                m.Unit.Should().Be(MetricUnit.Percent);
            },
            m =>
            {
                m.SubGroup.Should().Be("core0");
                m.Name.Should().Be("guest");
                m.Value.Should().Be(0);
                m.Unit.Should().Be(MetricUnit.Percent);
            },
            // core1
            m =>
            {
                m.SubGroup.Should().Be("core1");
                m.Name.Should().Be("user");
                m.Value.Should().Be(28.621908127208478);
                m.Unit.Should().Be(MetricUnit.Percent);
            },
            m =>
            {
                m.SubGroup.Should().Be("core1");
                m.Name.Should().Be("system");
                m.Value.Should().Be(17.6678445229682);
                m.Unit.Should().Be(MetricUnit.Percent);
            },
            m =>
            {
                m.SubGroup.Should().Be("core1");
                m.Name.Should().Be("idle");
                m.Value.Should().Be(53.71024734982333);
                m.Unit.Should().Be(MetricUnit.Percent);
            },
            m =>
            {
                m.SubGroup.Should().Be("core1");
                m.Name.Should().Be("iowait");
                m.Value.Should().Be(2d / 283 * 100);
                m.Unit.Should().Be(MetricUnit.Percent);
            },
            m =>
            {
                m.SubGroup.Should().Be("core1");
                m.Name.Should().Be("steal");
                m.Value.Should().Be(0);
                m.Unit.Should().Be(MetricUnit.Percent);
            },
            m =>
            {
                m.SubGroup.Should().Be("core1");
                m.Name.Should().Be("guest");
                m.Value.Should().Be(0);
                m.Unit.Should().Be(MetricUnit.Percent);
            });
    }
}
