using System.Collections.Generic;

namespace DotaPingMonitor.Models;

/// <summary>
/// Снимок данных диспетчера задач за один цикл измерения.
/// </summary>
public class TaskManagerSnapshot
{
    public double TotalCpuPercent { get; set; }
    public double TotalGpuPercent { get; set; }
    public ulong TotalRamBytes { get; set; }
    public ulong UsedRamBytes { get; set; }
    public double RamPercent { get; set; }

    public double TotalDiskSpeedBytesPerSec { get; set; }
    public double TotalNetworkSpeedBytesPerSec { get; set; }
    public double TotalNetworkPercent { get; set; }

    public int ProcessCount { get; set; }
    public int ThreadCount { get; set; }

    public List<ProcessItem> Processes { get; set; } = new();
}
