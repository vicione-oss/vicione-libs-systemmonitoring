using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using AwesomeAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ViciOne.SystemMonitoring.Collectors;

namespace ViciOne.SystemMonitoring;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public class LoadAverageMetricsCollector_CollectMetrics
{
    [Fact]
    public async Task Returns_all_load_process_and_cpu_metrics()
    {
        var collector = CreateCollector();

        var metrics = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);

        metrics.Should().HaveCount(6);
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
            },
            m =>
            {
                m.Name.Should().Be("online_cpus");
                m.Value.Should().Be(2);
            });
    }

    [Theory]
    [InlineData("0\n", 1)]
    [InlineData("0-3\n", 4)]
    [InlineData("0-3,6,8-9\n", 7)]
    public async Task Counts_the_online_cpus_of_the_kernel_list(string list, int count)
    {
        var collector = CreateCollector(list);

        var metrics = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);

        metrics.Single(m => m.Name == "online_cpus").Value.Should().Be(count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0-")]
    [InlineData("3-1")]
    [InlineData("a")]
    public async Task Leaves_out_the_online_cpus_of_an_unreadable_list(string list)
    {
        var collector = CreateCollector(list);

        var metrics = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);

        metrics.Select(m => m.Name).Should().HaveCount(5).And.NotContain("online_cpus");
    }

    [Fact]
    public async Task Leaves_out_the_online_cpus_when_the_list_is_missing()
    {
        var fileSystem = FileSystem();
        fileSystem.File.ReadAllTextAsync("/sys/devices/system/cpu/online", Arg.Any<CancellationToken>()).ThrowsAsync(new FileNotFoundException());
        LoadAverageMetricsCollector collector = new(fileSystem);

        var metrics = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);

        metrics.Select(m => m.Name).Should().HaveCount(5).And.NotContain("online_cpus");
    }

    private static LoadAverageMetricsCollector CreateCollector(string? onlineCpus = null)
    {
        var fileSystem = FileSystem();
        fileSystem.File.ReadAllTextAsync("/sys/devices/system/cpu/online", Arg.Any<CancellationToken>())
            .Returns(onlineCpus ?? File.ReadAllText("./EdgeS/sys/devices/system/cpu/online"));

        return new LoadAverageMetricsCollector(fileSystem);
    }

    private static IFileSystem FileSystem()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.File.ReadAllLinesAsync("/proc/loadavg").Returns(File.ReadAllLines("./EdgeS/proc/loadavg"));
        return fileSystem;
    }
}
