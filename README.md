# Aufgabenstellung System - Monitoring

## Ziel

Ein schlankes Überwachungssystem auf einem Debian Linux >=12 System einrichten, das mithilfe von c# Code verschiedene Systemmetriken überwacht und die Daten in Echtzeit an einen MQTT-Broker sendet. ViciOne stellt diese Infos dann als Events in einem baum zur Verfügung.
<mark>Damit kann die Speicherung der Daten auf dem ViciOne-Standard-Weg (Logging) erfolgen. zusätzlich können FBs die Events auswerten und weiter verarbeiten.</mark>

Es soll konfigurierbar sein welche Metrik Kollektoren geladen werden und welche davon ausgeführt werden.
Wenn mehr oder weniger Metrik Kollektoren geladen werden sollen ist ein Restart des Monitorings / Host-Managementes erforderlich (kommt ja eigentlich nie vor). Ob Metrik Kollektoren ausgeführt werden ist "On the fly" änderbar.
Um CPU Last zu schonen werden die Metrik Kollektoren NICHT parallel ausgeführt. Nur das Publishen auf dem MQTT Broker soll ein Task sein. Die Ausführung der Metrik Kollektoren soll an glatten, 1,5,15 Minutengrenzen erfolgen.
<br/>

## Zu überwachende Metriken

**CPU:** <br/>
Alle CPUs gesamt:   User, System, Idle, Steal, Guest über alle Kerne [in %]
Pro Kern:           User, System, Idle, Steal, Guest Auslastung des Kerns [in %]
Quelle: `/proc/stat`

- User Time (user + nice) <br/>
    Zeit, die die CPU für Benutzerprozesse aufwendet, sowohl mit als auch ohne Nice-Prioritäten.

- System Time (system + irq + softirq + iowait) <br/>
    Zeit, die die CPU für Systemprozesse, die Verarbeitung von Interrupts (Hardware und Software)
    sowie das Warten auf I/O-Operationen aufwendet. Dies gibt eine vollständige Übersicht über die
    Auslastung durch das Betriebssystem und Hardware-Interaktionen.

- CPU Idle Time (idle) <br/>
    Zeit, in der die CPU im Leerlauf ist.

- Steal Time (steal)
    Diese Metrik gibt die Zeit an, in der die VM eine CPU-Zeit zugewiesen bekommen hätte, aber diese
    Zeit aufgrund der Auslastung durch andere VMs nicht erhalten konnte. Mit anderen Worten: Steal
    Time zeigt, wie viel CPU-Zeit "gestohlen" wurde, weil der Hypervisor (die Software, die die
    Virtualisierung verwaltet) die CPU-Ressourcen einem anderen Gast (VM) zugeteilt hat.

- Guest Time (guest + guest_nice)<br/>
    Guest Time (guest + guest_nice): Diese Metrik gibt an, wie viel Zeit die CPU für die Ausführung
    von Prozessen innerhalb einer virtuellen Maschine (VM) aufwendet. Es umfasst sowohl Benutzerprozesse
    (user) als auch Nice-Prozesse, die innerhalb der VM laufen. Diese Metrik zeigt, wie intensiv eine VM
    die CPU nutzt.

<br/>

**RAM:** <br/>
Buffer, Cached, Free, Total, Used [in MB]
Berechnete Werte: Used =(Total - Free - Buffer - Cached), Utilization =((Used / Total) * 100) [in MB]
Quelle: `/proc/meminfo`

**Swap:** <br/>
Swap in, Swap out, Free, Total, Used [in MB]
Berechnete Werte: Utilization =((Used / Total) * 100) [in MB]
Quelle: `/proc/meminfo`

**Netzwerk je Interface:** <br/>
Name, rx_bytes, rx_packets, rx_errors, rx_dropped, rx_bandwidth,
      tx_bytes, tx_packets, tx_errors, tx_dropped, tx_bandwidth,
      collisions, carrier_errors

**Disk je mountpoint:** <br/>
Name, Drive Type, Filesystem Format, Total Size [GByte], Free Space [GByte]

**Thermal Sensors & Fans**
CPU Temperatur, Mainboard Temperatur, CPU Lüfterdrehzahl
Beispiel für plattformabhängige Ermittlung der Werte

