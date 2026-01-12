using System.Security.Cryptography.X509Certificates;
using MQTTnet;
using MQTTnet.Formatter;

namespace ViciOne.SystemMonitoring.Configuration;

internal static class MqttConfigExtensions
{
    internal static MqttClientOptions BuildClientOptions(this MqttConfig config)
    {
        var clientOptionsBuilder = new MqttClientOptionsBuilder()
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithTcpServer(config.Host, config.Port);

        ConfigureClientCredentials(clientOptionsBuilder, config);
        ConfigureClientTls(clientOptionsBuilder, config);

        return clientOptionsBuilder.Build();
    }

    private static void ConfigureClientCredentials(MqttClientOptionsBuilder clientOptionsBuilder, MqttConfig config)
    {
        if (config.Username is not null || config.Password is not null)
            clientOptionsBuilder.WithCredentials(config.Username, config.Password);
    }

    private static void ConfigureClientTls(MqttClientOptionsBuilder clientOptionsBuilder, MqttConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.CertificateFile))
        {
            X509Certificate2? certificate = null;
#pragma warning disable CA2000 // Objekte verwerfen, bevor Bereich verloren geht
            if (!string.IsNullOrEmpty(config.CertificateFilePassword))
                certificate = Certificate.FromPFX(config.CertificateFile, config.CertificateFilePassword);
            else if (!string.IsNullOrWhiteSpace(config.CertificatePrivateKeyFile))
                certificate = Certificate.FromPEM(config.CertificateFile, config.CertificatePrivateKeyFile);
#pragma warning restore CA2000 // Objekte verwerfen, bevor Bereich verloren geht

            clientOptionsBuilder.WithTlsOptions(tls =>
            {
                if (config.SslProtocol.HasValue)
                    tls.WithSslProtocols(config.SslProtocol.Value);

                if (certificate is not null)
                    tls.WithClientCertificates(new(certificate));

                if (config.DisableCertificateValidation is true)
                    tls.WithCertificateValidationHandler(_ => true);
            });
        }
    }
}
