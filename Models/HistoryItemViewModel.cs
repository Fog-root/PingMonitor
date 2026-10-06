using System.ComponentModel;
using System.Runtime.CompilerServices;
using DotaPingMonitor.Services;
using DotaPingMonitor.ViewModels;

namespace DotaPingMonitor.Models;

public class HistoryItemViewModel : INotifyPropertyChanged
{
    public long RawPingMs { get; set; }
    public bool RawIsTimeout { get; set; }
    public bool RawIsSpike { get; set; }

    private string _pingColor = string.Empty;
    private string _packetLossColor = string.Empty;
    private string _statusColor = string.Empty;
    private string _statusBg = string.Empty;

    public string Time { get; set; } = string.Empty;
    public string Ping { get; set; } = string.Empty;

    public string PingColor
    {
        get => _pingColor;
        set { _pingColor = value; OnPropertyChanged(); }
    }

    public string PacketLoss { get; set; } = "0%";

    public string PacketLossColor
    {
        get => _packetLossColor;
        set { _packetLossColor = value; OnPropertyChanged(); }
    }

    public string Status { get; set; } = string.Empty;

    public string StatusColor
    {
        get => _statusColor;
        set { _statusColor = value; OnPropertyChanged(); }
    }

    public string StatusBg
    {
        get => _statusBg;
        set { _statusBg = value; OnPropertyChanged(); }
    }

    public string Server { get; set; } = string.Empty;
    public string DisplayServer => PingTargetItemViewModel.GetLocalizedName(Server);

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(DisplayServer));
    }

    public void RefreshTheme()
    {
        if (RawIsTimeout || RawPingMs <= 0)
        {
            PingColor = ThemeService.Current.AccentRed;
            PacketLossColor = ThemeService.Current.AccentRed;
            StatusColor = ThemeService.Current.AccentRed;
            StatusBg = ThemeService.Current.AccentRedSoft;
        }
        else
        {
            var (hex, softHex) = ThemeService.GetPingColor(RawPingMs);
            PingColor = hex;
            StatusColor = hex;
            StatusBg = softHex;
            PacketLossColor = ThemeService.Current.AccentGreen;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
