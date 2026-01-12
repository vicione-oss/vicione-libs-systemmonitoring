using System.IO.Abstractions;
using System.Runtime.Versioning;
using ViciOne.SystemMonitoring.Metrics;

namespace ViciOne.SystemMonitoring.Collectors;

[SupportedOSPlatform("linux")]
public sealed class ProcessorMetricsCollector(IFileSystem fileSystem) : IOnetimeMetricsCollector
{
    public string Name => "processor";

    public async Task<List<Metric>> CollectMetrics(int interval, DateTimeOffset cycleStartTime, CancellationToken cancellationToken)
    {
        try
        {
            var processor = await CollectProcessor();

            return [new()
            {
                Group = Name,
                Name = "processor",
                IntervalInMinutes = 0,
                Value = processor,
                Unit = MetricUnit.None,
            }];
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error collecting processor metrics.", ex);
        }
    }

    private async Task<string> CollectProcessor()
    {
        try
        {
            return await GetFullProcessorName();
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException("Permission error reading processor.", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException("I/O error reading processor.", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error reading processor.", ex);
        }
    }

    private async Task<string> GetFullProcessorName()
    {
        var processorName = "Unknown Processor";

        // Try /proc/cpuinfo for x86 and ARM-based systems
        if (fileSystem.File.Exists("/proc/cpuinfo"))
        {
            string? vendor = null;
            string? model = null;

            await foreach (var line in fileSystem.File.ReadLinesAsync("/proc/cpuinfo"))
            {
                if (line.StartsWith("model name", StringComparison.InvariantCulture))
                    return line.Split(':').Last().Trim(); // Full CPU name for x86

                if (line.StartsWith("vendor_id", StringComparison.InvariantCulture))
                    vendor = line.Split(':').Last().Trim();

                if (line.StartsWith("CPU implementer", StringComparison.InvariantCulture))
                    vendor = MapArmVendor(line.Split(':').Last().Trim());

                if (line.StartsWith("CPU part", StringComparison.InvariantCulture))
                    model = MapArmPart(line.Split(':').Last().Trim());
            }

            if (!string.IsNullOrEmpty(vendor) && !string.IsNullOrEmpty(model))
                return $"{vendor} {model}";
        }

        // Try /proc/device-tree/cpus/cpu@0/compatible (for ARM-based systems)
        if (fileSystem.File.Exists("/proc/device-tree/cpus/cpu@0/compatible"))
        {
            processorName = (await fileSystem.File.ReadAllTextAsync("/proc/device-tree/cpus/cpu@0/compatible"))
                .Trim('\0', ' ', '\n', '\r');
            if (!string.IsNullOrEmpty(processorName)) return processorName;
        }

        // Try /sys/devices/system/cpu/cpu0/uevent for alternative ARM details
        if (fileSystem.File.Exists("/sys/devices/system/cpu/cpu0/uevent"))
        {
            await foreach (var line in fileSystem.File.ReadLinesAsync("/sys/devices/system/cpu/cpu0/uevent"))
            {
                if (line.StartsWith("OF_COMPATIBLE_0=", StringComparison.InvariantCulture))
                    return line.Split('=').Last().Trim();
            }
        }

        return processorName;
    }

    private static string MapArmVendor(string hexId)
        => hexId switch
        {
            "0x41" => "ARM",
            "0x42" => "Broadcom",
            "0x43" => "Cavium",
            "0x44" => "DEC",
            "0x46" => "Fujitsu",
            "0x48" => "HiSilicon",
            "0x49" => "Infineon",
            "0x4d" => "Motorola",
            "0x4e" => "NVIDIA",
            "0x50" => "APM",
            "0x51" => "Qualcomm",
            "0x56" => "Marvell",
            "0x69" => "Intel",
            _ => $"Unknown Vendor (ID: {hexId})"
        };

    private static string MapArmPart(string partId)
        => partId switch
        {
            "0xd03" => "Cortex-A53",
            "0xd07" => "Cortex-A57",
            "0xd08" => "Cortex-A72",
            "0xd09" => "Cortex-A73",
            "0xd0a" => "Cortex-A75",
            "0xd0b" => "Cortex-A76",
            "0xd0c" => "Cortex-A77",
            "0xd0d" => "Cortex-A78",
            "0xd40" => "Neoverse N1",
            _ => $"Unknown CPU Part (ID: {partId})"
        };
}
