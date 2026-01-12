namespace ViciOne.SystemMonitoring.Collectors;

internal class NetworkStats
{
    public double ReceivedBytes { get; init; }
    public double ReceivedPackets { get; init; }
    public double ReceivedErrors { get; init; }
    public double ReceivedPacketsDropped { get; init; }
    public double TransmittedBytes { get; init; }
    public double TransmittedPackets { get; init; }
    public double TransmittedErrors { get; init; }
    public double TransmittedPacketsDropped { get; init; }
    public double Collisions { get; init; }
    public double CarrierErrors { get; init; }
    public double? SpeedMBps { get; set; }
    public string? Duplex { get; set; }
    public double? ReceiveBandwidth { get; set; }
    public double? TransmittBandwidth { get; set; }
    public double? TotalBandwidthUsage { get; set; }
    public double? ReceiveBandwidthUsage { get; set; }
    public double? TransmittBandwidthUsage { get; set; }
}