**Uptime**
laufzeit des Systems

**NGINX:** <br/>
Anzahl der aktiven Verbindungen, Anzahl der wartenden Verbindungen, Latenzzeit

**Logs:** <br/>
Anzahl der neuen Logeinträge, gruppiert nach System, App, Security

**ViciOne Engine:** <br/>
*Je EngineHost und EngineChain:*  <br/>
Anzahl der Engines, Cycletime, Cycletime Min, Cycletime Max, Cycletime Limit

*Je EngineHost:* <br/>
CPU Load, Ram. (Ein EngineHost ist ein Prozess – darüber bekommt man CPU Load, Ram.)
Anzahl der Engines, Cycletime, Cycletime Min, Cycletime Max, Cycletime Limit müsse von ViciOne geliefert werden.
<br/>

## Bereitstellung auf dem MQTT Broker

Alle gemessenen Werte sollen zyklisch abgefragt werden.
Die Werte werden dann optional im c# Code wie folgt akkumuliert:

*Variante für schnell änderliche Werte*
     1 Minute Momentanwert
     5 Minuten Max, Min, Avg
    15 Minuten Max, Min, Avg

*Variante für langsam änderliche Werte*
     5 Minuten Momentanwert
    15 Minuten Max, Min, Avg

**Baumstruktur:**
```
    |-  SystemMonitoring
        |- [ClusterNodeId]
            |- [Metrik]
                |- [Submetrik]
                    |- [Intervall]
                        |- [ValueName] / Last oder Aggrgates [Value]
```
```
SystemMonitoring/
└── <ClusterNodeId>/
    ├── Cpu/
    │   ├── Total/
    │   │   ├── 1min/
    │   │   │   ├── User/Last
    │   │   │   ├── System/Last
    │   │   │   ├── Idle/Last
    │   │   │   ├── Guest/Last
    │   │   │   └── Steal/Last
    │   │   ├── 5min/
    │   │   │   ├── User/Aggregates
    │   │   │   ├── System/Aggregates
    │   │   │   ├── Idle/Aggregates
    │   │   │   ├── Guest/Aggregates
    │   │   │   └── Steal/Aggregates
    │   │   └── 15min/
    │   │       └── ... (wie 5min)
    │   │  
    │   ├── Cpu0/
    │   │   ├── 1min/
    │   │   │   ├── User/Last
    │   │   │   ├── System/Last
    │   │   │   ├── Idle/Last
    │   │   │   ├── Guest/Last
    │   │   │   └── Steal/Last
    │   │   ├── 5min/
    │   │   │   ├── User/Aggregates
    │   │   │   ├── System/Aggregates
    │   │   │   ├── Idle/Aggregates
    │   │   │   ├── Guest/Aggregates
    │   │   │   └── Steal/Aggregates
    │   │   └── 15min/
    │   │       └── ... (wie 5min)
    │   │  
    │   └── ...
    │
    ├── Ram/
    │   ├── 1min/
    │   │   ├── Total/Last
    │   │   ├── Free/Last
    │   │   ├── Buffer/Last
    │   │   └── Cached/Last
    │   ├── 5min/
    │   │   ├── Total/Aggregates
    │   │   ├── Free/Aggregates
    │   │   ├── Buffer/Aggregates
    │   │   └── Cached/Aggregates
    │   └── 15min/
    │       ├── Total/Aggregates
    │       ├── Free/Aggregates
    │       ├── Buffer/Aggregates
    │       └── Cached/Aggregates
    │
    ├── Disk/
    │   ├── <DiskName>/
    │   │   ├── 5min/
    │   │   │   ├── DriveType/Last
    │   │   │   ├── DriveType/Last
    │   │   │   ├── TotalSizeGB/Last
    │   │   │   └── TotalFreeSpaceGB/Last
    │   │   └── 15min/
    │   │       ├── DriveType/Aggregates
    │   │       ├── DriveType/Aggregates
    │   │       ├── TotalSizeGB/Aggregates
    │   │       └── TotalFreeSpaceGB/Aggregates
    │
    ├── Network/
    │   ├──  <InterfaceName>/
    │   │   ├── 1min/
    │   │   │   ├── rx_bytes/Last
    │   │   │   ├── rx_packets/Last
    │   │   │   ├── rx_errors/Last
    │   │   │   ├── rx_dropped/Last
    │   │   │   ├── tx_bytes/Last
    │   │   │   ├── tx_packets/Last
    │   │   │   ├── tx_errors/Last
    │   │   │   ├── tx_dropped/Last
    │   │   │   ├── collisions/Last
    │   │   │   ├── carrier_errors/Last
    │   │   │   ├── rx_bandwidth/Last
    │   │   │   └── tx_bandwidth/Last
    │   │   ├── 5min/
    │   │   │   ├── rx_bytes/Aggregates
    │   │   │   ├── rx_packets/Aggregates
    │   │   │   ├── rx_errors/Aggregates
    │   │   │   ├── rx_dropped/Aggregates
    │   │   │   ├── tx_bytes/Aggregates
    │   │   │   ├── tx_packets/Aggregates
    │   │   │   ├── tx_errors/Aggregates
    │   │   │   ├── tx_dropped/Aggregates
    │   │   │   ├── collisions/Aggregates
    │   │   │   ├── carrier_errors/Aggregates
    │   │   │   ├── rx_bandwidth/Aggregates
    │   │   │   └── tx_bandwidth/Aggregates
    │   │   └── 15min/
    │   │       ├── rx_bytes/Aggregates
    │   │       ├── rx_packets/Aggregates
    │   │       ├── rx_errors/Aggregates
    │   │       ├── rx_dropped/Aggregates
    │   │       ├── tx_bytes/Aggregates
    │   │       ├── tx_packets/Aggregates
    │   │       ├── tx_errors/Aggregates
    │   │       ├── tx_dropped/Aggregates
    │   │       ├── collisions/Aggregates
    │   │       ├── carrier_errors/Aggregates
    │   │       ├── rx_bandwidth/Aggregates
    │   │       └── tx_bandwidth/Aggregates
    │   │
    │   ├──  <InterfaceName>/
    │   │   ├── 1min/
    │   │   │   ├── ...
    │
    ├── Swap/
    │   ├── 1min/
    │   │   ├── Total/Last
    │   │   ├── Used/Last
    │   │   ├── SwapIn/Last
    │   │   └── SwapOut/Last
    │   ├── 5min/
    │   │   ├── Total/Aggregates
    │   │   ├── Used/Aggregates
    │   │   ├── SwapIn/Aggregates
    │   │   └── SwapOut/Aggregates
    │   └── 15min/
    │       ├── Total/Aggregates
    │       ├── Used/Aggregates
    │       ├── SwapIn/Aggregates
    │       └── SwapOut/Aggregates
    │
    ├── ThermalSensors/
    │   ├── 1min/
    │   │   ├── <CpuTemperature oder SensorName>/Last
    │   │   ├── <MainboardTemperature oder SensorName>/Last
    │   │   └── <CpuFan oder SensorName>/Last
    │   ├── 5min/
    │   │   ├── <CpuTemperature oder SensorName>/Aggregates
    │   │   ├── <MainboardTemperature oder SensorName>/Aggregates
    │   │   └── <CpuFan oder SensorName>/Aggregates
    │   └── 15min/
    │       ├── <CpuTemperature oder SensorName>/Aggregates
    │       ├── <MainboardTemperature oder SensorName>/Aggregates
    │       └── <CpuFan oder SensorName>/Aggregates
    │
    ├── Uptime/
    │   └── 1min/
    │       └── UptimeSeconds/Last
    │
    ├── Nginx/
    │   ├── 1min/
    │   │   ├── ActiveConnections/Last
    │   │   ├── WaitingConnections/Last
    │   │   └── LatencyMs/Last
    │   ├── 5min/
    │   │   ├── ActiveConnections/Aggregates
    │   │   ├── WaitingConnections/Aggregates
    │   │   └── LatencyMs/Aggregates
    │   └── 15min/
    │       ├── ActiveConnections/Aggregates
    │       ├── WaitingConnections/Aggregates
    │       └── LatencyMs/Aggregates
    │
    └── Processes/
        └── 1min
            └── JsonPayload of all Process Information
```
