using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ViciOne.SystemMonitoring.Configuration;

namespace Sample;

[SuppressMessage("Maintainability", "CA1515:Erwägen Sie, öffentliche Typen intern zu machen.")]
public sealed class LogCollectorStatesService : IHostedService
{
    private readonly Action<ILogger, string, bool, bool, Exception?> _logMetricChange = LoggerMessage.Define<string, bool, bool>(LogLevel.Information, new EventId(1, "MetricChange"), "Metric '{Metric}' changed from {LastValue} to {NewValue}.");
    private readonly Dictionary<string, bool> _lastEnabledMetrics;

    public LogCollectorStatesService(IOptionsMonitor<MonitoringConfig> options, ILogger<LogCollectorStatesService> logger)
    {
        _lastEnabledMetrics = options.CurrentValue.EnabledMetrics.ToDictionary();

        options.OnChange(c =>
        {
            foreach (var (metric, enabled) in c.EnabledMetrics)
            {
                if (_lastEnabledMetrics.TryGetValue(metric, out var lastValue) && lastValue != enabled)
                {
                    _logMetricChange(logger, metric.ToString(), lastValue, enabled, null);
                    _lastEnabledMetrics[metric] = enabled;
                }
            }
        });
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
