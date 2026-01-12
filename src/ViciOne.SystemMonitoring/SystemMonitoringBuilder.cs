using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ViciOne.SystemMonitoring.Collectors;

namespace ViciOne.SystemMonitoring;

public readonly ref struct SystemMonitoringBuilder
{
    private readonly IServiceCollection _services;

    internal SystemMonitoringBuilder(IServiceCollection services)
        => _services = services;

    public SystemMonitoringBuilder AddCollector<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class, IMetricsCollector
    {
        _services.AddTransient<IMetricsCollector, T>();
        return this;
    }

    public SystemMonitoringBuilder AddOutput<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class, IOutput
    {
        _services.AddSingleton<IOutput, T>();
        return this;
    }

    [SupportedOSPlatform("linux")]
    public SystemMonitoringBuilder UseDefaultCollectors()
    {
        AddCollector<CpuMetricsCollector>()
            .AddCollector<DiskMetricsCollector>()
            .AddCollector<NetworkMetricsCollector>()
            .AddCollector<ProcessMetricsCollector>()
            .AddCollector<ProcessorMetricsCollector>()
            .AddCollector<RamMetricsCollector>()
            .AddCollector<SwapMetricsCollector>()
            .AddCollector<ThermalSensorsMetricsCollector>()
            .AddCollector<UptimeMetricsCollector>();

        _services.TryAddSingleton<IFileSystem, FileSystem>();

        return this;
    }

    [SupportedOSPlatform("linux")]
    public SystemMonitoringBuilder UseMqttOutput()
        => AddOutput<MqttOutput>();
}
