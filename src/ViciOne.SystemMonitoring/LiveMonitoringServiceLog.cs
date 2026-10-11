using Microsoft.Extensions.Logging;

namespace ViciOne.SystemMonitoring;

internal static partial class LiveMonitoringServiceLog
{
    [LoggerMessage(0, LogLevel.Error, "Live sampling failed and stopped.")]
    public static partial void LogLiveSamplingFailed(this ILogger<LiveMonitoringService> logger, Exception exception);

    [LoggerMessage(1, LogLevel.Error, "A subscriber failed to handle a live sample; sampling continues and further failures of this subscriber are not logged.")]
    public static partial void LogSubscriberFailed(this ILogger<LiveMonitoringService> logger, Exception exception);
}
