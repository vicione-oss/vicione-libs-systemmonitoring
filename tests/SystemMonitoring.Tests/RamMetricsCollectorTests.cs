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
public class RamMetricsCollector_CollectMetrics
{
    [Fact]
    public async Task Returns_the_page_cache_refaults_per_second_since_the_previous_reading()
    {
        var (collector, fileSystem, timeProvider) = await CreateCollector();
        await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);
        fileSystem.File.ReadAllLines("/proc/vmstat").Returns(["workingset_refault_file 5176"]);
        timeProvider.Advance(60.Seconds());

        var metrics = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);

        RefaultsPerSecond(metrics, interval: 1).Should().Be(10d);
    }

    [Fact]
    public async Task Reports_no_page_cache_refaults_on_the_first_reading()
    {
        var (collector, _, _) = await CreateCollector();

        var metrics = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);

        metrics.Should().Contain(m => m.Name == "total_memory_usage").And.NotContain(m => m.Name == "page_cache_refaults_per_second");
    }

    [Fact]
    public async Task Reports_no_page_cache_refaults_on_a_kernel_without_the_counter()
    {
        var (collector, fileSystem, timeProvider) = await CreateCollector();
        fileSystem.File.ReadAllLines("/proc/vmstat").Returns(["workingset_refault 4576"]);
        await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);
        timeProvider.Advance(60.Seconds());

        var metrics = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);

        metrics.Should().Contain(m => m.Name == "total_memory_usage").And.NotContain(m => m.Name == "page_cache_refaults_per_second");
    }

    [Fact]
    public async Task Reports_the_other_values_when_vmstat_cannot_be_read()
    {
        var (collector, fileSystem, _) = await CreateCollector();
        fileSystem.File.ReadAllLines("/proc/vmstat").Returns(_ => throw new UnauthorizedAccessException());

        var metrics = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);

        metrics.Should().Contain(m => m.Name == "total_memory_usage").And.NotContain(m => m.Name == "page_cache_refaults_per_second");
    }

    [Fact]
    public async Task Reports_the_other_values_when_the_refault_counter_is_malformed()
    {
        var (collector, fileSystem, _) = await CreateCollector();
        fileSystem.File.ReadAllLines("/proc/vmstat").Returns(["workingset_refault_file x"]);

        var metrics = await collector.CollectLiveMetrics(CancellationToken.None);

        metrics.Should().Contain(m => m.Name == "total_memory_usage").And.NotContain(m => m.Name == "page_cache_refaults_per_second");
    }

    [Fact]
    public async Task Keeps_the_page_cache_refaults_of_the_interval_while_read_live()
    {
        var (collector, fileSystem, timeProvider) = await CreateCollector();
        await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);
        await collector.CollectLiveMetrics(CancellationToken.None);
        fileSystem.File.ReadAllLines("/proc/vmstat").Returns(["workingset_refault_file 5176"]);
        timeProvider.Advance(2.Seconds());

        var live = await collector.CollectLiveMetrics(CancellationToken.None);
        timeProvider.Advance(58.Seconds());
        var regular = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);

        RefaultsPerSecond(live, interval: 0).Should().Be(300d);
        RefaultsPerSecond(regular, interval: 1).Should().Be(10d);
    }

    private static async Task<(RamMetricsCollector Collector, IFileSystem FileSystem, FakeTimeProvider TimeProvider)> CreateCollector()
    {
        var meminfo = await File.ReadAllLinesAsync("./EdgeS/proc/meminfo", TestContext.Current.CancellationToken);
        var vmstat = await File.ReadAllLinesAsync("./EdgeS/proc/vmstat", TestContext.Current.CancellationToken);
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.File.ReadAllLines("/proc/meminfo").Returns(meminfo);
        fileSystem.File.ReadAllLines("/proc/vmstat").Returns(vmstat);
        FakeTimeProvider timeProvider = new();
        return (new RamMetricsCollector(fileSystem, timeProvider), fileSystem, timeProvider);
    }

    private static object RefaultsPerSecond(IEnumerable<Metric> metrics, int interval)
        => metrics.Single(m => m.Name == "page_cache_refaults_per_second" && m.IntervalInMinutes == interval).Value;
}
