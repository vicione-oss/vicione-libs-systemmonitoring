using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using ViciOne.SystemMonitoring.Collectors;
using ViciOne.SystemMonitoring.Configuration;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring;

public class MetricsManager_
{
    [Fact]
    public async Task Collects_metrics_on_first_cycle_for_all_scheduled_collectors()
    {
        var collector = Substitute.For<IScheduledMetricsCollector>();
        collector.Name.Returns("cpu");
        collector.IntervalInMinutes.Returns(5);
        TestLogger<MetricsManager> logger = new();
        var options = Substitute.For<IOptionsMonitor<MonitoringConfig>>();
        options.CurrentValue.Returns(new MonitoringConfig()
        {
            ClusterNodeId = string.Empty,
            Mqtt = new MqttConfig()
            {
                Host = "localhost",
                Port = 1883,
            },
            EnabledMetrics = new()
            {
                { "cpu", true },
            }
        });
        using MetricsManager metricsManager = new(options, logger, [collector]);

        await metricsManager.CollectAllMetrics(1, DateTimeOffset.Now, CancellationToken.None);
        await collector.Received(1).CollectMetrics(1, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Collects_metrics_on_first_cycle_for_all_onetime_collectors()
    {
        var collector = Substitute.For<IOnetimeMetricsCollector>();
        collector.Name.Returns("cpu");
        TestLogger<MetricsManager> logger = new();
        var options = Substitute.For<IOptionsMonitor<MonitoringConfig>>();
        options.CurrentValue.Returns(new MonitoringConfig()
        {
            ClusterNodeId = string.Empty,
            Mqtt = new MqttConfig()
            {
                Host = "localhost",
                Port = 1883,
            },
            EnabledMetrics = new()
            {
                { "cpu", true },
            }
        });
        using MetricsManager metricsManager = new(options, logger, [collector]);

        await metricsManager.CollectAllMetrics(1, DateTimeOffset.Now, CancellationToken.None);
        await collector.Received(1).CollectMetrics(1, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_nothing_on_incorrect_interval_for_scheduled_collectors()
    {
        var collector = Substitute.For<IScheduledMetricsCollector>();
        collector.Name.Returns("cpu");
        collector.IntervalInMinutes.Returns(5);
        TestLogger<MetricsManager> logger = new();
        var options = Substitute.For<IOptionsMonitor<MonitoringConfig>>();
        options.CurrentValue.Returns(new MonitoringConfig()
        {
            ClusterNodeId = string.Empty,
            Mqtt = new MqttConfig()
            {
                Host = "localhost",
                Port = 1883,
            },
            EnabledMetrics = new()
            {
                { "cpu", true },
            }
        });
        using MetricsManager metricsManager = new(options, logger, [collector]);

        await metricsManager.CollectAllMetrics(1, DateTimeOffset.Now, CancellationToken.None);
        await collector.Received(1).CollectMetrics(1, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());

        await metricsManager.CollectAllMetrics(1, DateTimeOffset.Now, CancellationToken.None);
        await collector.Received(1).CollectMetrics(1, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Collects_metrics_on_correct_interval_for_scheduled_collectors()
    {
        var collector = Substitute.For<IScheduledMetricsCollector>();
        collector.Name.Returns("cpu");
        collector.IntervalInMinutes.Returns(5);
        TestLogger<MetricsManager> logger = new();
        var options = Substitute.For<IOptionsMonitor<MonitoringConfig>>();
        options.CurrentValue.Returns(new MonitoringConfig()
        {
            ClusterNodeId = string.Empty,
            Mqtt = new MqttConfig()
            {
                Host = "localhost",
                Port = 1883,
            },
            EnabledMetrics = new()
            {
                { "cpu", true },
            }
        });
        using MetricsManager metricsManager = new(options, logger, [collector]);

        await metricsManager.CollectAllMetrics(1, DateTimeOffset.Now, CancellationToken.None);
        await collector.Received(1).CollectMetrics(1, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());

        await metricsManager.CollectAllMetrics(5, DateTimeOffset.Now, CancellationToken.None);
        await collector.Received(1).CollectMetrics(5, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ignores_disabled_collectors()
    {
        var collector = Substitute.For<IMetricsCollector>();
        collector.Name.Returns("cpu");
        TestLogger<MetricsManager> logger = new();
        var options = Substitute.For<IOptionsMonitor<MonitoringConfig>>();
        options.CurrentValue.Returns(new MonitoringConfig()
        {
            ClusterNodeId = string.Empty,
            Mqtt = new MqttConfig()
            {
                Host = "localhost",
                Port = 1883,
            },
            EnabledMetrics = new()
            {
                { "cpu", false },
            }
        });
        using MetricsManager metricsManager = new(options, logger, [collector]);

        await metricsManager.CollectAllMetrics(1, DateTimeOffset.Now, CancellationToken.None);
        await collector.DidNotReceive().CollectMetrics(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ignores_onetime_collectors_after_first_run()
    {
        var collector = Substitute.For<IOnetimeMetricsCollector>();
        collector.Name.Returns("cpu");
        TestLogger<MetricsManager> logger = new();
        var options = Substitute.For<IOptionsMonitor<MonitoringConfig>>();
        options.CurrentValue.Returns(new MonitoringConfig()
        {
            ClusterNodeId = string.Empty,
            Mqtt = new MqttConfig()
            {
                Host = "localhost",
                Port = 1883,
            },
            EnabledMetrics = new()
            {
                { "cpu", true },
            }
        });
        using MetricsManager metricsManager = new(options, logger, [collector]);

        await metricsManager.CollectAllMetrics(1, DateTimeOffset.Now, CancellationToken.None);
        await collector.Received(1).CollectMetrics(1, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());

        collector.ClearReceivedCalls();

        await metricsManager.CollectAllMetrics(1, DateTimeOffset.Now, CancellationToken.None);
        await collector.DidNotReceive().CollectMetrics(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }
}
