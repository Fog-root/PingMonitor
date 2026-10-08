using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using DotaPingMonitor.Services;

namespace DotaPingMonitor.Models;

public enum SpeedtestPhase
{
    Idle,
    Connecting,
    Latency,
    Download,
    Upload,
    Completed,
    Cancelled,
    Failed
}

public class SpeedtestResult : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime Timestamp { get; set; } = DateTime.Now;

    public double DownloadSpeedMbps { get; set; }

    public double DownloadPeakMbps { get; set; }

    public double UploadSpeedMbps { get; set; }

    public double PingMs { get; set; }

    public double JitterMs { get; set; }

    public string ClientIp { get; set; } = string.Empty;

    public string IspName { get; set; } = string.Empty;

    public string ServerLocation { get; set; } = string.Empty;

    public string ServerColo { get; set; } = string.Empty;

    [JsonIgnore]
    public string FormattedDate => Timestamp.ToString("dd.MM.yyyy HH:mm");

    [JsonIgnore]
    public string DownloadText => LocalizationService.FormatSpeed(DownloadSpeedMbps);

    [JsonIgnore]
    public string DownloadPeakText => LocalizationService.FormatSpeed(DownloadPeakMbps);

    [JsonIgnore]
    public string UploadText => LocalizationService.FormatSpeed(UploadSpeedMbps);

    [JsonIgnore]
    public string PingText => $"{PingMs:F0} ms";

    [JsonIgnore]
    public string JitterText => LocalizationService.FormatJitter(JitterMs);

    public double LoadedPingDownloadMs { get; set; }
    public double LoadedPingUploadMs { get; set; }
    public double BufferbloatDeltaMs { get; set; }
    public string BufferbloatGrade { get; set; } = "A+";

    [JsonIgnore]
    public string BufferbloatText => BufferbloatDeltaMs > 0 
        ? $"+{BufferbloatDeltaMs:F0} ms ({BufferbloatGrade})" 
        : $"0 ms ({BufferbloatGrade})";

    [JsonIgnore]
    public string ServerFullText => !string.IsNullOrEmpty(ServerColo) 
        ? $"{ServerLocation} ({ServerColo})" 
        : (string.IsNullOrEmpty(ServerLocation) ? "Cloudflare Anycast" : ServerLocation);

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(DownloadText));
        OnPropertyChanged(nameof(DownloadPeakText));
        OnPropertyChanged(nameof(UploadText));
        OnPropertyChanged(nameof(PingText));
        OnPropertyChanged(nameof(JitterText));
    }
}

public class SpeedtestProgressReport
{
    public SpeedtestPhase Phase { get; set; }

    public double CurrentSpeedMbps { get; set; }

    public double PeakSpeedMbps { get; set; }

    public double AverageSpeedMbps { get; set; }

    public double DownloadSpeedMbps { get; set; }

    public double DownloadPeakMbps { get; set; }

    public double UploadSpeedMbps { get; set; }

    public double PhaseProgress { get; set; } // 0.0 .. 1.0

    public double TotalProgress { get; set; } // 0.0 .. 1.0

    public double PingMs { get; set; }

    public double JitterMs { get; set; }

    public string ClientIp { get; set; } = string.Empty;

    public string IspName { get; set; } = string.Empty;

    public string ServerLocation { get; set; } = string.Empty;

    public string ServerColo { get; set; } = string.Empty;

    public double BufferbloatDeltaMs { get; set; }

    public string BufferbloatGrade { get; set; } = string.Empty;

    public string StatusMessage { get; set; } = string.Empty;
}
