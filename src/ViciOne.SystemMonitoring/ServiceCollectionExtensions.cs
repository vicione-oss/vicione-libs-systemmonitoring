using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring;

[SupportedOSPlatform("linux")]
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSystemMonitoring(this IServiceCollection services, Action<SystemMonitoringBuilder> build)
    {
        build(new(services));
        return services.AddHostedService<MonitoringService>()
            .AddSingleton<MetricsManager>()
            .AddSingleton<IMetricsManager>(s => s.GetRequiredService<MetricsManager>())
            .AddSingleton<ILiveMetricsSource>(s => s.GetRequiredService<MetricsManager>())
            .AddSingleton<ILiveMonitoring, LiveMonitoringService>();
    }
}
