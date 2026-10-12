namespace ViciOne.SystemMonitoring.Collectors;

internal static class NetworkStatsFactory
{
    public static NetworkStats CreateNetworkStats(RawNetworkStats currentStats, RawNetworkStats previousStats, double timeDifference)
        => CalculateNetworkMetric(currentStats, previousStats)
            .CalculateBandwidth(timeDifference)
            .CalculateBandwidthUsage();

    private static NetworkStats CalculateBandwidthUsage(this NetworkStats currentStats)
    {
        if (currentStats.SpeedMBps.HasValue && !string.IsNullOrEmpty(currentStats.Duplex) &&
            currentStats.ReceiveBandwidth.HasValue && currentStats.TransmittBandwidth.HasValue)
        {
            var speedBps = currentStats.SpeedMBps.Value * 1_000_000;
            var rxBandwidthBps = currentStats.ReceiveBandwidth.Value * 8;
            var txBandwidthBps = currentStats.TransmittBandwidth.Value * 8;

            currentStats.ReceiveBandwidthUsage = KeepPercentInBounds(Math.Round(rxBandwidthBps / speedBps * 100, 2));
            currentStats.TransmittBandwidthUsage = KeepPercentInBounds(Math.Round(txBandwidthBps / speedBps * 100, 2));

            if (currentStats.Duplex == "full")
                currentStats.TotalBandwidthUsage = KeepPercentInBounds(Math.Round((rxBandwidthBps + txBandwidthBps) / (speedBps * 2) * 100, 2));
            else
                currentStats.TotalBandwidthUsage = KeepPercentInBounds(Math.Round((rxBandwidthBps + txBandwidthBps) / speedBps * 100, 2));
        }

        return currentStats;
    }

    private static double KeepPercentInBounds(double result)
    {
        if (result < 0)
            result = 0;

        if (result > 100)
            result = 100;

        return result;
    }

    private static NetworkStats CalculateNetworkMetric(RawNetworkStats currentData, RawNetworkStats previousData)
        => new()
        {
            ReceivedBytes = CalculateDifferenceCountingInWrapAround(currentData.RxBytes, previousData.RxBytes),
            ReceivedPackets = CalculateDifferenceCountingInWrapAround(currentData.RxPackets, previousData.RxPackets),
            ReceivedErrors = CalculateDifferenceCountingInWrapAround(currentData.RxErrors, previousData.RxErrors),
            ReceivedPacketsDropped = CalculateDifferenceCountingInWrapAround(currentData.RxDropped, previousData.RxDropped),
            TransmittedBytes = CalculateDifferenceCountingInWrapAround(currentData.TxBytes, previousData.TxBytes),
            TransmittedPackets = CalculateDifferenceCountingInWrapAround(currentData.TxPackets, previousData.TxPackets),
            TransmittedErrors = CalculateDifferenceCountingInWrapAround(currentData.TxErrors, previousData.TxErrors),
            TransmittedPacketsDropped = CalculateDifferenceCountingInWrapAround(currentData.TxDropped, previousData.TxDropped),
            Collisions = CalculateDifferenceCountingInWrapAround(currentData.Collisions, previousData.Collisions),
            CarrierErrors = CalculateDifferenceCountingInWrapAround(currentData.CarrierErrors, previousData.CarrierErrors),
            SpeedMBps = currentData.Speed,
            Duplex = currentData.Duplex,
            Carrier = currentData.Carrier,
            Conduit = currentData.Conduit,
        };

    internal static double CalculateDifferenceCountingInWrapAround(ulong current, ulong previous)
    {
        ulong result;

        if (current >= previous || previous > uint.MaxValue)
        {
            result = current - previous;
        }
        else
        {
            result = (uint)current - (uint)previous;
        }

        return Convert.ToDouble(result);
    }

    private static NetworkStats CalculateBandwidth(this NetworkStats currentData, double timeDifference)
    {
        currentData.ReceiveBandwidth = currentData.ReceivedBytes / timeDifference;
        currentData.TransmittBandwidth = currentData.TransmittedBytes / timeDifference;

        return currentData;
    }
}
