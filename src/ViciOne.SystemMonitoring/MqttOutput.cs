using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using ViciOne.SystemMonitoring.Collectors;
using ViciOne.SystemMonitoring.Configuration;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring;

public sealed class MqttOutput : IOutput, IDisposable
{
    private bool _isDisconnected;
    private readonly IMqttClient _client;
    private readonly IOptionsMonitor<MonitoringConfig> _options;
    private readonly ILogger<MqttOutput> _logger;
    internal readonly Func<Metric, string>? _serialize;

    public MqttOutput(IOptionsMonitor<MonitoringConfig> options, ILogger<MqttOutput> logger, Func<Metric, string>? serialize = null)
    {
        _client = new MqttClientFactory().CreateMqttClient();
        _client.DisconnectedAsync += OnDisconnected;
        _options = options;
        _logger = logger;
        _serialize = serialize;
    }

    internal MqttOutput(IMqttClient mqttClient, IOptionsMonitor<MonitoringConfig> options, ILogger<MqttOutput> logger, Func<Metric, string>? serialize = null)
    {
        _client = mqttClient;
        _client.DisconnectedAsync += OnDisconnected;
        _options = options;
        _logger = logger;
        _serialize = serialize;
    }

    private Task OnDisconnected(MqttClientDisconnectedEventArgs args)
    {
        _logger.LogDisconnected();
        return Task.CompletedTask;
    }

    public async Task Connect(CancellationToken cancellationToken)
    {
        try
        {
            var mqttOptions = _options.CurrentValue.Mqtt;
            var clientOptions = mqttOptions.BuildClientOptions();
            await _client.ConnectAsync(clientOptions, cancellationToken);
            _logger.LogConnected(mqttOptions.Host, mqttOptions.Port);
        }
        catch (Exception ex)
        {
            _logger.LogConnectFailed(_options.CurrentValue.Mqtt.Host, _options.CurrentValue.Mqtt.Port, ex);
        }
    }

    public async Task<bool> ConnectSilent(CancellationToken cancellationToken)
    {
        try
        {
            var mqttOptions = _options.CurrentValue.Mqtt;
            var clientOptions = mqttOptions.BuildClientOptions();
            await _client.ConnectAsync(clientOptions, cancellationToken);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task Disconnect(CancellationToken cancellationToken)
    {
        if (_client.IsConnected)
        {
            try
            {
                await _client.DisconnectAsync(cancellationToken: cancellationToken);
                _logger.LogDisconnected();
            }
            catch (Exception ex)
            {
                _logger.LogDisconnectFailed(ex);
            }
        }
    }

    public void Dispose()
    {
        _client.DisconnectedAsync -= OnDisconnected;
        _client.Dispose();
    }

    public async Task Publish(IReadOnlyCollection<Metric> metrics, DateTimeOffset timestamp, CancellationToken cancellationToken)
    {
        if (_isDisconnected && await ConnectSilent(cancellationToken))
        {
            _isDisconnected = false;
            _logger.LogPublishReconnected();
        }

        if (_isDisconnected)
            return;

        foreach (var metric in metrics)
        {
            var sendSuccessful = await TrySend(() => Publish(metric, timestamp, cancellationToken), metric.ToIdString(),
                _logger, async (r, c) =>
                {
                    await Task.Delay(1_000 * (3 - r), c);

                    if (!_client.IsConnected)
                    {
                        using CancellationTokenSource cancellationTokenSource = new(3_000);
                        using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(c, cancellationTokenSource.Token);
                        await Connect(linkedTokenSource.Token);
                    }
                }, cancellationToken);

            if (!sendSuccessful && !_client.IsConnected)
            {
                _logger.LogPublishAbandoned();

                _isDisconnected = true;

                break;
            }
        }
    }

    private async Task Publish(Metric metric, DateTimeOffset timestamp, CancellationToken cancellationToken)
    {
        var topic = BuildMqttTopic(_options.CurrentValue, metric);
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Serialize(metric))
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce)
            .WithUserProperty("Timestamp", timestamp.ToString("O"))
            .WithUserProperty("Unit", metric.Unit)
            .Build();
        await _client.PublishAsync(message, cancellationToken);
    }

    private string Serialize(Metric metric)
    {
        try
        {
            if (_serialize is not null)
                return _serialize(metric);

            return metric.Value switch
            {
                double v => JsonSerializer.Serialize(v, MqttOutputGenerationContext.Default.Double),
                string v => JsonSerializer.Serialize(v, MqttOutputGenerationContext.Default.String),
                AggregatedMetricValue v => JsonSerializer.Serialize(v, MqttOutputGenerationContext.Default.AggregatedMetricValue),
                List<LinuxProcessInfo> v => JsonSerializer.Serialize(v, MqttOutputGenerationContext.Default.ListLinuxProcessInfo),
                Dictionary<string, int> v => JsonSerializer.Serialize(v, MqttOutputGenerationContext.Default.DictionaryStringInt32),
                _ => throw new InvalidOperationException("Unsupported value type.")
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Unsupported value type: {metric.Value.GetType()}", ex);
        }
    }

    private static string BuildMqttTopic(MonitoringConfig config, Metric metric)
    {
        StringBuilder topicBuilder = new();
        topicBuilder
            .Append("monitoring")
            .Append('/').Append(config.ClusterNodeId)
            .Append('/').Append(metric.Group)
            .Append('/').Append(metric.IntervalInMinutes).Append("min");

        if (!string.IsNullOrEmpty(metric.SubGroup))
            topicBuilder.Append('/').Append(metric.SubGroup);

        topicBuilder.Append('/').Append(metric.Name);

        if (metric.Value is AggregatedMetricValue)
            topicBuilder.Append("/Aggregates");
        else
            topicBuilder.Append("/Last");

        return topicBuilder.ToString();
    }

    internal static async Task<bool> TrySend(Func<Task> send, string id, ILogger<MqttOutput> logger, Func<int, CancellationToken, Task> wait, CancellationToken cancellationToken)
    {
        var retries = 3;
        while (retries-- > 0)
        {
            try
            {
                await send();
                break; // Success - exit retry loop
            }
            catch (Exception ex)
            {
                if (retries == 0)
                {
                    logger.LogFailedToPublishMetricAfterRetries(id, ex);

                    return false;
                }
                else
                {
                    await wait(retries, cancellationToken);
                }
            }
        }

        return true;
    }
}
