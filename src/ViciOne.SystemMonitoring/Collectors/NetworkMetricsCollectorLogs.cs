using Microsoft.Extensions.Logging;

namespace ViciOne.SystemMonitoring.Collectors;

internal static partial class NetworkMetricsCollectorLogs
{
    [LoggerMessage(0, LogLevel.Debug, "Could not read specific network metric.")]
    public static partial void LogCouldNotReadSpecificNetworkMetric(this ILogger<NetworkMetricsCollector> logger, Exception exception);
}
