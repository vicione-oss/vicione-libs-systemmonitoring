# Changelog

## 1.1.0 - Unreleased

### Added

- `ILiveMonitoring`, reads the enabled collectors every few seconds while subscribed, without changing the regular values; shares and rates in a sample cover the subscription's own interval
- `ILiveMetricsCollector`, the extension point for custom collectors that can also be read live; they keep the baseline for values computed from counters in the `LiveWindow` they are handed
- `LinuxProcessInfo.StartTime`, when the process started
- `DiskIoMetricsCollector` (`disk_io`), read and write throughput, written bytes, writes per second and busy time per disk
- `CpuFrequencyMetricsCollector` (`cpu_frequency`), current and maximum clock per core, the clock the kernel currently allows and the clock utilisation
- `network` collector, `carrier` tells whether a cable is plugged in, and `conduit` names the interface behind a switch port such as `lan1`
- `cpu` collector, `iowait` is the idle time in which a task waited for I/O, already contained in `idle`
- `loadavg` collector, `online_cpus` is the number of CPUs the host schedules on, to relate the load to
- `swap` collector, `swap_in_per_second` and `swap_out_per_second` are the pages swapped per second, independent of the sampling interval
- `ram` collector, `page_cache_refaults_per_second` is the number of recently evicted page cache pages read back per second, a sign of memory pressure even without swap
- `ThermalZoneMetricsCollector` (`thermal_zones`), temperature, passive and critical trip point and throttling state of each kernel thermal zone

### Changed

- `swap/…/total` and `swap/…/used` carry the current swap size in kB instead of its change since the previous reading. **Behaviour change:** a consumer that added the values up now gets the size itself

### Fixed

- The uptime and the thermal sensors are read correctly whatever the culture of the process
- Processes with a space in their name report their CPU usage and start time correctly
- `cpu` counts the time spent running virtual machines once: the kernel also counts it in `user`, so it was added to the total twice and all shares came out too low on a host with busy guests. `user` now leaves it out, and `total_system` adds `guest`

## 1.0.1 - 2026-03-27

### Fixed

- Add Int32 DataType to `MqttOutput`

## 1.0.0 - 2026-03-20

### Added

- Add `LoadAverageMetricsCollector`

### Changed

- Update `.NET` to `10.0`
- Update `Microsoft.Extensions.Configuration.Abstractions` to `10.0.3`
- Update `Microsoft.Extensions.DependencyInjection` to `10.0.3`
- Update `Microsoft.Extensions.DependencyInjection.Abstractions` to `10.0.0`
- Update `Microsoft.Extensions.Hosting` to `10.0.3`
- Update `Microsoft.Extensions.Hosting.Abstractions` to `10.0.3`
- Update `Microsoft.Extensions.Logging.Abstractions` to `10.0.3`
- Update `Microsoft.Extensions.Options` to `10.0.3`
- Update `MQTTnet` to `5.1.0.1559`
- Update `System.IO.Abstractions` to `22.1.0`

## 0.10.0 - 2025-12-03

### Added

- Publish metrics for RX and TX bandwidth usage for non full duplex interfaces
- Add units to metrics
- Add wrap-around handling
- Limit percentages in network usage

### Removed

- Decreasing byte counts handling in NetworkMetricsCollector
- Reduce log output on connection loss to MQTT

### Changed

- Round network bandwidth values to two decimal digits

## 0.9.0 - 2025-05-27

### Fixed

- Calculate network metrics correctly when counting backwards
- Use larger types when reading network statistic

## 0.8.0 - 2025-05-19

### Added

- Support custom serialization of metrics in mqtt output
- Support `Dictionary<string, int>` for default serialization in mqtt output

## 0.7.0 - 2025-04-30

### Removed

- **Breaking:** Remove per core usage of process info (it was not correct)

### Fixed

- Extend regarded time frame between prepare and first run
- Calculate cpu usage of process info with jiffies

## 0.6.0 - 2025-04-29

### Fixed

- Calculate cpu usage correctly by using the jiffies
- Count IO wait time as idle time

## 0.5.0 - 2025-04-22

### Added

- Provide a total system value for cpu metrics
- Add check if network speed is negative
- Add check if network received/transmitted difference is negative

### Changed

- Change access modifier of IScheduledMetricsCollector to public
- Change access modifier of ToMetrics to public

## 0.4.0 - 2025-03-18

### Added

- Reconnect mqtt client on connection lost

### Fixed

- Error on collecting aggregated network metrics

## 0.3.0 - 2025-03-12

### Changed

- Update `Microsoft.Extensions.Configuration.Abstractions` to version `9.0.3`
- Update `Microsoft.Extensions.Hosting.Abstractions` to version `9.0.3`
- Update `Microsoft.Extensions.Logging.Abstractions` to version `9.0.3`
- Update `Microsoft.Extensions.Options` to version `9.0.3`
- Update `Microsoft.Extensions.DependencyInjection.Abstractions` to version `9.0.3`
- Update `System.IO.Abstraction` to version `22.0.11`

### Fixed

- Regard specific network metrics as optional

## 0.2.0 - 2025-03-03

### Added

- Provide network usage, speed and duplex mode
- Support multiple outputs

### Changed

- Pass metrics as batch to output providers

### Removed

- Remove limited in processor name

## 0.1.0 - 2025-02-26

- Initial release
