using AwesomeAssertions;
using MQTTnet;
using MQTTnet.Formatter;
using ViciOne.SystemMonitoring.Configuration;

namespace ViciOne.SystemMonitoring;

public class MqttConfigExtensions_BuildClientOptions
{
    [Fact]
    public void Configures_tcp_server()
    {
        MqttConfig communication = new()
        {
            Host = "localhost",
            Port = 1884,
        };

        var options = communication.BuildClientOptions();

        options.Should().BeEquivalentTo(new MqttClientOptionsBuilder()
                .WithProtocolVersion(MqttProtocolVersion.V500)
                .WithTcpServer("localhost", 1884)
                .Build(),
                c => c.Excluding(i => i.ClientId));
    }

    [Theory]
    [InlineData("", "abc")]
    [InlineData("user", "abc")]
    [InlineData("user", "")]
    public void Configures_credentials(string username, string password)
    {
        MqttConfig communication = new()
        {
            Host = "localhost",
            Port = 1883,
            Username = username,
            Password = password,
        };

        var options = communication.BuildClientOptions();

        options.Should().BeEquivalentTo(new MqttClientOptionsBuilder()
                .WithProtocolVersion(MqttProtocolVersion.V500)
                .WithTcpServer("localhost", 1883)
                .WithCredentials(username, password)
                .Build(),
                c => c.Excluding(i => i.ClientId));
    }
}
