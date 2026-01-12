using System.Text.Json.Serialization;
using ViciOne.SystemMonitoring.Collectors;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring;

[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(AggregatedMetricValue))]
[JsonSerializable(typeof(List<LinuxProcessInfo>))]
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSourceGenerationOptions(NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals)]
internal partial class MqttOutputGenerationContext : JsonSerializerContext
{ }
