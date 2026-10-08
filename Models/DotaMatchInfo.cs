namespace DotaPingMonitor.Models;

/// <summary>
/// Информация об активном матче Dota 2 и выделенном сервере/релее Valve SDR
/// </summary>
public class DotaMatchInfo
{
    public bool IsConnected { get; set; }

    /// <summary>
    /// Идентификатор игры (dota2, cs2, deadlock)
    /// </summary>
    public string GameId { get; set; } = "dota2";

    /// <summary>
    /// Отображаемое название игры (Dota 2, CS2, Deadlock)
    /// </summary>
    public string GameName { get; set; } = "Dota 2";

    /// <summary>
    /// Код кластера Valve POP (например: sto2, fra, vie, waw, lux)
    /// </summary>
    public string ClusterCode { get; set; } = string.Empty;

    /// <summary>
    /// Локализованное название региона/города (например: Стокгольм, Вена, Франкфурт)
    /// </summary>
    public string ClusterDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// IP-адрес первичного SDR релея Valve
    /// </summary>
    public string RelayIp { get; set; } = string.Empty;

    /// <summary>
    /// UDP порт релея Valve (например: 27055)
    /// </summary>
    public int RelayPort { get; set; }

    /// <summary>
    /// Время подключения к матчу
    /// </summary>
    public DateTime ConnectedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// SteamID игрового сервера (например: [A:1:2163277855:51707])
    /// </summary>
    public string ServerSteamId { get; set; } = string.Empty;

    /// <summary>
    /// Исходная строка из console.log
    /// </summary>
    public string RawLogLine { get; set; } = string.Empty;

    /// <summary>
    /// Внутренняя задержка сети Valve (Backhaul / Interior) от входного релея до хоста матча
    /// </summary>
    public int BackhaulLatencyMs { get; set; }

    /// <summary>
    /// Задержка от клиента до входного релея (Front latency)
    /// </summary>
    public int FrontLatencyMs { get; set; }

    /// <summary>
    /// Последний зафиксированный клиентом Dota 2 суммарный пинг (включая команду -ping и [Networking])
    /// </summary>
    public int LastReportedTotalPingMs { get; set; }

    /// <summary>
    /// Код входного SDR-релея (например: ams, fra, sto)
    /// </summary>
    public string IngressClusterCode { get; set; } = string.Empty;

    /// <summary>
    /// Код целевого региона сервера матча (например: jnb, dxb, bom, iad, fra)
    /// </summary>
    public string TargetClusterCode { get; set; } = string.Empty;

    /// <summary>
    /// Локализованное название региона фактического игрового сервера
    /// </summary>
    public string TargetClusterDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Человекочитаемый маршрут соединения (например: "AMS ➔ JNB (+160ms)")
    /// </summary>
    public string RouteDescription { get; set; } = string.Empty;
}
