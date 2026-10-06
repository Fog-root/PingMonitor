using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace DotaPingMonitor.Services;

public class NetworkDiagnosticResult
{
    public long RouterPingMs { get; set; } = -1;
    public long IspPingMs { get; set; } = -1;
    public long TargetPingMs { get; set; } = -1;
    public string RouterIp { get; set; } = "192.168.0.1";
    public string IspIp { get; set; } = "Шлюз провайдера";
    public string Category { get; set; } = "None"; // "Router", "Isp", "Backbone", "None"
    public string VerdictTitle { get; set; } = "✓ Соединение стабильное";
    public string VerdictDescription { get; set; } = string.Empty;
    public string VerdictIcon { get; set; } = "✓";
    public string VerdictColor { get; set; } = "#59D499";
    public string VerdictBg { get; set; } = "#1859D499";
    public string Recommendation { get; set; } = string.Empty;
}

public class NetworkDiagnosticService
{
    private string _cachedRouterIp = string.Empty;
    private string _cachedIspIp = string.Empty;
    private DateTime _lastGatewayCheck = DateTime.MinValue;
    private long _lastCachedRouterPing = -1;
    private DateTime _lastRouterPingTime = DateTime.MinValue;

    public string RouterIp => !string.IsNullOrEmpty(_cachedRouterIp) ? _cachedRouterIp : "192.168.0.1";
    public string IspIp => !string.IsNullOrEmpty(_cachedIspIp) ? _cachedIspIp : "Шлюз провайдера";

    public NetworkDiagnosticService()
    {
        Task.Run(() => DetectGatewaysAsync("8.8.8.8"));
    }

    public async Task DetectGatewaysAsync(string sampleTargetIp = "8.8.8.8")
    {
        try
        {
            // 1. Поиск локального физического шлюза (домашнего роутера)
            var gateway = GetPhysicalDefaultGateway();
            if (gateway != null)
            {
                _cachedRouterIp = gateway.ToString();
            }

            // 2. Поиск шлюза провайдера через TTL = 2
            if (string.IsNullOrEmpty(_cachedIspIp) || _cachedIspIp == "Шлюз провайдера")
            {
                string ispHop = await DiscoverIspGatewayAsync(sampleTargetIp);
                if (!string.IsNullOrEmpty(ispHop))
                {
                    _cachedIspIp = ispHop;
                }
            }

            _lastGatewayCheck = DateTime.Now;
        }
        catch
        {
            // Fallback default
            if (string.IsNullOrEmpty(_cachedRouterIp)) _cachedRouterIp = "192.168.0.1";
        }
    }

