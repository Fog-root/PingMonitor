using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using DotaPingMonitor.Models;
using DotaPingMonitor.ViewModels;

namespace DotaPingMonitor.Services;

public class MtrService
{
    private const int MaxHops = 25;
    private const int ProbesPerHop = 2;
    private const int TimeoutMs = 700;

    public async Task<MtrReport> RunDiagnosticsAsync(
        string targetHost,
        string targetName,
        IProgress<MtrHopResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var report = new MtrReport
        {
            TargetIp = targetHost,
            TargetName = targetName,
            Timestamp = DateTime.Now
        };

        IPAddress targetIp;
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(targetHost, cancellationToken);
            targetIp = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                       ?? addresses.First();
            report.TargetIp = targetIp.ToString();
        }
        catch
        {
            if (!IPAddress.TryParse(targetHost, out targetIp!))
            {
                report.VerdictTitle = LocalizationService.IsRussian ? "✕ Ошибка разрешения адреса" : "✕ Address Resolution Error";
                report.VerdictDescription = LocalizationService.IsRussian
                    ? $"Не удалось получить сетевой адрес для {targetHost}. Проверьте подключение к DNS."
                    : $"Could not resolve network address for {targetHost}. Check DNS connection.";
                report.VerdictIcon = "✕";
                report.VerdictColor = "#FF6161";
                report.VerdictBg = "#18FF6161";
                return report;
            }
        }

        byte[] buffer = Encoding.ASCII.GetBytes("DotaPingMonitorMtrPayloadProbe12");

        for (int ttl = 1; ttl <= MaxHops; ttl++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var hop = await ProbeHopAsync(targetIp, targetName, ttl, buffer, cancellationToken);
            report.Hops.Add(hop);
            progress?.Report(hop);

            if (hop.Category == HopNodeCategory.Destination || hop.IpAddress == targetIp.ToString())
            {
                break;
            }
        }

