namespace ViciOne.SystemMonitoring.Collectors;

internal class RawNetworkStats
{
    public ulong RxBytes { get; init; }
    public ulong RxPackets { get; init; }
    public ulong RxErrors { get; init; }
    public ulong RxDropped { get; init; }
    public ulong TxBytes { get; init; }
    public ulong TxPackets { get; init; }
    public ulong TxErrors { get; init; }
    public ulong TxDropped { get; init; }
    public ulong Collisions { get; init; }
    public ulong CarrierErrors { get; init; }
    public int? Speed { get; set; }
    public string? Duplex { get; set; }
}
