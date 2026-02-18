using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sample;
using ViciOne.SystemMonitoring.Configuration;

namespace ViciOne.SystemMonitoring;

internal class Program
{
    internal static async Task Main(string[] args)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("This monitoring system is only supported on Linux.");

        using var host = CreateHostBuilder(args);
        var config = host.Services.GetRequiredService<IOptionsMonitor<MonitoringConfig>>().CurrentValue;

        // Print configutation - only for test - adjust to ViciOne later
        Console.WriteLine($"ClusterNodeId  : {config.ClusterNodeId}");
        Console.WriteLine($"NodeType       : {config.NodeType}");
        Console.WriteLine($"MQTT Broker    : {config.Mqtt.Host}:{config.Mqtt.Port}");
        Console.WriteLine($"Enabled Metrics: {string.Join(", ", config.EnabledMetrics)}");

        await host.RunAsync();
    }

    [SupportedOSPlatform("linux")]
    private static IHost CreateHostBuilder(string[] args)
        => Host.CreateDefaultBuilder(args)
            .ConfigureServices(static (hostContext, services) =>
            {
                services
                    .Configure<MonitoringConfig>(hostContext.Configuration.GetSection("Monitoring"))
                    .AddSystemMonitoring(b => b
                        .UseMqttOutput()
                        .UseDefaultCollectors())
                    .AddHostedService<LogCollectorStatesService>();
            })
            .Build();
}
