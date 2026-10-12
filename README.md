# ViciOne System Monitoring

A lightweight .NET library that collects system metrics on Linux and publishes them through outputs such as an MQTT
broker. ViciOne turns the published values into events in its tree, so they can be logged the standard ViciOne way and
evaluated by function blocks.

- Runs on Linux (Debian 12 or later), targets .NET 10.
- Collectors run one after another, never in parallel, to keep the CPU load low; only publishing runs on its own task.
- Which collectors are loaded is fixed at startup; which of them run can be changed at runtime through `EnabledMetrics`.
- Values can also be read every few seconds on demand, see [Live sampling](#live-sampling).

## Getting started

```csharp
services
    .Configure<MonitoringConfig>(configuration.GetSection("Monitoring"))
    .AddSystemMonitoring(b => b
        .UseMqttOutput()
        .UseDefaultCollectors());
```

```json
{
    "Monitoring": {
        "ClusterNodeId": "my-node",
        "NodeType": "DefaultDebian",
        "EnabledMetrics": {
            "Cpu": true,
            "Ram": true,
            "Disk": true,
            "Network": true
        },
        "Mqtt": {
            "Host": "localhost",
            "Port": 1883
        }
    }
}
```

`samples/Sample` is a runnable host with this setup.

## Configuration

`MonitoringConfig`:

| Setting | Description |
|---|---|
| `ClusterNodeId` | Required. Second level of every MQTT topic. |
| `NodeType` | How the thermal sensors are found: `DefaultDebian` and `Unknown` read every `/sys/class/hwmon` sensor, `RaspberryPi3`/`4`/`5` read the CPU and mainboard thermal zones and the fan; for `EdgeS`, `EdgeM`, `EC10`, `EC20` and `X8` the `thermal_sensors` collector returns nothing. |
| `EnabledMetrics` | Collector name → `true`/`false`. A collector that is missing here does not run. Keys are case-insensitive. |
| `Mqtt` | Broker connection: `Host`, `Port`, `Username`, `Password`, `CertificateFile`, `CertificateFilePassword`, `CertificatePrivateKeyFile`, `SslProtocol`, `DisableCertificateValidation`. |

## Scheduling and aggregation

- The monitoring ticks once a minute, starting from when the host starts.
- On a tick whose wall-clock minute is divisible by 15, the interval is 15. Otherwise it is 5 when the minute is divisible by 5, and 1 in all other cases.
- A collector runs on its first tick and then whenever the interval is a multiple of its own interval (1, 5 or 15 minutes).
- A one-time collector runs on the first tick only.
- Collectors publish the current value as `Last`. Every 5 and 15 minutes, `cpu`, `cpu_frequency`, `ram`, `swap`, `disk_io`, `network`, `thermal_sensors`, `thermal_zones` and `nginx` add the minimum, average and maximum since the previous aggregate as `Aggregates`; `disk` adds only the 15-minute one, `thermal_zones` leaves out its trip points, and `loadavg`, `uptime` and `process` publish `Last` only.
- Values derived from counters (CPU, process CPU, network bandwidth, disk I/O, swapped pages, page cache refaults) cover the time since the previous reading.

## Collectors

`UseDefaultCollectors()` registers every collector below except `nginx`. The name is the key in `EnabledMetrics` and the
third level of the MQTT topic.

| Name | Interval | Live | Subgroup | Metrics |
|---|---|---|---|---|
| `cpu` | 1 min | yes | `total`, `core0` … | `user` (user + nice, without guest), `system` (system + irq + softirq), `idle` (idle + iowait), `iowait` (the part of `idle` in which a task on that CPU waited for I/O; a busy CPU accrues none, so one stalled task shows at most 1/number of cores in `total`), `steal`, `guest` (guest + guest_nice) [%]; `total` adds `total_system` (user + system + steal + guest) [%] |
| `cpu_frequency` | 1 min | yes | `total`, `core0` … | `current`, `limit` (the clock the kernel currently allows), `maximum` [MHz]; `total` holds the average `current`, the highest `maximum`, the clock utilisation `share_of_maximum` (average of `current` / `maximum` per core) and `limit_share` (lowest `limit` / `maximum` of a core) [%]. An idle core runs slow under the ondemand and schedutil governors |
| `ram` | 1 min | yes | – | `total`, `free`, `available`, `buffer`, `cached` [kB]; `actual_memory_usage` (like htop), `used_memory_usage` (like top), `total_memory_usage` (like free) [%]; `page_cache_refaults_per_second`, page cache pages read back from disk shortly after they were evicted, which rises when memory is too small for the working set, with or without swap (not on the first reading; needs kernel 5.9) |
| `swap` | 1 min | yes | – | `total`, `used` [kB]; `swap_in`, `swap_out` [pages] since the previous reading; `swap_in_per_second`, `swap_out_per_second` [pages/s] (not on the first reading) |
| `disk` | 5 min | yes | mount point, `/` replaced by `\` | `driveType`, `driveFormat`, `totalSizeGB`, `totalFreeSpaceGB` [GB], `usage` [%] |
| `disk_io` | 1 min | yes | device, e.g. `mmcblk0` | `read_bytes_per_second`, `write_bytes_per_second` [B/s], `written_bytes` [B], `writes_per_second`, `busy` [%]; whole physical disks only |
| `network` | 1 min | yes | interface, except `lo` | `rx_`/`tx_` `bytes` [B], `packets`, `errors`, `dropped`, `bandwidth` [B/s]; `collisions`, `carrier_errors`; `carrier` (1 with a cable, 0 without); when the link reports it: `speed` [Mbit/s], `duplex`, `rx_`/`tx_`/`total_bandwidth_usage` [%]; for a port of a built-in switch (DSA): `conduit`, the interface that carries and counts its traffic |
| `loadavg` | 1 min | yes | – | `load1`, `load5`, `load15`, `running_kernel_scheduling_entities`, `total_kernel_scheduling_entities`; `online_cpus`, the number of CPUs the load relates to, left out when `/sys/devices/system/cpu/online` cannot be read. The load counts runnable tasks and tasks blocked in uninterruptible sleep, mostly waiting for I/O, so `load1` / `online_cpus` is demand, not CPU utilisation |
| `thermal_sensors` | 1 min | yes | – | One value per sensor and fan, named `<hwmon name>_<label>`; temperatures [°C], fan speeds without unit; see `NodeType` |
| `thermal_zones` | 1 min | yes | zone type, e.g. `cpu-thermal`, `/` replaced by `\`, `+` and `#` by `_`; with the zone number when the device has several zones of that type | `temperature`, the lowest `passive_trip` (where the kernel starts to throttle) and `critical_trip` (where it shuts down) [°C]; `throttling`, the highest state of the cooling devices bound to a passive trip as a share of their maximum [%]. A state is a step such as a cpufreq operating point, not a share of the clock (see `cpu_frequency` `limit_share`), and a device bound to several zones shows the same state in each. A trip point or `throttling` the zone does not have is left out. Firmware that throttles on its own, as on a Raspberry Pi, binds no cooling device, so there neither `passive_trip` nor `throttling` tells whether the CPU is throttled; `critical_trip` is not a throttle threshold. Disabled zones and zones that cannot be read are skipped |
| `uptime` | 1 min | yes | – | `uptime_seconds` [s] |
| `process` | 1 min | yes | – | `processes`: JSON list with `Name`, `State`, `Pid`, `PPid`, `Threads`, `CpuUsage` [% of all cores], `MemoryDetails` (`VmSizeKb`, `VmRSSKb`, `VmPeakKb`, `VmSwapKb`), `MemoryUsageKb`, `CmdLine`, `StartTime` |
| `processor` | once | no | – | `processor`: processor name from `/proc/cpuinfo`, published under `0min` |
| `nginx` | 1 min | no | – | `activeConnections`, `waitingConnections`, `latencyMs`; reads the stub status URL from the configuration key `nginx:Url` |

Sources: `/proc/stat`, `/proc/meminfo`, `/proc/vmstat`, `/proc/diskstats`, `/proc/net/dev`, `/proc/loadavg`,
`/proc/uptime`, `/proc/[pid]`, `/proc/cpuinfo`, `/sys/class/net`, `/sys/class/hwmon`, `/sys/class/thermal`,
`/sys/devices/system/cpu/cpu*/cpufreq`.

## MQTT output

Topic:

```
monitoring/<ClusterNodeId>/<collector>/<interval>min[/<subgroup>]/<metric>/Last
monitoring/<ClusterNodeId>/<collector>/<interval>min[/<subgroup>]/<metric>/Aggregates
```

Examples:

```
monitoring/my-node/cpu/1min/total/total_system/Last           42.5
monitoring/my-node/cpu/5min/core0/user/Aggregates             {"Min":3.1,"Avg":12.4,"Max":40.2}
monitoring/my-node/network/1min/eth0/rx_bandwidth/Last        18250
monitoring/my-node/process/1min/processes/Last                [{"Name":"systemd","Pid":1,…}]
```

- The payload is JSON: a number, a string, an `Aggregates` object or the process list.
- Every message carries the user properties `Timestamp` (ISO 8601, start of the tick) and `Unit`.
- Messages are published with QoS 2.

## Live sampling

`ILiveMonitoring` reads the enabled live collectors at a short interval while at least one subscription is held:

```csharp
using var subscription = liveMonitoring.Subscribe(TimeSpan.FromSeconds(2), sample => Show(sample.Metrics));
```

- Each subscription receives a sample every interval it asked for, from 100 ms to 1 day. Subscriptions with the same interval share one read per sample; each other interval reads on its own.
- Disposing the subscription also stops its handler; a handler that throws is logged once and affects no other subscriber.
- Live metrics have the interval 0, are not aggregated and go only to the subscribers, never to an output. Shares and rates in a live sample cover the subscription's interval, whatever other subscriptions ask for; a collector that was disabled or failed in the previous read is left out of the next sample instead of covering the gap.
- The regular values stay the same as without live sampling.

## Extending

- **Collector:** implement `IScheduledMetricsCollector` (interval 1, 5 or 15 minutes) or `IOnetimeMetricsCollector`. Add
  `IPrepareMetricsCollector` to take a first reading at startup and `ILiveMetricsCollector` to support live sampling; a
  live collector keeps the baseline for values computed from counters in the `LiveWindow` it is handed. Register it with
  `AddCollector<T>()`.
- **Output:** implement `IOutput` (`Connect`, `Publish`, `Disconnect`) and register it with `AddOutput<T>()`.

## Contributing

### Code Formatting

This project enforces code style rules via [`.editorconfig`](.editorconfig). Before committing, run:

```sh
dotnet format system-monitoring.slnx
```
