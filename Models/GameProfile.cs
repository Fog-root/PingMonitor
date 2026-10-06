using DotaPingMonitor.Services;
using DotaPingMonitor.ViewModels;

namespace DotaPingMonitor.Models;

/// <summary>
/// Профиль дисциплины / игры с настройками кластеров, серверов и Valve SDR API
/// </summary>
public class GameProfile
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = string.Empty;
    public int SteamAppId { get; set; }
    public string HeaderTitle { get; set; } = string.Empty;
    public string SdrHeaderTitle { get; set; } = string.Empty;
    public string SdrHeaderBadge { get; set; } = "RELAY";
    public string Subtitle { get; set; } = "Телеметрия сети и Steam Datagram Relay";
    public string SubtitleEn { get; set; } = "Network telemetry & Steam Datagram Relay";
    public string DefaultServerName { get; set; } = string.Empty;
    public string LogoColorHex { get; set; } = "#59D499";
    public string DefaultRouteName { get; set; } = "Прямой (Direct)";
    public List<PingTargetItemViewModel> Targets { get; set; } = new();
    public List<ValvePop> DefaultPops { get; set; } = new();
}
