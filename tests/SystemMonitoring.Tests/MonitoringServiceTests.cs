using AwesomeAssertions;
using AwesomeAssertions.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring;

public class MonitoringService_
{
    [Fact]
    public async Task Publishes_metrics()
    {
        var metricsManager = Substitute.For<IMetricsManager>();
        metricsManager.CollectAllMetrics(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([new() { Group = "Test", Name = "My", IntervalInMinutes = 1, Value = 0, Unit = string.Empty, }]);
        var output = Substitute.For<IOutput>();
        using SemaphoreSlim semaphore = new(0, 1);
        output.When(o => o.Publish(Arg.Any<IReadOnlyCollection<Metric>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()))
            .Do(_ => semaphore.Release());
        TestLogger<MonitoringService> logger = new();
        FakeTimeProvider timeProvider = new();
        using MonitoringService service = new(metricsManager, [output], logger, timeProvider);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await semaphore.WaitAsync(1_000, TestContext.Current.CancellationToken);
        timeProvider.Advance(1.Minutes());
        await semaphore.WaitAsync(1_000, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        await metricsManager.Received(1).Initialize(Arg.Any<CancellationToken>());
        await output.Received(1).Publish(Arg.Is<IReadOnlyCollection<Metric>>(m => m.Single().Name == "My"), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        logger.Entries.Should().SatisfyRespectively(
            e => e.Message.Should().MatchEquivalentOf("*process*"),
            e => e.Message.Should().MatchEquivalentOf("*cancel*"));
    }
}
