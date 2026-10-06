using CommunityToolkit.Mvvm.ComponentModel;

namespace DotaPingMonitor.Models;

public enum HopNodeCategory
{
    LocalRouter,
    IspGateway,
    Transit,
    Destination
}

public partial class MtrHopResult : ObservableObject
{
    public int HopIndex { get; set; }
    public string IpAddress { get; set; } = "*";
    public string Hostname { get; set; } = string.Empty;
    public HopNodeCategory Category { get; set; }

    [ObservableProperty]
    private string _categoryName = string.Empty;

    public string CategoryIcon { get; set; } = string.Empty;
    public string CategoryColor { get; set; } = "#57C1FF";
    public string CategoryBg { get; set; } = "#1857C1FF";

    public long MinPingMs { get; set; } = -1;
    public long AvgPingMs { get; set; } = -1;
    public long MaxPingMs { get; set; } = -1;
    public double LossPercent { get; set; } = 0.0;
    public int SentProbes { get; set; } = 0;
    public int ReceivedProbes { get; set; } = 0;

    [ObservableProperty]
    private string _pingDisplay = "-- ms";

    public string LossDisplay { get; set; } = "0%";
    public string LossColor { get; set; } = "#59D499";
    public string LossBg { get; set; } = "#1859D499";

    [ObservableProperty]
    private string _statusText = "В норме";

    public string StatusColor { get; set; } = "#59D499";
    public string StatusBg { get; set; } = "#1859D499";

    public bool IsFailureNode { get; set; } = false;
}

public class MtrReport
{
    public string TargetName { get; set; } = string.Empty;
    public string TargetIp { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public List<MtrHopResult> Hops { get; set; } = new();

    public string VerdictTitle { get; set; } = string.Empty;
    public string VerdictDescription { get; set; } = string.Empty;
    public string VerdictIcon { get; set; } = "✓";
    public string VerdictColor { get; set; } = "#59D499";
    public string VerdictBg { get; set; } = "#1859D499";
    public string ProblemNodeText { get; set; } = string.Empty;
}
