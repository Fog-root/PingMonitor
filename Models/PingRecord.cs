namespace DotaPingMonitor.Models;

public class PingRecord
{
    public int Id { get; set; }

    public DateTime Timestamp { get; set; }

    public long PingMs { get; set; }

    public bool IsSpike { get; set; }

    public bool IsTimeout { get; set; }

    public string Server { get; set; } = string.Empty;

    // Сегментация сетевого пути (Роутер / Провайдер / Магистраль)
    public long RouterPingMs { get; set; } = -1;

    public long IspPingMs { get; set; } = -1;

    public string RouterIp { get; set; } = string.Empty;

    public string IspIp { get; set; } = string.Empty;

    public string IncidentCategory { get; set; } = string.Empty; // "Router", "Isp", "Backbone", "None"

    public string IncidentDescription { get; set; } = string.Empty;
}