using System.ComponentModel.DataAnnotations;

namespace ViciOne.SystemMonitoring.Configuration;

public sealed class MonitoringConfig
{
    [Required]
    public string ClusterNodeId { get; set; } = string.Empty;
    [Required]
    public NodeType NodeType { get; set; } = NodeType.Unknown;
    [Required]
    public MqttConfig Mqtt { get; set; } = new();
    public Dictionary<string, bool> EnabledMetrics { get; internal set; } = new(StringComparer.OrdinalIgnoreCase);
}
