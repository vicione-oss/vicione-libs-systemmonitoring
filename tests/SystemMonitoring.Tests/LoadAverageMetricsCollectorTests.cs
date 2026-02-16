using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using AwesomeAssertions;
using NSubstitute;
using ViciOne.SystemMonitoring.Collectors;

namespace ViciOne.SystemMonitoring;

[SuppressMessage("Interoperability", "CA1416:Plattformkompatibilität überprüfen")]
public class LoadAverageMetricsCollector_CollectMetrics
{
    [Fact]
    public async Task Returns_all_load_and_process_metrics()
    {
        var collector = CreateCollector();

        var metrics = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);

        metrics.Should().HaveCount(5);
        metrics.Should().AllSatisfy(m =>
        {
            m.Group.Should().Be("loadavg");
            m.IntervalInMinutes.Should().Be(1);
            m.Unit.Should().Be(MetricUnit.None);
        });
        metrics.Should().SatisfyRespectively(
            m =>
            {
                m.Name.Should().Be("load1");
                m.Value.Should().Be(0.52);
            },
            m =>
            {
                m.Name.Should().Be("load5");
                m.Value.Should().Be(0.34);
            },
            m =>
            {
                m.Name.Should().Be("load15");
                m.Value.Should().Be(0.28);
            },
            m =>
            {
                m.Name.Should().Be("running_kernel_scheduling_entities");
                m.Value.Should().Be(1);
            },
            m =>
            {
                m.Name.Should().Be("total_kernel_scheduling_entities");
                m.Value.Should().Be(215);
            });
    }

    private static LoadAverageMetricsCollector CreateCollector()
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.File.ReadAllLinesAsync("/proc/loadavg").Returns(File.ReadAllLines("./EdgeS/proc/loadavg"));

        return new LoadAverageMetricsCollector(fileSystem);
    }
}
