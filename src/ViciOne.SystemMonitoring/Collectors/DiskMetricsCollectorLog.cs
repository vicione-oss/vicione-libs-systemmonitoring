using Microsoft.Extensions.Logging;

namespace ViciOne.SystemMonitoring.Collectors;

internal static partial class DiskMetricsCollectorLog
{
    [LoggerMessage(0, LogLevel.Trace, "Error processing drive '{DriveName}'.")]
    public static partial void LogErrorProcessingDrive(this ILogger<DiskMetricsCollector> logger, string driveName, Exception exception);
}
