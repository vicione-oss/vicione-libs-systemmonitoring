using Microsoft.Extensions.Logging;

namespace ViciOne.SystemMonitoring.Metrics;

internal static partial class MetricsManagerLog
{
    [LoggerMessage(0, LogLevel.Debug, "Running '{CollectorName}' with interval {CurrentInterval} minutes.")]
    public static partial void LogRunningCollector(this ILogger<MetricsManager> logger, string collectorName, int currentInterval);

    [LoggerMessage(4, LogLevel.Warning, "Error collecting live metrics from '{CollectorName}'; further errors of it are not logged.")]
    public static partial void LogErrorCollectingLiveMetrics(this ILogger<MetricsManager> logger, string collectorName, Exception exception);

    [LoggerMessage(1, LogLevel.Error, "Error collecting metrics from '{CollectorName}'.")]
    public static partial void LogErrorCollectingMetrics(this ILogger<MetricsManager> logger, string collectorName, Exception exception);

    [LoggerMessage(2, LogLevel.Debug, "Running prepare for '{CollectorName}'.")]
    public static partial void LogRunningPrepare(this ILogger<MetricsManager> logger, string collectorName);

    [LoggerMessage(3, LogLevel.Debug, "Register collector '{CollectorName}'.")]
    public static partial void LogRegisterCollector(this ILogger<MetricsManager> logger, string collectorName);

    [LoggerMessage(5, LogLevel.Warning, "A live subscription asks for '{CollectorName}', which is not registered, not enabled or cannot be read live; it is left out of the samples.")]
    public static partial void LogUnreadLiveCollector(this ILogger<MetricsManager> logger, string collectorName);
}
