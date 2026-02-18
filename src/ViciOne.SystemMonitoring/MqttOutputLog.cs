using Microsoft.Extensions.Logging;

namespace ViciOne.SystemMonitoring;

internal static partial class MqttOutputLog
{
    [LoggerMessage(0, LogLevel.Information, "Connected to '{Host}:{Port}'.")]
    public static partial void LogConnected(this ILogger<MqttOutput> logger, string host, int port);

    [LoggerMessage(1, LogLevel.Information, "Disconnected.")]
    public static partial void LogDisconnected(this ILogger<MqttOutput> logger);

    [LoggerMessage(2, LogLevel.Warning, "Error publishing of metric '{MetricName}' failed.")]
    public static partial void LogErrorPublishingMetric(this ILogger<MqttOutput> logger, string metricName, Exception exception);

    [LoggerMessage(3, LogLevel.Error, "Failed to publish '{MetricName}' after 3 retries.")]
    public static partial void LogFailedToPublishMetricAfterRetries(this ILogger<MqttOutput> logger, string metricName, Exception exception);

    [LoggerMessage(4, LogLevel.Error, "Connect to '{Host}:{Port}' failed.")]
    public static partial void LogConnectFailed(this ILogger<MqttOutput> logger, string host, int port, Exception exception);

    [LoggerMessage(5, LogLevel.Error, "Disconnect failed.")]
    public static partial void LogDisconnectFailed(this ILogger<MqttOutput> logger, Exception exception);

    [LoggerMessage(6, LogLevel.Error, "Failed to publish after 3 retries with no connection. Publishing will be abandoned for this set of metrics.")]
    public static partial void LogPublishAbandoned(this ILogger<MqttOutput> logger);

    [LoggerMessage(7, LogLevel.Information, "Reconnected, publishing will continue.")]
    public static partial void LogPublishReconnected(this ILogger<MqttOutput> logger);
}
