using System.ComponentModel.DataAnnotations;
using System.Security.Authentication;

namespace ViciOne.SystemMonitoring.Configuration;

public sealed class MqttConfig
{
    [Required]
    public string Host { get; set; } = string.Empty;
    [Required]
    public ushort Port { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? CertificateFile { get; set; }
    public string? CertificateFilePassword { get; set; }
    public string? CertificatePrivateKeyFile { get; set; }
    public SslProtocols? SslProtocol { get; set; }
    public bool? DisableCertificateValidation { get; set; }
}
