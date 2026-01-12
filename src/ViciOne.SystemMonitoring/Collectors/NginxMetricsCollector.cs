using System.Globalization;
using Microsoft.Extensions.Configuration;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring.Collectors;

public sealed class NginxMetricsCollector(IConfiguration configuration) : IScheduledMetricsCollector, IDisposable
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly List<List<LastMetricValue>> _fiveMinuteBuffer = [];
    private readonly List<List<LastMetricValue>> _fifteenMinuteBuffer = [];
    private string? _nginxStatusUrl;
    private readonly Lock _lock = new();

    public int IntervalInMinutes { get; } = 1;
    public string Name => "nginx";

    public async Task<List<Metric>> CollectMetrics(int interval, DateTimeOffset cycleStartTime, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(_nginxStatusUrl))
                _nginxStatusUrl = configuration.GetRequiredSection(Name).GetRequiredSection("Url").Value ?? throw new InvalidOperationException("NGINX status URL is invalid.");

            var nginxMetrics = await CollectNginxMetrics();
            lock (_lock)
            {
                var metricsData = CreateMetricsData(nginxMetrics);
                List<Metric> metrics = [];

                metrics.AddRange(metricsData.ToMetrics(Name, null, IntervalInMinutes, m => m.Value));

                UpdateBuffer(_fiveMinuteBuffer, metricsData);
                UpdateBuffer(_fifteenMinuteBuffer, metricsData);

                if (interval % 5 == 0)
                {
                    metrics.AddRange(_fiveMinuteBuffer.AggregateBuffer().ToMetrics(Name, null, 5));
                    _fifteenMinuteBuffer.Clear();
                }

                if (interval % 15 == 0)
                {
                    metrics.AddRange(_fifteenMinuteBuffer.AggregateBuffer().ToMetrics(Name, null, 15));
                    _fifteenMinuteBuffer.Clear();
                }

                return metrics;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting NGINX metrics.", ex);
        }
    }

    private async Task<Dictionary<string, double>> CollectNginxMetrics()
    {
        var metrics = new Dictionary<string, double>
        {
            { "activeConnections", 0 },
            { "waitingConnections", 0 },
            { "latencyMs", 0 }
        };

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var response = await _httpClient.GetStringAsync(new Uri(_nginxStatusUrl ?? throw new InvalidOperationException("NGINX status URL must be set.")));
            stopwatch.Stop();
            metrics["latencyMs"] = stopwatch.Elapsed.TotalMilliseconds;

            if (string.IsNullOrWhiteSpace(response) || !response.Contains("active connections", StringComparison.InvariantCulture))
                throw new InvalidOperationException("Invalid or unexpected response from NGINX status URL.");

            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length > 0 && lines[0].Contains("active connections", StringComparison.InvariantCulture))
            {
                var activeConnections = double.Parse(lines[0].Split(':', StringSplitOptions.TrimEntries)[1], CultureInfo.InvariantCulture);
                metrics["activeConnections"] = activeConnections;
            }

            if (lines.Length > 2 && lines[2].Contains("reading", StringComparison.InvariantCulture))
            {
                var parts = lines[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var waitingConnections = double.Parse(parts.Last(), CultureInfo.InvariantCulture);
                metrics["waitingConnections"] = waitingConnections;
            }
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("Error reaching NGINX status URL.", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error parsing NGINX metrics.", ex);
        }

        return metrics;
    }


    private static List<LastMetricValue> CreateMetricsData(Dictionary<string, double> nginxMetrics) => [.. nginxMetrics.Select(kv => new LastMetricValue(kv.Key, kv.Value, kv.Key == "latencyMs" ? MetricUnit.Millisecond : MetricUnit.None))];

    private static void UpdateBuffer(List<List<LastMetricValue>> buffer, List<LastMetricValue> metrics)
    {
        if (metrics.Count > 0)
            buffer.Add(metrics);
    }

    public void Dispose()
        => _httpClient.Dispose();
}
