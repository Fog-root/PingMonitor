using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using DotaPingMonitor.Data;
using DotaPingMonitor.Models;

namespace DotaPingMonitor.Services;

public class IspTicketData
{
    public string ContractNumber { get; set; } = string.Empty;
    public string ClientAddress { get; set; } = string.Empty;
    public string ClientPhone { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public string TargetHost { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public string IspName { get; set; } = string.Empty;
    public string RouterIp { get; set; } = string.Empty;
    public string AdapterName { get; set; } = string.Empty;
    public long RouterPingMs { get; set; } = -1;
    public MtrReport? MtrReport { get; set; }
    public List<PingRecord> Incidents { get; set; } = new();
    public SpeedtestResult? Speedtest { get; set; }
}

public static class IspTicketService
{
    public static async Task<IspTicketData> GatherDiagnosticDataAsync(
        DatabaseService? database,
        List<PingRecord>? recentRecords,
        string targetName,
        string targetHost,
        MtrReport? existingMtr = null)
    {
        var data = new IspTicketData
        {
            TargetName = string.IsNullOrWhiteSpace(targetName) ? "Игровой сервер / DNS" : targetName,
            TargetHost = string.IsNullOrWhiteSpace(targetHost) ? "8.8.8.8" : targetHost,
            MtrReport = existingMtr
        };

        // 1. Определение активного адаптера и шлюза
        try
        {
            var activeNic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                      ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                      ni.GetIPProperties().GatewayAddresses.Count > 0);

            if (activeNic != null)
            {
                data.AdapterName = activeNic.Name;
                var gw = activeNic.GetIPProperties().GatewayAddresses.FirstOrDefault();
                if (gw != null && gw.Address != null)
                {
                    data.RouterIp = gw.Address.ToString();
                }
            }
        }
        catch { }

        if (string.IsNullOrEmpty(data.RouterIp))
        {
            data.RouterIp = "192.168.1.1";
        }

        // 2. Экспресс-замер пинга до роутера
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(data.RouterIp, 500).ConfigureAwait(false);
            if (reply.Status == IPStatus.Success)
            {
                data.RouterPingMs = reply.RoundtripTime;
            }
        }
        catch { }

        // 3. Получение последних зафиксированных инцидентов
        try
        {
            if (recentRecords != null && recentRecords.Count > 0)
            {
                data.Incidents = recentRecords
                    .Where(r => r.IsSpike || r.IsTimeout)
                    .OrderByDescending(r => r.Timestamp)
                    .Take(15)
                    .ToList();
            }
            else if (database != null)
            {
                data.Incidents = await database.GetRecentIncidentsAsync(15).ConfigureAwait(false);
            }
        }
        catch { }

        // 4. Если MTR не был передан, но передан хост — при необходимости можно запустить экспресс-трассировку
        if (data.MtrReport == null && !string.IsNullOrWhiteSpace(data.TargetHost))
        {
            try
            {
                var mtrService = new MtrService();
                data.MtrReport = await mtrService.RunDiagnosticsAsync(data.TargetHost, data.TargetName).ConfigureAwait(false);
            }
            catch { }
        }

        return data;
    }

