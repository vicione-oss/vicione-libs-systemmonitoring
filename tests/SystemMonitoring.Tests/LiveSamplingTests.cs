using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Abstractions;
using AwesomeAssertions;
using AwesomeAssertions.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ViciOne.SystemMonitoring.Collectors;
using ViciOne.SystemMonitoring.Configuration;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public class CpuMetricsCollector_CollectLiveMetrics
{
    [Fact]
    public async Task Leaves_the_regular_value_unchanged()
    {
        var undisturbed = await RegularTotalSystem(liveReads: false);
        var withLiveReads = await RegularTotalSystem(liveReads: true);

        withLiveReads.Should().Be(undisturbed);
    }

    [Fact]
    public async Task Returns_metrics_without_interval()
    {
        var first = await File.ReadAllLinesAsync("./EdgeS/proc/stat", TestContext.Current.CancellationToken);
        var second = await File.ReadAllLinesAsync("./EdgeS/proc/stat2", TestContext.Current.CancellationToken);
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.File.ReadAllLines("/proc/stat").Returns(first);
        CpuMetricsCollector collector = new(fileSystem);
        await collector.PrepareMetrics(CancellationToken.None);
        fileSystem.File.ReadAllLines("/proc/stat").Returns(second);

        var metrics = await collector.CollectLiveMetrics(CancellationToken.None);

        metrics.Should().NotBeEmpty().And.AllSatisfy(m => m.IntervalInMinutes.Should().Be(0));
        metrics.Should().Contain(m => m.SubGroup == "total" && m.Name == "total_system");
    }

    [Fact]
    public async Task Reports_no_negative_iowait_when_the_kernel_moved_it_to_idle()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.File.ReadAllLines("/proc/stat").Returns(["cpu  100 0 100 1000 50 0 0 0 0 0"]);
        CpuMetricsCollector collector = new(fileSystem);
        await collector.PrepareMetrics(CancellationToken.None);
        fileSystem.File.ReadAllLines("/proc/stat").Returns(["cpu  200 0 200 1100 40 0 0 0 0 0"]);

        var metrics = await collector.CollectLiveMetrics(CancellationToken.None);

        metrics.Single(m => m.SubGroup == "total" && m.Name == "iowait").Value.Should().Be(0d);
        metrics.Single(m => m.SubGroup == "total" && m.Name == "idle").Value.Should().Be(90d / 290 * 100);
    }

    [Fact]
    public async Task Reports_the_iowait_share_since_the_previous_live_reading()
    {
        var first = await File.ReadAllLinesAsync("./EdgeS/proc/stat", TestContext.Current.CancellationToken);
        var second = await File.ReadAllLinesAsync("./EdgeS/proc/stat2", TestContext.Current.CancellationToken);
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.File.ReadAllLines("/proc/stat").Returns(first);
        CpuMetricsCollector collector = new(fileSystem);
        await collector.PrepareMetrics(CancellationToken.None);
        fileSystem.File.ReadAllLines("/proc/stat").Returns(second);

        var metrics = await collector.CollectLiveMetrics(CancellationToken.None);

        metrics.Single(m => m.SubGroup == "total" && m.Name == "iowait").Value.Should().Be(2d / 564 * 100);
    }

    private static async Task<object> RegularTotalSystem(bool liveReads)
    {
        var first = await File.ReadAllLinesAsync("./EdgeS/proc/stat", TestContext.Current.CancellationToken);
        var second = await File.ReadAllLinesAsync("./EdgeS/proc/stat2", TestContext.Current.CancellationToken);
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.File.ReadAllLines("/proc/stat").Returns(first);
        CpuMetricsCollector collector = new(fileSystem);
        await collector.PrepareMetrics(CancellationToken.None);

        fileSystem.File.ReadAllLines("/proc/stat").Returns(second);
        if (liveReads)
            await collector.CollectLiveMetrics(CancellationToken.None);

        var metrics = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);
        return metrics.Single(m => m.SubGroup == "total" && m.Name == "total_system").Value;
    }
}

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public class UptimeMetricsCollector_CollectMetrics
{
    [Fact]
    public async Task Parses_the_uptime_whatever_the_current_culture()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.File.ReadAllLines("/proc/uptime").Returns(["79507.53 158182.88"]);
        UptimeMetricsCollector collector = new(fileSystem);
        var culture = CultureInfo.CurrentCulture;

        List<Metric> metrics;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            metrics = await collector.CollectMetrics(1, DateTimeOffset.UtcNow, CancellationToken.None);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }

        metrics.Single().Value.Should().Be(79507.53);
    }
}

