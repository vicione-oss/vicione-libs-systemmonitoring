namespace ViciOne.SystemMonitoring.Collectors;

public class MemoryDetails
{
    public long VmSizeKb { get; set; } // Virtual Memory
    public long VmRSSKb { get; set; }  // Physical Memory
    public long VmPeakKb { get; set; } // Peak Memory
    public long VmSwapKb { get; set; } // Swap Memory

    public bool IsValid()
        => VmSizeKb >= 0 && VmRSSKb >= 0 && VmPeakKb >= 0 && VmSwapKb >= 0;
}
