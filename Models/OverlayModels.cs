namespace DotaPingMonitor.Models;

public enum OverlayPresetId
{
    ClassicBar,
    Minimal,
    VerticalStack,
    NetGraph,
    CyberHud
}

public class OverlayTelemetryData
{
    public long Ping { get; set; }
    public int Fps { get; set; }
    public int Cpu { get; set; }
    public int Gpu { get; set; }
    public int Ram { get; set; }
    public double RamUsedGb { get; set; }
    public double RamTotalGb { get; set; }
    public double PacketLoss { get; set; }
    public double Jitter { get; set; }
    public bool IsTimeout { get; set; }
    public bool IsSpike { get; set; }
}

public class OverlaySettings
{
    public OverlayPresetId Preset { get; set; } = OverlayPresetId.ClassicBar;
    public double BaseOpacity { get; set; } = 0.92;
    public bool IsClickThrough { get; set; }
    public double X { get; set; } = 40;
    public double Y { get; set; } = 40;
}