public class MetricsManager_CollectLiveMetrics
{
    [Fact]
    public async Task Reads_only_enabled_live_collectors()
    {
        var live = Substitute.For<ILiveMetricsCollector>();
        live.Name.Returns("cpu");
        live.CollectLiveMetrics(Arg.Any<CancellationToken>()).Returns([Metric("cpu")]);
        var disabled = Substitute.For<ILiveMetricsCollector>();
        disabled.Name.Returns("thermal_sensors");
        var regularOnly = Substitute.For<IScheduledMetricsCollector>();
        regularOnly.Name.Returns("swap");
        regularOnly.IntervalInMinutes.Returns(1);
        using MetricsManager metricsManager = new(Options(("cpu", true), ("thermal_sensors", false), ("swap", true)), new TestLogger<MetricsManager>(),
            [live, disabled, regularOnly]);

        var metrics = await metricsManager.CollectLiveMetrics(CancellationToken.None);

        metrics.Should().ContainSingle().Which.Group.Should().Be("cpu");
        await disabled.DidNotReceive().CollectLiveMetrics(Arg.Any<CancellationToken>());
        await regularOnly.DidNotReceive().CollectMetrics(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Logs_a_failing_collector_once()
    {
        var failing = Substitute.For<ILiveMetricsCollector>();
        failing.Name.Returns("cpu");
        failing.CollectLiveMetrics(Arg.Any<CancellationToken>()).Returns<List<Metric>>(_ => throw new InvalidOperationException("missing"));
        TestLogger<MetricsManager> logger = new();
        using MetricsManager metricsManager = new(Options(("cpu", true)), logger, [failing]);

        await metricsManager.CollectLiveMetrics(CancellationToken.None);
        await metricsManager.CollectLiveMetrics(CancellationToken.None);

        logger.Entries.Where(e => e.Exception is not null).Should().ContainSingle();
    }

    [Fact]
    public async Task Returns_nothing_once_disposed()
    {
        var live = Substitute.For<ILiveMetricsCollector>();
        live.Name.Returns("cpu");
        live.CollectLiveMetrics(Arg.Any<CancellationToken>()).Returns([Metric("cpu")]);
        MetricsManager metricsManager = new(Options(("cpu", true)), new TestLogger<MetricsManager>(), [live]);
        metricsManager.Dispose();

        var metrics = await metricsManager.CollectLiveMetrics(CancellationToken.None);

        metrics.Should().BeEmpty();
    }

    private static IOptionsMonitor<MonitoringConfig> Options(params (string Name, bool Enabled)[] metrics)
    {
        var options = Substitute.For<IOptionsMonitor<MonitoringConfig>>();
        options.CurrentValue.Returns(new MonitoringConfig
        {
            ClusterNodeId = string.Empty,
            Mqtt = new MqttConfig { Host = "localhost", Port = 1883 },
            EnabledMetrics = metrics.ToDictionary(m => m.Name, m => m.Enabled),
        });
        return options;
    }

    private static Metric Metric(string group)
        => new() { Group = group, Name = "value", IntervalInMinutes = 0, Value = 1d, Unit = MetricUnit.None };
}

public class LiveMonitoringService_
{
    [Fact]
    public async Task Samples_while_subscribed_and_stops_afterwards()
    {
        var source = Substitute.For<ILiveMetricsSource>();
        using SemaphoreSlim collected = new(0, 10);
        source.CollectLiveMetrics(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            collected.Release();
            return [new() { Group = "cpu", Name = "value", IntervalInMinutes = 0, Value = 1d, Unit = MetricUnit.None }];
        });
        FakeTimeProvider timeProvider = new();
        await using LiveMonitoringService service = new(source, new TestLogger<LiveMonitoringService>(), timeProvider);
        List<LiveSample> samples = [];
        var received = 0;
        var subscription = service.Subscribe(2.Seconds(), sample =>
        {
            samples.Add(sample);
            Interlocked.Increment(ref received);
        });
        (await collected.WaitAsync(1_000, TestContext.Current.CancellationToken)).Should().BeTrue("the first read moves the baselines");
        (await AdvanceUntilCollected(timeProvider, collected)).Should().BeTrue();
        (await Eventually(() => Volatile.Read(ref received) == 1)).Should().BeTrue();
        subscription.Dispose();
        timeProvider.Advance(10.Seconds());

        (await collected.WaitAsync(200, TestContext.Current.CancellationToken)).Should().BeFalse();
        samples.Should().ContainSingle().Which.Metrics.Should().ContainSingle();
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(OperationCanceledException))]
    public async Task Keeps_sampling_for_the_other_subscribers_when_one_fails(Type failure)
    {
        var (source, collected) = CountingSource();
        FakeTimeProvider timeProvider = new();
        TestLogger<LiveMonitoringService> logger = new();
        await using LiveMonitoringService service = new(source, logger, timeProvider);
        var samples = 0;
        using var failing = service.Subscribe(2.Seconds(), _ => throw (Exception)Activator.CreateInstance(failure, "subscriber")!);
        using var counting = service.Subscribe(2.Seconds(), _ => Interlocked.Increment(ref samples));
        (await collected.WaitAsync(1_000, TestContext.Current.CancellationToken)).Should().BeTrue();
        (await AdvanceUntilCollected(timeProvider, collected)).Should().BeTrue();
        (await AdvanceUntilCollected(timeProvider, collected)).Should().BeTrue();
        (await AdvanceUntilCollected(timeProvider, collected)).Should().BeTrue();

        (await Eventually(() => Volatile.Read(ref samples) >= 2)).Should().BeTrue();
        logger.Entries.Where(e => e.Exception is not null).Should().ContainSingle();
    }