    private static IPAddress? GetPhysicalDefaultGateway()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                            !n.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase) &&
                            !n.Description.Contains("TAP", StringComparison.OrdinalIgnoreCase) &&
                            !n.Description.Contains("Radmin", StringComparison.OrdinalIgnoreCase) &&
                            !n.Name.Contains("VPN", StringComparison.OrdinalIgnoreCase) &&
                            !n.Name.Contains("Radmin", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var iface in interfaces)
            {
                var props = iface.GetIPProperties();
                var gw = props.GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork &&
                                         !g.Address.Equals(IPAddress.Any) &&
                                         g.Address.ToString() != "0.0.0.0");
                if (gw != null)
                {
                    return gw.Address;
                }
            }

            // Запасной вариант: любой IPv4 шлюз кроме 0.0.0.0
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().GatewayAddresses)
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any) && a.ToString() != "0.0.0.0");
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> DiscoverIspGatewayAsync(string targetIpStr)
    {
        try
        {
            if (!IPAddress.TryParse(targetIpStr, out var targetIp))
            {
                var addrs = await Dns.GetHostAddressesAsync(targetIpStr);
                targetIp = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addrs.First();
            }

            using var ping = new Ping();
            var opt = new PingOptions(2, true);
            var buf = Encoding.ASCII.GetBytes("probe");
            var reply = await ping.SendPingAsync(targetIp, 800, buf, opt);

            if (reply.Status == IPStatus.TtlExpired || reply.Status == IPStatus.TimeExceeded)
            {
                if (reply.Address != null)
                {
                    return reply.Address.ToString();
                }
            }
        }
        catch { }

        return string.Empty;
    }

    public async Task<long> PingIpAsync(string ipStr, int timeoutMs = 150)
    {
        if (string.IsNullOrEmpty(ipStr) || ipStr == "Шлюз провайдера")
            return -1;

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ipStr, timeoutMs);

            if (reply.Status == IPStatus.Success)
            {
                return Math.Max(1, reply.RoundtripTime);
            }
        }
        catch { }

        return -1;
    }

    public async Task<(long RouterPingMs, long IspPingMs, string Category, string Description)> DiagnoseTickAsync(
        string targetIp,
        long targetPingMs,
        bool isTimeout,
        bool isSpike)
    {
        if (DateTime.Now - _lastGatewayCheck > TimeSpan.FromMinutes(10))
        {
            _ = Task.Run(() => DetectGatewaysAsync(targetIp));
        }

        string rIp = RouterIp;
        bool isTrouble = isSpike || isTimeout || targetPingMs > 100;
        bool needRouterPing = isTrouble || (DateTime.Now - _lastRouterPingTime > TimeSpan.FromSeconds(5)) || _lastCachedRouterPing < 0;

        Task<long>? rTask = null;
        if (needRouterPing)
        {
            rTask = PingIpAsync(rIp, 120);
        }

        Task<long>? ispTask = null;
        if (isTrouble)
        {
            if (!string.IsNullOrEmpty(_cachedIspIp) && _cachedIspIp != "Шлюз провайдера")
            {
                ispTask = PingIpAsync(_cachedIspIp, 150);
            }
            else
            {
                // Поиск шлюза провайдера в фоне, чтобы не тормозить текущий замер
                _ = Task.Run(async () =>
                {
                    var ip = await DiscoverIspGatewayAsync(targetIp);
                    if (!string.IsNullOrEmpty(ip)) _cachedIspIp = ip;
                });
            }
        }

        long rPing = _lastCachedRouterPing;
        if (rTask != null)
        {
            rPing = await rTask;
            if (rPing >= 0 || !isTrouble)
            {
                _lastCachedRouterPing = rPing;
                _lastRouterPingTime = DateTime.Now;
            }
        }

        long ispPing = -1;
        if (ispTask != null)
        {
            ispPing = await ispTask;
        }

        var result = AnalyzeIncident(targetPingMs, rPing, ispPing, isTimeout, isSpike);
        return (rPing, ispPing, result.Category, result.VerdictDescription);
    }

    public NetworkDiagnosticResult AnalyzeIncident(
        long targetPingMs,
        long routerPingMs,
        long ispPingMs,
        bool isTimeout,
        bool isSpike)
    {
        bool isRu = LocalizationService.IsRussian;
        var res = new NetworkDiagnosticResult
        {
            TargetPingMs = targetPingMs,
            RouterPingMs = routerPingMs,
            IspPingMs = ispPingMs,
            RouterIp = RouterIp,
            IspIp = !string.IsNullOrEmpty(_cachedIspIp) ? _cachedIspIp : (isRu ? "Шлюз провайдера" : "ISP gateway")
        };

        // 1. ПРОВЕРКА РОУТЕРА / WI-FI
        // Пороги: >100 ms — желтый (повышенный), >200 ms — красный (высокий)
        if (routerPingMs > 100 || (routerPingMs <= 0 && isTimeout))
        {
            res.Category = "Router";
            res.VerdictIcon = "🏠";

            if (routerPingMs > 200)
            {
                res.VerdictTitle = isRu 
                    ? "⚠ Высокая задержка в домашнем Wi-Fi / роутере (>200 ms)"
                    : "⚠ High latency on home Wi-Fi / router (>200 ms)";
                res.VerdictColor = "#FF6161";
                res.VerdictBg = "#18FF6161";
                res.VerdictDescription = isRu
                    ? $"Задержка до домашнего роутера ({res.RouterIp}) подскочила до {routerPingMs} ms (критический уровень >200 ms). Это вызвало всплеск общего пинга до игрового сервера."
                    : $"Latency to home router ({res.RouterIp}) jumped to {routerPingMs} ms (critical level >200 ms). This caused a spike in overall ping to the game server.";
                res.Recommendation = isRu
                    ? "Рекомендации: подключитесь по кабелю Ethernet вместо Wi-Fi, переключитесь на диапазон 5 GHz, проверьте, не качают ли другие устройства торренты или обновления, или перезагрузите роутер."
                    : "Recommendations: connect via Ethernet instead of Wi-Fi, switch to 5 GHz Wi-Fi band, check if other devices are downloading torrents or updates, or reboot the router.";
            }
            else if (routerPingMs > 100)
            {
                res.VerdictTitle = isRu 
                    ? "⚠ Повышенная задержка роутера (>100 ms)"
                    : "⚠ Elevated router latency (>100 ms)";
                res.VerdictColor = "#FFC533";
                res.VerdictBg = "#18FFC533";
                res.VerdictDescription = isRu
                    ? $"Задержка до домашнего роутера ({res.RouterIp}) составила {routerPingMs} ms (порог >100 ms). Возможны временные помехи Wi-Fi или локальная нагрузка на сеть."
                    : $"Latency to home router ({res.RouterIp}) reached {routerPingMs} ms (threshold >100 ms). Possible temporary Wi-Fi interference or local network load.";
                res.Recommendation = isRu
                    ? "Рекомендации: проверьте качество сигнала Wi-Fi или активность других устройств в домашней сети."
                    : "Recommendations: check Wi-Fi signal strength or network activity of other local devices.";
            }
            else
            {
                res.VerdictTitle = isRu 
                    ? "⚠ Роутер не отвечает (обрыв локального соединения)"
                    : "⚠ Router not responding (local connection drop)";
                res.VerdictColor = "#FF6161";
                res.VerdictBg = "#18FF6161";
                res.VerdictDescription = isRu
                    ? $"Домашний роутер ({res.RouterIp}) перестал отвечать на сетевые запросы. Произошёл полный обрыв локального соединения."
                    : $"Home router ({res.RouterIp}) stopped responding to network requests. Local connection is down.";
                res.Recommendation = isRu
                    ? "Рекомендации: проверьте физическое подключение сетевого кабеля/адаптера или перезагрузите роутер по питанию."
                    : "Recommendations: check physical ethernet cable/adapter connection or power cycle your router.";
            }

            return res;
        }

        // 2. ПРОВЕРКА ГОРОДСКОГО ПРОВАЙДЕРА
        // Если роутер стабилен (<= 100 ms), но шлюз провайдера тормозит (> 100 ms) или не отвечает
        if (ispPingMs > 100 || (ispPingMs <= 0 && isTimeout && routerPingMs > 0 && routerPingMs <= 100))
        {
            res.Category = "Isp";
            res.VerdictIcon = "🏢";

            if (ispPingMs > 200)
            {
                res.VerdictTitle = isRu 
                    ? "⚠ Критическая задержка на узле интернет-провайдера (>200 ms)"
                    : "⚠ Critical latency on ISP gateway node (>200 ms)";
                res.VerdictColor = "#FF6161";
                res.VerdictBg = "#18FF6161";
                res.VerdictDescription = isRu
                    ? $"Домашний роутер ответил стабильно ({routerPingMs} ms), однако шлюз провайдера ({res.IspIp}) ответил с задержкой {ispPingMs} ms. Проблема локализована на первой миле вашего интернет-оператора."
                    : $"Home router responded normally ({routerPingMs} ms), but ISP gateway ({res.IspIp}) latency reached {ispPingMs} ms. Issue is localized on your provider's first mile.";
            }
            else if (ispPingMs > 100)
            {
                res.VerdictTitle = isRu 
                    ? "⚠ Повышенный пинг на узле провайдера (>100 ms)"
                    : "⚠ Elevated ping on ISP gateway node (>100 ms)";
                res.VerdictColor = "#FFC533";
                res.VerdictBg = "#18FFC533";
                res.VerdictDescription = isRu
                    ? $"Домашний роутер работает в норме ({routerPingMs} ms), но шлюз оператора ({res.IspIp}) отвечает с задержкой {ispPingMs} ms."
                    : $"Home router is operating normally ({routerPingMs} ms), but ISP gateway ({res.IspIp}) responded with latency {ispPingMs} ms.";
            }
            else
            {
                res.VerdictTitle = isRu 
                    ? "⚠ Потери пакетов у провайдера"
                    : "⚠ Packet loss at ISP";
                res.VerdictColor = "#FF6161";
                res.VerdictBg = "#18FF6161";
                res.VerdictDescription = isRu
                    ? $"Домашний роутер полностью исправен ({routerPingMs} ms), но внешний шлюз интернет-провайдера ({res.IspIp}) потерял пакеты."
                    : $"Home router is operating normally ({routerPingMs} ms), but ISP gateway ({res.IspIp}) dropped packets.";
            }

            res.Recommendation = isRu
                ? "Рекомендации: домашняя сеть работает отлично. Сбой находится в сети вашего провайдера (перегрузка оборудования, авария на линии). При повторении обратитесь в техподдержку оператора."
                : "Recommendations: home network is working properly. The issue is in your provider's network (equipment overload, line outage). If it persists, contact ISP support.";
            return res;
        }

        // 3. ПРОВЕРКА МАГИСТРАЛЬНОГО ТРАНЗИТА / СЕРВЕРА
        // Если и роутер и провайдер в норме, но общий пинг высокий (> 100 ms) или таймаут
        if (isSpike || isTimeout || targetPingMs > 100)
        {
            res.Category = "Backbone";
            res.VerdictTitle = isRu 
                ? "⚠ Проблема на магистрали / игровом сервере"
                : "⚠ Transit backbone / game server issue";
            res.VerdictIcon = "🌐";
            res.VerdictColor = "#FFC533";
            res.VerdictBg = "#18FFC533";

            res.VerdictDescription = isRu
                ? $"Ваш роутер ({routerPingMs} ms) и городской провайдер работают отлично. Задержка или потеря возникли на международном транзитном магистральном стыке по пути к игровому кластеру Valve."
                : $"Your home router ({routerPingMs} ms) and ISP are working properly. Latency or packet loss occurred on international transit backbone link on the way to the game cluster.";
            res.Recommendation = isRu
                ? "Рекомендации: ваш домашний интернет и провайдер работают штатно. Проблема вызвана загрузкой зарубежных транзитных каналов или самого сервера. Попробуйте выбрать другой регион SDR в таблице слева."
                : "Recommendations: your local network and ISP are fine. The issue is caused by transit provider congestion or the game server itself. Try selecting another SDR cluster in the list.";
            return res;
        }

        // 4. СЕТЬ СТАБИЛЬНА
        res.Category = "None";
        res.VerdictTitle = isRu 
            ? "✓ Сеть работает стабильно"
            : "✓ Network connection stable";
        res.VerdictIcon = "✓";
        res.VerdictColor = "#59D499";
        res.VerdictBg = "#1859D499";
        res.VerdictDescription = isRu
            ? $"Все ключевые узлы маршрута (домашний роутер: {Math.Max(1, routerPingMs)} ms, сеть оператора, транзитные магистрали) работают в идеальном диапазоне."
            : $"All key route hops (home router: {Math.Max(1, routerPingMs)} ms, ISP gateway, transit backbones) are operating within the ideal range.";
        res.Recommendation = isRu
            ? "Никаких действий не требуется. Качество связи для игры оптимальное."
            : "No action required. Connection quality is optimal for gaming.";

        return res;
    }

    public async Task<NetworkDiagnosticResult> PerformLiveProbeAsync(string targetHost, long historicalPingMs, bool isHistoricalTimeout)
    {
        string rIp = RouterIp;
        long liveRouterPing = await PingIpAsync(rIp, 400);

        if (string.IsNullOrEmpty(_cachedIspIp) || _cachedIspIp == "Шлюз провайдера")
        {
            _cachedIspIp = await DiscoverIspGatewayAsync(targetHost);
        }

        long liveIspPing = !string.IsNullOrEmpty(_cachedIspIp) ? await PingIpAsync(_cachedIspIp, 500) : -1;
        long liveTargetPing = await PingIpAsync(targetHost, 1000);

        // Используем данные исторического момента для вердикта, но дополняем текущими замерами
        long pingToUse = historicalPingMs > 0 ? historicalPingMs : liveTargetPing;
        bool timeoutToUse = isHistoricalTimeout;
        bool spikeToUse = pingToUse > 100;

        var result = AnalyzeIncident(pingToUse, liveRouterPing, liveIspPing, timeoutToUse, spikeToUse);
        result.TargetPingMs = pingToUse;
        result.RouterPingMs = liveRouterPing;
        result.IspPingMs = liveIspPing;

        return result;
    }
}
