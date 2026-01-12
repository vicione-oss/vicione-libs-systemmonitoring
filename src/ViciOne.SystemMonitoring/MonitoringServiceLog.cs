using Microsoft.Extensions.Logging;

namespace ViciOne.SystemMonitoring;

internal static partial class MonitoringServiceLog
{
    [LoggerMessage(0, LogLevel.Debug, "Monitoring stopped by cancellation token.")]
    public static partial void LogMonitoringStoppedByCancellationToken(this ILogger<MonitoringService> logger);

    [LoggerMessage(3, LogLevel.Debug, "Processing next interval. ({Timestamp})")]
    public static partial void LogProcessingNextInterval(this ILogger<MonitoringService> logger, DateTimeOffset timestamp);

    [LoggerMessage(4, LogLevel.Debug, "Publish failed for output '{OutputName}'.")]
    public static partial void LogPublishFailed(this ILogger<MonitoringService> logger, string outputName, Exception exception);
}