        ComputeVerdict(report);
        return report;
    }

    private static async Task<MtrHopResult> ProbeHopAsync(
        IPAddress targetIp,
        string targetName,
        int ttl,
        byte[] buffer,
        CancellationToken ct)
    {
        var hop = new MtrHopResult
        {
            HopIndex = ttl,
            SentProbes = ProbesPerHop
        };

        var pings = new List<long>();
        string detectedIp = "*";
        bool reachedDestination = false;

        for (int probe = 0; probe < ProbesPerHop; probe++)
        {
            if (ct.IsCancellationRequested)
                break;

            using var ping = new Ping();
            var options = new PingOptions(ttl, true);
            var sw = Stopwatch.StartNew();

            try
            {
                var reply = await ping.SendPingAsync(targetIp, TimeoutMs, buffer, options);
                sw.Stop();

                if (reply.Status == IPStatus.Success)
                {
                    reachedDestination = true;
                    detectedIp = reply.Address.ToString();
                    long ms = reply.RoundtripTime > 0 ? reply.RoundtripTime : Math.Max(1, (long)Math.Round(sw.Elapsed.TotalMilliseconds));
                    pings.Add(ms);
                    hop.ReceivedProbes++;
                }
                else if (reply.Status == IPStatus.TtlExpired || reply.Status == IPStatus.TimeExceeded)
                {
                    if (reply.Address != null)
                    {
                        detectedIp = reply.Address.ToString();
                    }
                    long ms = Math.Max(1, (long)Math.Round(sw.Elapsed.TotalMilliseconds));
                    pings.Add(ms);
                    hop.ReceivedProbes++;
                }
            }
            catch
            {
                // Таймаут зонда
            }

            if (probe < ProbesPerHop - 1)
            {
                await Task.Delay(80, ct);
            }
        }

        hop.IpAddress = detectedIp;
        hop.LossPercent = hop.SentProbes > 0
            ? ((hop.SentProbes - hop.ReceivedProbes) / (double)hop.SentProbes) * 100.0
            : 0.0;

        if (pings.Count > 0)
        {
            hop.MinPingMs = pings.Min();
            hop.MaxPingMs = pings.Max();
            hop.AvgPingMs = (long)Math.Round(pings.Average());
            hop.PingDisplay = $"{hop.AvgPingMs} ms";
        }
        else
        {
            hop.PingDisplay = LocalizationService.IsRussian ? "* (таймаут)" : "* (timeout)";
        }

        ClassifyNode(hop, ttl, targetIp, targetName, reachedDestination);
        FormatHopStatus(hop);

        if (detectedIp != "*")
        {
            hop.Hostname = await ResolveHostnameAsync(detectedIp);
        }

        return hop;
    }

    public static void RefreshHopLocalization(MtrHopResult hop, string targetName)
    {
        if (hop.AvgPingMs <= 0 && hop.ReceivedProbes == 0)
        {
            hop.PingDisplay = LocalizationService.IsRussian ? "* (таймаут)" : "* (timeout)";
        }

        switch (hop.Category)
        {
            case HopNodeCategory.LocalRouter:
                hop.CategoryName = LocalizationService.IsRussian ? "Домашний Wi-Fi / роутер" : "Home Wi-Fi / Router";
                break;
            case HopNodeCategory.IspGateway:
                hop.CategoryName = LocalizationService.IsRussian ? "Городской провайдер" : "ISP Gateway";
                break;
            case HopNodeCategory.Transit:
                hop.CategoryName = LocalizationService.IsRussian ? "Магистральный канал" : "Backbone Link";
                break;
            case HopNodeCategory.Destination:
                bool isValve = targetName.Contains("Valve", StringComparison.OrdinalIgnoreCase) || targetName.Contains("Dota", StringComparison.OrdinalIgnoreCase);
                hop.CategoryName = isValve
                    ? (LocalizationService.IsRussian ? "Сервер Valve SDR" : "Valve SDR Server")
                    : (LocalizationService.IsRussian ? "Целевой сервер" : "Target Server");
                break;
        }

        FormatHopStatus(hop);
    }

    private static void ClassifyNode(
        MtrHopResult hop,
        int ttl,
        IPAddress targetIp,
        string targetName,
        bool reachedDestination)
    {
        bool isTarget = reachedDestination || hop.IpAddress == targetIp.ToString();

        if (isTarget)
        {
            hop.Category = HopNodeCategory.Destination;
            bool isValve = targetName.Contains("Valve", StringComparison.OrdinalIgnoreCase) || targetName.Contains("Dota", StringComparison.OrdinalIgnoreCase);
            hop.CategoryName = isValve
                ? (LocalizationService.IsRussian ? "Сервер Valve SDR" : "Valve SDR Server")
                : (LocalizationService.IsRussian ? "Целевой сервер" : "Target Server");
            hop.CategoryIcon = "🎮";
            hop.CategoryColor = "#59D499";
            hop.CategoryBg = "#1859D499";
            return;
        }

        bool isPrivate = IPAddress.TryParse(hop.IpAddress, out var parsedIp) && IsPrivateIp(parsedIp);

        // Только 1-й хоп является домашним роутером пользователя
        if (ttl == 1)
        {
            hop.Category = HopNodeCategory.LocalRouter;
            hop.CategoryName = LocalizationService.IsRussian ? "Домашний Wi-Fi / роутер" : "Home Wi-Fi / Router";
            hop.CategoryIcon = "🏠";
            hop.CategoryColor = "#57C1FF";
            hop.CategoryBg = "#1857C1FF";
        }
        // Хопы 2-3 или последующие серые подсети (CGNAT 10.x / 100.64.x) — это шлюзы провайдера
        else if (ttl <= 3 || isPrivate)
        {
            hop.Category = HopNodeCategory.IspGateway;
            hop.CategoryName = LocalizationService.IsRussian ? "Городской провайдер" : "ISP Gateway";
            hop.CategoryIcon = "🏢";
            hop.CategoryColor = "#A0A8B4";
            hop.CategoryBg = "#18A0A8B4";
        }
        else
        {
            hop.Category = HopNodeCategory.Transit;
            hop.CategoryName = LocalizationService.IsRussian ? "Магистральный канал" : "Backbone Link";
            hop.CategoryIcon = "🌐";
            hop.CategoryColor = "#FFC533";
            hop.CategoryBg = "#18FFC533";
        }
    }

    private static void FormatHopStatus(MtrHopResult hop)
    {
        if (hop.LossPercent == 0)
        {
            hop.LossDisplay = "0%";
            hop.LossColor = "#59D499";
            hop.LossBg = "#1859D499";

            if (hop.AvgPingMs > 150)
            {
                hop.StatusText = LocalizationService.IsRussian ? "⚠ Высокий пинг" : "⚠ High Ping";
                hop.StatusColor = "#FFC533";
                hop.StatusBg = "#18FFC533";
            }
            else
            {
                hop.StatusText = LocalizationService.IsRussian ? "✓ Стабильно" : "✓ Stable";
                hop.StatusColor = "#59D499";
                hop.StatusBg = "#1859D499";
            }
        }
        else if (hop.LossPercent < 100)
        {
            hop.LossDisplay = $"{hop.LossPercent:F0}%";
            hop.LossColor = "#FFC533";
            hop.LossBg = "#18FFC533";
            hop.StatusText = LocalizationService.IsRussian ? "⚠ Потери пакетов" : "⚠ Packet Loss";
            hop.StatusColor = "#FFC533";
            hop.StatusBg = "#18FFC533";
        }
        else
        {
            hop.LossDisplay = "100%";
            hop.LossColor = "#FF6161";
            hop.LossBg = "#18FF6161";
            hop.StatusText = LocalizationService.IsRussian ? "✕ Нет ответа (ICMP drop)" : "✕ No Response (ICMP drop)";
            hop.StatusColor = "#FF6161";
            hop.StatusBg = "#18FF6161";
        }
    }

    public static void ComputeVerdict(MtrReport report)
    {
        if (report.Hops.Count == 0)
        {
            report.VerdictTitle = LocalizationService.IsRussian ? "✕ Маршрут не найден" : "✕ Route Not Found";
            report.VerdictDescription = LocalizationService.IsRussian
                ? "Не удалось связаться ни с одним узлом на пути следования пакетов."
                : "Could not communicate with any node along the packet path.";
            report.VerdictIcon = "✕";
            report.VerdictColor = "#FF6161";
            report.VerdictBg = "#18FF6161";
            return;
        }

        var hop1 = report.Hops.FirstOrDefault(h => h.HopIndex == 1);

        // 1. Проблема с роутером ТОЛЬКО если реально теряются пакеты (Loss > 0)
        // или если на первом узле аномальный лаг (>150ms) и последующие узлы тоже страдают
        bool routerLoss = hop1 != null && hop1.LossPercent > 0;
        bool routerHeavyLag = hop1 != null && hop1.AvgPingMs > 150 && report.Hops.Count > 1 && report.Hops.All(h => h.AvgPingMs > 150);

        if (routerLoss || routerHeavyLag)
        {
            hop1!.IsFailureNode = true;
            report.VerdictTitle = LocalizationService.IsRussian
                ? "⚠ Источник проблемы: Домашний Wi-Fi / роутер"
                : "⚠ Problem Source: Home Wi-Fi / Router";
            report.VerdictDescription = routerLoss
                ? (LocalizationService.IsRussian
                    ? $"На первом узле ({hop1.IpAddress}) зафиксированы потери пакетов {hop1.LossPercent:F0}%. Проверьте кабель Ethernet, загрузку Wi-Fi или перезагрузите роутер."
                    : $"Packet loss of {hop1.LossPercent:F0}% detected on the first node ({hop1.IpAddress}). Check Ethernet cable, Wi-Fi load or reboot your router.")
                : (LocalizationService.IsRussian
                    ? $"На первом узле ({hop1.IpAddress}) критическая задержка {hop1.AvgPingMs} ms. Рекомендуется перезагрузить роутер."
                    : $"Critical latency of {hop1.AvgPingMs} ms on the first node ({hop1.IpAddress}). Router restart recommended.");
            report.VerdictIcon = "🏠";
            report.VerdictColor = "#FF6161";
            report.VerdictBg = "#18FF6161";
            report.ProblemNodeText = $"Hop 1: {hop1.IpAddress}";
            return;
        }

        // 2. Проверяем потери на узлах провайдера
        var ispHop = report.Hops.FirstOrDefault(h =>
            h.Category == HopNodeCategory.IspGateway &&
            h.LossPercent > 0 && h.LossPercent < 100 &&
            h.IpAddress != "*");

        if (ispHop != null)
        {
            ispHop.IsFailureNode = true;
            report.VerdictTitle = LocalizationService.IsRussian
                ? "⚠ Источник проблемы: Городской интернет-провайдер"
                : "⚠ Problem Source: Internet Service Provider (ISP)";
            report.VerdictDescription = LocalizationService.IsRussian
                ? $"Домашний роутер в норме, но зафиксированы потери ({ispHop.LossPercent:F0}%) на оборудовании провайдера ({ispHop.IpAddress} {(string.IsNullOrEmpty(ispHop.Hostname) ? "" : $"[{ispHop.Hostname}]")}). Проблема на линии связи вашего оператора."
                : $"Home router is OK, but packet loss ({ispHop.LossPercent:F0}%) detected on ISP equipment ({ispHop.IpAddress} {(string.IsNullOrEmpty(ispHop.Hostname) ? "" : $"[{ispHop.Hostname}]")}). Problem on provider network line.";
            report.VerdictIcon = "🏢";
            report.VerdictColor = "#FF6161";
            report.VerdictBg = "#18FF6161";
            report.ProblemNodeText = $"Hop {ispHop.HopIndex}: {ispHop.IpAddress}";
            return;
        }

        // 3. Проверяем потери на магистральных стыках
        var transitHop = report.Hops.FirstOrDefault(h =>
            h.Category == HopNodeCategory.Transit &&
            h.LossPercent > 0 && h.LossPercent < 100 &&
            h.IpAddress != "*");

        if (transitHop != null)
        {
            transitHop.IsFailureNode = true;
            report.VerdictTitle = LocalizationService.IsRussian
                ? "⚠ Источник проблемы: Магистральный транзитный канал"
                : "⚠ Problem Source: Backbone Transit Link";
            report.VerdictDescription = LocalizationService.IsRussian
                ? $"Домашний роутер и городской провайдер исправны. Потери ({transitHop.LossPercent:F0}%) возникли на международном магистральном стыке ({transitHop.IpAddress})."
                : $"Home router and ISP are fine. Packet loss ({transitHop.LossPercent:F0}%) occurred on international backbone transit link ({transitHop.IpAddress}).";
            report.VerdictIcon = "🌐";
            report.VerdictColor = "#FFC533";
            report.VerdictBg = "#18FFC533";
            report.ProblemNodeText = $"Hop {transitHop.HopIndex}: {transitHop.IpAddress}";
            return;
        }

        // 4. Все узлы стабильны (потерь нет)
        var lastRespondingHop = report.Hops.LastOrDefault(h => h.AvgPingMs > 0);
        long endPing = lastRespondingHop?.AvgPingMs ?? (hop1?.AvgPingMs ?? 0);

        string localizedTargetName = PingTargetItemViewModel.GetLocalizedName(report.TargetName);
        report.VerdictTitle = LocalizationService.IsRussian
            ? "✓ Все узлы стабильны (потерь нет)"
            : "✓ All nodes stable (no packet loss)";
        report.VerdictDescription = LocalizationService.IsRussian
            ? $"Маршрут от вашего компьютера до {localizedTargetName} ({endPing} ms) полностью стабилен. Домашняя сеть, провайдер и магистрали работают без сбоев."
            : $"Route from your PC to {localizedTargetName} ({endPing} ms) is completely stable. Home network, ISP, and backbones are operating normally.";
        report.VerdictIcon = "✓";
        report.VerdictColor = "#59D499";
        report.VerdictBg = "#1859D499";
    }

    public static string FormatReportText(MtrReport report)
    {
        var sb = new StringBuilder();
        string localizedTargetName = PingTargetItemViewModel.GetLocalizedName(report.TargetName);

        if (LocalizationService.IsRussian)
        {
            sb.AppendLine("=== PING MONITORING: MTR ДИАГНОСТИКА МАРШРУТА ===");
            sb.AppendLine($"Цель: {localizedTargetName} ({report.TargetIp})");
            sb.AppendLine($"Дата и время: {report.Timestamp:dd.MM.yyyy HH:mm:ss}");
            sb.AppendLine($"Вердикт: {report.VerdictTitle}");
            sb.AppendLine($"Пояснение: {report.VerdictDescription}");
            sb.AppendLine();
            sb.AppendLine(string.Format("{0,-4} | {1,-26} | {2,-16} | {3,-10} | {4,-8} | {5}",
                "ХОП", "КАТЕГОРИЯ УЗЛА", "IP АДРЕС", "ОТКЛИК", "ПОТЕРИ", "PTR ИМЯ"));
            sb.AppendLine(new string('-', 95));

            foreach (var h in report.Hops)
            {
                sb.AppendLine(string.Format("{0,-4} | {1,-26} | {2,-16} | {3,-10} | {4,-8} | {5}",
                    $"#{h.HopIndex:D2}",
                    h.CategoryName,
                    h.IpAddress,
                    h.PingDisplay,
                    h.LossDisplay,
                    h.Hostname));
            }

            sb.AppendLine(new string('-', 95));
            sb.AppendLine("Сформировано DotaPingMonitor");
        }
        else
        {
            sb.AppendLine("=== PING MONITORING: MTR ROUTE DIAGNOSTICS ===");
            sb.AppendLine($"Target: {localizedTargetName} ({report.TargetIp})");
            sb.AppendLine($"Date & Time: {report.Timestamp:dd.MM.yyyy HH:mm:ss}");
            sb.AppendLine($"Verdict: {report.VerdictTitle}");
            sb.AppendLine($"Explanation: {report.VerdictDescription}");
            sb.AppendLine();
            sb.AppendLine(string.Format("{0,-4} | {1,-26} | {2,-16} | {3,-10} | {4,-8} | {5}",
                "HOP", "NODE CATEGORY", "IP ADDRESS", "PING", "LOSS", "PTR NAME"));
            sb.AppendLine(new string('-', 95));

            foreach (var h in report.Hops)
            {
                sb.AppendLine(string.Format("{0,-4} | {1,-26} | {2,-16} | {3,-10} | {4,-8} | {5}",
                    $"#{h.HopIndex:D2}",
                    h.CategoryName,
                    h.IpAddress,
                    h.PingDisplay,
                    h.LossDisplay,
                    h.Hostname));
            }

            sb.AppendLine(new string('-', 95));
            sb.AppendLine("Generated by DotaPingMonitor");
        }

        return sb.ToString();
    }

    private static bool IsPrivateIp(IPAddress ip)
    {
        byte[] bytes = ip.GetAddressBytes();
        if (bytes.Length != 4) return false;
        if (bytes[0] == 10) return true;
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
        if (bytes[0] == 192 && bytes[1] == 168) return true;
        if (bytes[0] == 100 && (bytes[1] & 0xC0) == 64) return true;
        if (bytes[0] == 127) return true;
        return false;
    }

    private static async Task<string> ResolveHostnameAsync(string ipStr)
    {
        if (string.IsNullOrEmpty(ipStr) || ipStr == "*") return string.Empty;
        try
        {
            var hostTask = Dns.GetHostEntryAsync(ipStr);
            if (await Task.WhenAny(hostTask, Task.Delay(350)) == hostTask)
            {
                var entry = await hostTask;
                return entry.HostName ?? string.Empty;
            }
        }
        catch { }
        return string.Empty;
    }
}