    [Fact]
    public async Task Stops_handing_samples_to_a_disposed_subscription()
    {
        var (source, collected) = CountingSource();
        FakeTimeProvider timeProvider = new();
        await using LiveMonitoringService service = new(source, new TestLogger<LiveMonitoringService>(), timeProvider);
        var disposedSamples = 0;
        var remainingSamples = 0;
        var disposed = service.Subscribe(2.Seconds(), _ => Interlocked.Increment(ref disposedSamples));
        using var remaining = service.Subscribe(2.Seconds(), _ => Interlocked.Increment(ref remainingSamples));
        (await collected.WaitAsync(1_000, TestContext.Current.CancellationToken)).Should().BeTrue();

        disposed.Dispose();
        (await AdvanceUntilCollected(timeProvider, collected)).Should().BeTrue();
        (await AdvanceUntilCollected(timeProvider, collected)).Should().BeTrue();

        (await Eventually(() => Volatile.Read(ref remainingSamples) >= 2)).Should().BeTrue();
        disposedSamples.Should().Be(0);
    }

    [Fact]
    public async Task Keeps_sampling_after_a_failed_read()
    {
        var reads = 0;
        var source = Substitute.For<ILiveMetricsSource>();
        using SemaphoreSlim collected = new(0, 10);
        source.CollectLiveMetrics(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            collected.Release();
            return Interlocked.Increment(ref reads) == 2
                ? throw new IOException("collector")
                : [new() { Group = "cpu", Name = "value", IntervalInMinutes = 0, Value = 1d, Unit = MetricUnit.None }];
        });
        FakeTimeProvider timeProvider = new();
        TestLogger<LiveMonitoringService> logger = new();
        await using LiveMonitoringService service = new(source, logger, timeProvider);
        var samples = 0;
        using var subscription = service.Subscribe(2.Seconds(), _ => Interlocked.Increment(ref samples));
        (await collected.WaitAsync(1_000, TestContext.Current.CancellationToken)).Should().BeTrue();

        (await AdvanceUntilCollected(timeProvider, collected)).Should().BeTrue("the second read fails");
        (await AdvanceUntilCollected(timeProvider, collected)).Should().BeTrue();
        (await AdvanceUntilCollected(timeProvider, collected)).Should().BeTrue();

        (await Eventually(() => Volatile.Read(ref samples) >= 2)).Should().BeTrue();
        logger.Entries.Where(e => e.Exception is not null).Should().ContainSingle();
    }

    [Fact]
    public async Task Rejects_an_interval_below_the_minimum()
    {
        await using LiveMonitoringService service = new(Substitute.For<ILiveMetricsSource>(), new TestLogger<LiveMonitoringService>());

        var subscribe = () => service.Subscribe(TimeSpan.FromMilliseconds(99), _ => { });

        subscribe.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static (ILiveMetricsSource Source, SemaphoreSlim Collected) CountingSource()
    {
        var source = Substitute.For<ILiveMetricsSource>();
        SemaphoreSlim collected = new(0, 10);
        source.CollectLiveMetrics(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            collected.Release();
            return [new() { Group = "cpu", Name = "value", IntervalInMinutes = 0, Value = 1d, Unit = MetricUnit.None }];
        });
        return (source, collected);
    }

    private static async Task<bool> Eventually(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 50 && !condition(); attempt++)
            await Task.Delay(20, TestContext.Current.CancellationToken);

        return condition();
    }

    /// <summary>
    /// The timer starts only after the first read, so a single advance can come before it exists.
    /// </summary>
    private static async Task<bool> AdvanceUntilCollected(FakeTimeProvider timeProvider, SemaphoreSlim collected)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            timeProvider.Advance(2.Seconds());
            if (await collected.WaitAsync(20, TestContext.Current.CancellationToken))
                return true;
        }

        return false;
    }
}