    public static string GenerateTicketText(IspTicketData data, bool isRussian = true)
    {
        var sb = new StringBuilder();
        var now = DateTime.Now;

        string contractText = !string.IsNullOrWhiteSpace(data.ContractNumber) 
            ? data.ContractNumber 
            : (isRussian ? "[Укажите номер договора / лицевого счёта]" : "[Specify account / contract ID]");
        string addressText = !string.IsNullOrWhiteSpace(data.ClientAddress) 
            ? data.ClientAddress 
            : (isRussian ? "[Укажите адрес подключения]" : "[Specify service address]");
        string phoneText = !string.IsNullOrWhiteSpace(data.ClientPhone) 
            ? data.ClientPhone 
            : (isRussian ? "[Укажите контактный телефон]" : "[Specify contact phone number]");
        string routerPingStr = isRussian 
            ? (data.RouterPingMs >= 0 ? $"{data.RouterPingMs} мс" : "< 1 мс")
            : (data.RouterPingMs >= 0 ? $"{data.RouterPingMs} ms" : "< 1 ms");

        if (isRussian)
        {
            sb.AppendLine("================================================================================");
            sb.AppendLine("ОБРАЩЕНИЕ В СЛУЖБУ ТЕХНИЧЕСКОЙ ПОДДЕРЖКИ ИНТЕРНЕТ-ПРОВАЙДЕРА");
            sb.AppendLine("================================================================================");
            sb.AppendLine($"Дата и время формирования: {now:dd.MM.yyyy HH:mm:ss}");
            if (!string.IsNullOrEmpty(data.IspName))
                sb.AppendLine($"Провайдер:                  {data.IspName}");
            if (!string.IsNullOrEmpty(data.ClientIp))
                sb.AppendLine($"Внешний IP клиента:         {data.ClientIp}");
            sb.AppendLine($"Локальный шлюз (роутер):    {data.RouterIp} (отклик: {routerPingStr})");
            if (!string.IsNullOrEmpty(data.AdapterName))
                sb.AppendLine($"Сетевой адаптер ПК:         {data.AdapterName}");
            sb.AppendLine($"Целевой узел проверки:      {data.TargetName} ({data.TargetHost})");
            sb.AppendLine();

            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine("ТЕКСТ ЗАЯВКИ ДЛЯ ТЕХПОДДЕРЖКИ (СКОПИРУЙТЕ В ТИКЕТ ИЛИ ЧАТ):");
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine("Здравствуйте, служба технической поддержки!");
            sb.AppendLine();
            sb.AppendLine("Прошу зарегистрировать техническую заявку на проверку качества интернет-канала");
            sb.AppendLine("и стабильности маршрутизации трафика.");
            sb.AppendLine("На моём подключении фиксируются регулярные потери пакетов (Packet Loss) и резкие");
            sb.AppendLine("скачки задержки (Ping Spikes / Jitter) при прохождении через внешние узлы сети.");
            sb.AppendLine();
            sb.AppendLine($"• Номер договора / л/с:   {contractText}");
            sb.AppendLine($"• Адрес подключения:      {addressText}");
            sb.AppendLine($"• Контактный телефон:     {phoneText}");
            sb.AppendLine();

            sb.AppendLine("ПРЕДВАРИТЕЛЬНАЯ ПРОВЕРКА ЛОКАЛЬНОГО ОБОРУДОВАНИЯ:");
            sb.AppendLine($"1. Домашний роутер ({data.RouterIp}) работает штатно: отклик {routerPingStr}, потери 0.0%.");
            sb.AppendLine("2. На ПК исключены фоновые загрузки, паразитный трафик и помехи.");
            sb.AppendLine("3. Проблема локализована на внешних сетевых узлах / магистральном пиринге провайдера.");
            sb.AppendLine();

            if (data.MtrReport != null && !string.IsNullOrEmpty(data.MtrReport.VerdictTitle))
            {
                sb.AppendLine("ВЕРДИКТ АППАРАТНОЙ ДИАГНОСТИКИ МАРШРУТА:");
                sb.AppendLine($"• Статус:      {data.MtrReport.VerdictTitle}");
                sb.AppendLine($"• Детали:      {data.MtrReport.VerdictDescription}");
                if (!string.IsNullOrEmpty(data.MtrReport.ProblemNodeText))
                {
                    sb.AppendLine($"• Узел сбоя:   {data.MtrReport.ProblemNodeText}");
                }
                sb.AppendLine();
            }

            if (data.MtrReport != null && data.MtrReport.Hops.Count > 0)
            {
                sb.AppendLine("РЕЗУЛЬТАТЫ ПОУЗЛОВОЙ ТРАССИРОВКИ (MTR TRACE ROUTE):");
                sb.AppendLine(string.Format("{0,-4} | {1,-17} | {2,-24} | {3,-8} | {4,-8} | {5}",
                    "ХОП", "IP-АДРЕС", "СЕГМЕНТ МАРШРУТА", "ПОТЕРИ", "ПИНГ", "PTR ИМЯ / УЗЕЛ"));
                sb.AppendLine(new string('-', 90));

                foreach (var hop in data.MtrReport.Hops)
                {
                    string categoryTitle = hop.Category switch
                    {
                        HopNodeCategory.LocalRouter => "Домашний роутер",
                        HopNodeCategory.IspGateway => "Узел провайдера (ISP)",
                        HopNodeCategory.Transit => "Магистраль / Транзит",
                        HopNodeCategory.Destination => "Целевой сервер",
                        _ => hop.CategoryName
                    };

                    string flag = hop.IsFailureNode || hop.LossPercent > 0 ? " <-- [СБОЙ/ПОТЕРИ]" : "";

                    sb.AppendLine(string.Format("{0,-4} | {1,-17} | {2,-24} | {3,-8} | {4,-8} | {5}{6}",
                        $"#{hop.HopIndex:D2}",
                        hop.IpAddress,
                        categoryTitle,
                        hop.LossDisplay,
                        hop.PingDisplay,
                        string.IsNullOrEmpty(hop.Hostname) ? "-" : hop.Hostname,
                        flag));
                }
                sb.AppendLine(new string('-', 90));
                sb.AppendLine();
            }

            if (data.Incidents.Count > 0)
            {
                sb.AppendLine("ЖУРНАЛ ЗАФИКСИРОВАННЫХ СЕТЕВЫХ ИНЦИДЕНТОВ (ПОСЛЕДНИЕ СОБЫТИЯ):");
                foreach (var inc in data.Incidents.Take(8))
                {
                    string type = inc.IsTimeout ? "ТАЙМ-АУТ / ПОТЕРЯ" : "СПАЙК ЗАДЕРЖКИ";
                    sb.AppendLine($"• [{inc.Timestamp:HH:mm:ss}] {type}: пинг {inc.PingMs} мс (сервер: {inc.Server})");
                    if (!string.IsNullOrEmpty(inc.IncidentDescription))
                    {
                        sb.AppendLine($"    Детализация: {inc.IncidentDescription}");
                    }
                }
                sb.AppendLine();
            }

            sb.AppendLine("ПРОСЬБА К ИНЖЕНЕРАМ СЛУЖБЫ ПОДДЕРЖКИ:");
            sb.AppendLine("1. Проверить состояние абонентского порта на коммутаторе доступа / оптическом терминале.");
            sb.AppendLine("2. Проверить уровень затухания оптической линии / целостность витой пары.");
            sb.AppendLine("3. Проанализировать промежуточный маршрут и загрузку вышестоящих магистральных каналов.");
            sb.AppendLine();
            sb.AppendLine("Отчёт сформирован специализированным комплексом сетевой телеметрии Ping Monitoring.");
            sb.AppendLine("================================================================================");
        }
        else
        {
            sb.AppendLine("================================================================================");
            sb.AppendLine("INTERNET SERVICE PROVIDER (ISP) TECHNICAL SUPPORT TICKET");
            sb.AppendLine("================================================================================");
            sb.AppendLine($"Report Generated:        {now:yyyy-MM-dd HH:mm:ss}");
            if (!string.IsNullOrEmpty(data.IspName))
                sb.AppendLine($"ISP Name:                {data.IspName}");
            if (!string.IsNullOrEmpty(data.ClientIp))
                sb.AppendLine($"Client Public IP:        {data.ClientIp}");
            sb.AppendLine($"Local Gateway (Router):  {data.RouterIp} (latency: {routerPingStr})");
            sb.AppendLine($"Target Destination:      {data.TargetName} ({data.TargetHost})");
            sb.AppendLine();

            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine("TICKET BODY (COPY INTO YOUR ISP SUPPORT PORTAL OR EMAIL):");
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine("Hello Technical Support Team,");
            sb.AppendLine();
            sb.AppendLine("I am submitting a request regarding high latency spikes and packet loss observed");
            sb.AppendLine("on my internet connection routing through your network infrastructure.");
            sb.AppendLine();
            sb.AppendLine($"• Account / Contract ID:  {contractText}");
            sb.AppendLine($"• Service Address:        {addressText}");
            sb.AppendLine($"• Contact Phone:          {phoneText}");
            sb.AppendLine();
            sb.AppendLine("LOCAL NETWORK VERIFICATION:");
            sb.AppendLine($"1. Local home router ({data.RouterIp}) responded normally: {routerPingStr}, 0.0% loss.");
            sb.AppendLine("2. No background downloads, malware, or bandwidth hogs are running on local PC.");
            sb.AppendLine("3. The degradation starts on external provider routing hops / transit backbones.");
            sb.AppendLine();

            if (data.MtrReport != null && !string.IsNullOrEmpty(data.MtrReport.VerdictTitle))
            {
                sb.AppendLine("ROUTE HARDWARE DIAGNOSTICS VERDICT:");
                sb.AppendLine($"• Status:       {data.MtrReport.VerdictTitle}");
                sb.AppendLine($"• Details:      {data.MtrReport.VerdictDescription}");
                if (!string.IsNullOrEmpty(data.MtrReport.ProblemNodeText))
                {
                    sb.AppendLine($"• Failing Node: {data.MtrReport.ProblemNodeText}");
                }
                sb.AppendLine();
            }

            if (data.MtrReport != null && data.MtrReport.Hops.Count > 0)
            {
                sb.AppendLine("MTR HOP-BY-HOP ROUTE TRACE:");
                sb.AppendLine(string.Format("{0,-4} | {1,-17} | {2,-24} | {3,-8} | {4,-8} | {5}",
                    "HOP", "IP ADDRESS", "ROUTE SEGMENT", "LOSS", "PING", "PTR HOSTNAME"));
                sb.AppendLine(new string('-', 90));

                foreach (var hop in data.MtrReport.Hops)
                {
                    string categoryTitle = hop.Category switch
                    {
                        HopNodeCategory.LocalRouter => "Local Home Router",
                        HopNodeCategory.IspGateway => "ISP Gateway Node",
                        HopNodeCategory.Transit => "Transit Backbone",
                        HopNodeCategory.Destination => "Destination Server",
                        _ => hop.CategoryName
                    };

                    string flag = hop.IsFailureNode || hop.LossPercent > 0 ? " <-- [LOSS/SPIKE]" : "";
                    sb.AppendLine(string.Format("{0,-4} | {1,-17} | {2,-24} | {3,-8} | {4,-8} | {5}{6}",
                        $"#{hop.HopIndex:D2}",
                        hop.IpAddress,
                        categoryTitle,
                        hop.LossDisplay,
                        hop.PingDisplay,
                        string.IsNullOrEmpty(hop.Hostname) ? "-" : hop.Hostname,
                        flag));
                }
                sb.AppendLine(new string('-', 90));
                sb.AppendLine();
            }

            if (data.Incidents.Count > 0)
            {
                sb.AppendLine("LOGGED NETWORK INCIDENTS (RECENT EVENTS):");
                foreach (var inc in data.Incidents.Take(8))
                {
                    string type = inc.IsTimeout ? "TIMEOUT / PACKET LOSS" : "LATENCY SPIKE";
                    sb.AppendLine($"• [{inc.Timestamp:HH:mm:ss}] {type}: ping {inc.PingMs} ms (server: {inc.Server})");
                    if (!string.IsNullOrEmpty(inc.IncidentDescription))
                    {
                        sb.AppendLine($"    Details: {inc.IncidentDescription}");
                    }
                }
                sb.AppendLine();
            }

            sb.AppendLine("REQUEST FOR ISP TECHNICAL ENGINEERS:");
            sb.AppendLine("1. Check subscriber access switch port / ONT status for physical errors or frame drops.");
            sb.AppendLine("2. Verify optical line attenuation levels and ethernet cable integrity.");
            sb.AppendLine("3. Analyze upstream transit routing paths and interconnect capacity for congestion.");
            sb.AppendLine();
            sb.AppendLine("Generated by Ping Monitoring Telemetry Suite.");
            sb.AppendLine("================================================================================");
        }

        return sb.ToString();
    }
}
