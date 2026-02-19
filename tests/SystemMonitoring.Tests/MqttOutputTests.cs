using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Adapter;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ViciOne.SystemMonitoring.Configuration;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring;

public class MqttOutput_
{
    [Fact]
    public void Can_be_created()
    {
        var provider = new ServiceCollection()
            .AddTransient(_ => Substitute.For<IOptionsMonitor<MonitoringConfig>>())
            .AddTransient(_ => Substitute.For<ILogger<MqttOutput>>())
            .AddSingleton<MqttOutput>()
            .BuildServiceProvider();

        var service = provider.GetRequiredService<MqttOutput>();

        service.Should().NotBeNull();
    }

    [Fact]
    public void Uses_custom_serialization()
    {
        Func<Metric, string> serialize = _ => throw new InvalidOperationException("test");
        var provider = new ServiceCollection()
            .AddTransient(_ => Substitute.For<IOptionsMonitor<MonitoringConfig>>())
            .AddTransient(_ => Substitute.For<ILogger<MqttOutput>>())
            .AddTransient<Func<Metric, string>>(_ => serialize)
            .AddSingleton<MqttOutput>()
            .BuildServiceProvider();
        var service = provider.GetRequiredService<MqttOutput>();

        service._serialize.Should().Be(serialize);
    }

    [Fact]
    public async Task Logs_Reconnect()
    {
        var logger = new TestLogger<MqttOutput>();
        var client = Substitute.For<IMqttClient>();
        var option = new MqttConfig();
        var conf = Substitute.For<IOptionsMonitor<MonitoringConfig>>();
        conf.CurrentValue.Returns(new MonitoringConfig() { Mqtt = new() { Host = "localhost" } });
        client.ConnectAsync(Arg.Any<MqttClientOptions>(), Arg.Any<CancellationToken>()).Returns(new MqttClientConnectResult());
        client.PublishAsync(Arg.Any<MqttApplicationMessage>(), Arg.Any<CancellationToken>()).ThrowsAsync(new MqttConnectingFailedException("Test", new InvalidOperationException("Test")));
        using var output = new MqttOutput(client, conf, logger);
        var metric = new Metric() { Group = "Test", IntervalInMinutes = 1, Name = "Test", Unit = "Test", Value = "0" };

        await output.Publish([metric], DateTimeOffset.MinValue, TestContext.Current.CancellationToken);

        client.PublishAsync(Arg.Any<MqttApplicationMessage>(), Arg.Any<CancellationToken>()).Returns(new MqttClientPublishResult(null, MqttClientPublishReasonCode.Success, string.Empty, []));

        await output.Publish([metric], DateTimeOffset.MinValue, TestContext.Current.CancellationToken);

        logger.Entries.Last().Message.Should().MatchEquivalentOf("*Reconnect*");
    }
}

public class MqttOutput_TrySend
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Does_retry(int retries)
    {
        TestLogger<MqttOutput> logger = new();
        var calls = 0;

        await MqttOutput.TrySend(() =>
        {
            calls++;
            if (calls < retries)
                throw new InvalidOperationException();
            return Task.CompletedTask;
        }, "Test", logger, static (_, __) => Task.CompletedTask, CancellationToken.None);

        calls.Should().Be(retries);
        logger.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Does_only_retry_three_times()
    {
        TestLogger<MqttOutput> logger = new();
        var calls = 0;

        await MqttOutput.TrySend(() =>
        {
            calls++;
            throw new InvalidOperationException();
        }, "Test", logger, static (_, __) => Task.CompletedTask, CancellationToken.None);

        calls.Should().Be(3);
        logger.Entries.Should().SatisfyRespectively(
            e => e.Message.Should().MatchEquivalentOf("*failed*retries*"));
    }
}
