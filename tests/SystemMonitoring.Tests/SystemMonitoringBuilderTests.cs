using System.Diagnostics.CodeAnalysis;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ViciOne.SystemMonitoring.Collectors;

namespace ViciOne.SystemMonitoring;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public class SystemMonitoringBuilder_UseDefaultCollectors
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolves_every_collector_whether_or_not_a_TimeProvider_is_registered(bool withTimeProvider)
    {
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>)).AddOptions().AddSystemMonitoring(b => b.UseDefaultCollectors());
        if (withTimeProvider)
            services.AddSingleton(TimeProvider.System);
        using var provider = services.BuildServiceProvider();

        var collectors = provider.GetServices<IMetricsCollector>();

        collectors.Should().ContainSingle(c => c is SwapMetricsCollector).And.ContainSingle(c => c is RamMetricsCollector);
    }
}
