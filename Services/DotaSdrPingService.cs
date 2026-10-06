using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace DotaPingMonitor.Services;

public class SdrPingResult
{
    public string Code { get; set; } = string.Empty;

    public long PingMs { get; set; }

    public string Route { get; set; } = string.Empty;

    public string RegionName { get; set; } = string.Empty;
}

public class ValvePop
{
    public string Code { get; set; } = string.Empty;

    public string Desc { get; set; } = string.Empty;

    public List<string> Relays { get; set; } = new();
}

public class DotaSdrPingService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private int _currentAppId = 570;
    private string _currentDefaultRoute = "Прямой (Direct)";

    public int CurrentAppId => _currentAppId;

    private string SdrConfigUrl => $"https://api.steampowered.com/ISteamApps/GetSDRConfig/v1?appid={_currentAppId}";

    public event Action<List<SdrPingResult>>? PingUpdated;

    private readonly List<ValvePop> _pops = new();

    private readonly Dictionary<(string, string), int> _typicalPings = new();

    private List<SdrPingResult> _cachedResults = new();

    private bool _isRefreshing;

    public DotaSdrPingService()
    {
        InitializeDefaultPops();
    }

    /// <summary>
    /// Динамическое переключение игры для Steam Datagram Relay или облачных дата-центров
    /// </summary>
    public async Task<List<SdrPingResult>> SwitchGameAsync(int appId, IEnumerable<ValvePop> defaultPops, string defaultRoute = "Прямой (Direct)")
    {
        _currentAppId = appId;
        _currentDefaultRoute = defaultRoute;

        lock (_pops)
        {
            _pops.Clear();
            _pops.AddRange(defaultPops);
        }

        lock (_cachedResults)
        {
            _cachedResults.Clear();
        }

        // 1. Быстрый первичный замер по базовым релеям для мгновенного отклика UI
        var results = await RefreshAsync().ConfigureAwait(false);

        // 2. В фоне скачиваем актуальную конфигурацию с серверов Valve для выбранного AppID (если это игра Valve)
        if (_currentAppId > 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await FetchConfigFromValveAsync().ConfigureAwait(false);
                    await RefreshAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Игнорируем временные сетевые сбои
                }
            });
        }

        return results;
    }

    /// <summary>
    /// Возвращает последние известные результаты замера SDR
    /// </summary>
    public List<SdrPingResult> GetLatestSdrPings()
    {
        lock (_cachedResults)
        {
            return new List<SdrPingResult>(_cachedResults);
        }
    }

    /// <summary>
    /// Запуск сервиса в фоновом режиме (периодическое обновление)
    /// </summary>
    public async Task StartAsync()
    {
        // 1. Сразу опрашиваем встроенные кластеры Valve / AWS, чтобы пользователь не ждал HTTP-запроса
        _ = RefreshAsync();

        // 2. В фоне загружаем актуальную конфигурацию с серверов Valve (только если AppID > 0)
        if (_currentAppId > 0)
        {
            await FetchConfigFromValveAsync().ConfigureAwait(false);
            await RefreshAsync().ConfigureAwait(false);
        }

        // 3. Фоновый цикл обновления каждые 15 секунд
        while (true)
        {
            try
            {
                await Task.Delay(15000).ConfigureAwait(false);
                await RefreshAsync().ConfigureAwait(false);
            }
            catch
            {
                // Игнорируем временные сетевые сбои
            }
        }
    }

    /// <summary>
    /// Выполняет параллельный опрос всех SDR-кластеров Valve и расчет оптимальных маршрутов
    /// </summary>
    public async Task<List<SdrPingResult>> RefreshAsync()
    {
        if (_isRefreshing)
        {
            return GetLatestSdrPings();
        }

        _isRefreshing = true;

        try
        {
            List<ValvePop> currentPops;
            lock (_pops)
            {
                currentPops = new List<ValvePop>(_pops);
            }

            // 1. Опрашиваем кластеры Valve пачками (batching по 4 сервера) с микропаузами
            // Это исключает лавину сырых сокетов и системные прерывания в NDIS.sys/tcpip.sys
            const int batchSize = 4;
            var pingResults = new List<(ValvePop Pop, long DirectPing)>();
            var targetPops = currentPops.Where(p => p.Relays.Count > 0).ToList();

            for (int i = 0; i < targetPops.Count; i += batchSize)
            {
                var batch = targetPops.Skip(i).Take(batchSize).ToList();
                var batchTasks = batch.Select(async pop =>
                {
                    long directPing = await PingPopRelaysAsync(pop).ConfigureAwait(false);
                    return (Pop: pop, DirectPing: directPing);
                });

                var batchRes = await Task.WhenAll(batchTasks).ConfigureAwait(false);
                pingResults.AddRange(batchRes);

                if (i + batchSize < targetPops.Count)
                {
                    await Task.Delay(50).ConfigureAwait(false);
                }
            }

            var directPingMap = pingResults
                .Where(r => r.DirectPing > 0)
                .ToDictionary(r => r.Pop.Code.ToLowerInvariant(), r => r.DirectPing);

            var finalResults = new List<SdrPingResult>();

            // 2. Для каждого кластера определяем лучший маршрут (прямой или через SDR-релей)
            foreach (var (pop, directPing) in pingResults)
            {
                string popCode = pop.Code.ToLowerInvariant();
                long bestPing = directPing > 0 ? directPing : -1;
                string route = bestPing > 0 
                    ? (_currentAppId <= 0 ? _currentDefaultRoute : "Прямой (Direct)") 
                    : "Недоступен";

                // Проверяем возможность транзитной маршрутизации через близкие SDR-релеи (только для Valve SDR)
                if (_currentAppId > 0 && directPingMap.Count > 0)
                {
                    foreach (var (otherCode, otherPing) in directPingMap)
                    {
                        if (otherCode == popCode)
                            continue;

                        // Ищем типичную задержку между кластерами Valve
                        int interClusterLatency = GetTypicalPing(otherCode, popCode);
                        if (interClusterLatency > 0)
                        {
                            long routedPing = otherPing + interClusterLatency;

                            // Если маршрут через соседний релей быстрее или прямой недоступен
                            if (bestPing <= 0 || routedPing < bestPing)
                            {
                                bestPing = routedPing;
                                route = $"SDR через {otherCode.ToUpperInvariant()}";
                            }
                        }
                    }
                }

                if (bestPing > 0)
                {
                    finalResults.Add(new SdrPingResult
                    {
                        Code = pop.Code.ToUpperInvariant(),
                        RegionName = CleanRegionName(pop.Desc),
                        PingMs = bestPing,
                        Route = route
                    });
                }
            }

            // Сортируем от лучшего пинга к худшему
            finalResults = finalResults.OrderBy(r => r.PingMs).ToList();

            lock (_cachedResults)
            {
                _cachedResults = finalResults;
            }

            if (finalResults.Count > 0)
            {
                PingUpdated?.Invoke(finalResults);
            }

            return finalResults;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    /// <summary>
    /// Опрос релеев конкретного кластера: берем первые 3 адреса и возвращаем минимальную задержку
    /// </summary>
    private static async Task<long> PingPopRelaysAsync(ValvePop pop)
    {
        if (pop.Relays.Count == 0)
            return -1;

        int checksToRun = Math.Min(2, pop.Relays.Count);
        for (int i = 0; i < checksToRun; i++)
        {
            string ip = pop.Relays[i];
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, 600).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success)
                {
                    return Math.Max(1, reply.RoundtripTime);
                }
            }
            catch
            {
                // Таймаут или ошибка — пробуем следующий
            }
        }

        return -1L;
    }

    /// <summary>
    /// Скачивание официальной карты сети SDR Valve
    /// </summary>
    public async Task FetchConfigFromValveAsync()
    {
        if (_currentAppId <= 0)
            return;

        try
        {
            string json = await HttpClient.GetStringAsync(SdrConfigUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("pops", out var popsElement))
            {
                var newPops = new List<ValvePop>();

                foreach (var popProperty in popsElement.EnumerateObject())
                {
                    string code = popProperty.Name;
                    var popObj = popProperty.Value;

                    string desc = popObj.TryGetProperty("desc", out var descProp)
                        ? descProp.GetString() ?? code
                        : code;

                    var relays = new List<string>();
                    if (popObj.TryGetProperty("relays", out var relaysProp) &&
                        relaysProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var relayObj in relaysProp.EnumerateArray())
                        {
                            if (relayObj.TryGetProperty("ipv4", out var ipProp))
                            {
                                string? ip = ipProp.GetString();
                                if (!string.IsNullOrEmpty(ip))
                                {
                                    relays.Add(ip);
                                }
                            }
                        }
                    }

                    if (relays.Count > 0)
                    {
                        newPops.Add(new ValvePop
                        {
                            Code = code,
                            Desc = desc,
                            Relays = relays
                        });
                    }
                }

                if (newPops.Count > 0)
                {
                    lock (_pops)
                    {
                        _pops.Clear();
                        _pops.AddRange(newPops);
                    }
                }
            }

            // Загружаем типичные межкластерные задержки Valve (typical_pings)
            if (root.TryGetProperty("typical_pings", out var typicalElement) &&
                typicalElement.ValueKind == JsonValueKind.Array)
            {
                lock (_typicalPings)
                {
                    _typicalPings.Clear();
                    foreach (var item in typicalElement.EnumerateArray())
                    {
                        if (item.GetArrayLength() >= 3)
                        {
                            string from = item[0].GetString()?.ToLowerInvariant() ?? "";
                            string to = item[1].GetString()?.ToLowerInvariant() ?? "";
                            if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to) && item[2].TryGetInt32(out int latency))
                            {
                                _typicalPings[(from, to)] = latency;
                                _typicalPings[(to, from)] = latency;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Если сеть недоступна, используются базовые дефолтные POPs
        }
    }

    public int GetTypicalPing(string from, string to)
    {
        lock (_typicalPings)
        {
            if (_typicalPings.TryGetValue((from, to), out int latency))
                return latency;
            if (_typicalPings.TryGetValue((to, from), out int revLatency))
                return revLatency;
        }
        return -1;
    }

    /// <summary>
    /// Определение целевого региона/кластера матча по входному релею и внутренней задержке Valve (backhaul)
    /// </summary>
    public string? FindDestinationCluster(string ingressCode, int backhaulLatencyMs)
    {
        if (backhaulLatencyMs <= 15)
        {
            return ingressCode;
        }

        string from = ingressCode.ToLowerInvariant();
        string? bestCluster = null;
        int minDiff = int.MaxValue;

        lock (_typicalPings)
        {
            foreach (var (pair, latency) in _typicalPings)
            {
                string? candidate = null;
                if (pair.Item1.Equals(from, StringComparison.OrdinalIgnoreCase))
                    candidate = pair.Item2;
                else if (pair.Item2.Equals(from, StringComparison.OrdinalIgnoreCase))
                    candidate = pair.Item1;

                if (!string.IsNullOrEmpty(candidate))
                {
                    int diff = Math.Abs(latency - backhaulLatencyMs);
                    if (diff < minDiff && diff <= 35)
                    {
                        minDiff = diff;
                        bestCluster = candidate;
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(bestCluster))
        {
            return bestCluster;
        }

        // Встроенная экспертная эвристика межконтинентальных транзитов Valve
        return ResolveFallbackInterCluster(from, backhaulLatencyMs);
    }

    private static string? ResolveFallbackInterCluster(string ingress, int backhaul)
    {
        // Для европейских входных релеев (AMS, FRA, STO, WAW, VIE)
        if (ingress is "ams" or "fra" or "sto" or "sto2" or "waw" or "vie" or "lux" or "lhr" or "par" or "mad")
        {
            if (backhaul is >= 145 and <= 175) return "jnb"; // Йоханнесбург (ЮАР) ~160ms
            if (backhaul is >= 176 and <= 205) return "bom"; // Мумбаи (Индия) ~185ms
            if (backhaul is >= 206 and <= 235) return "maa"; // Ченнаи (Индия) ~215ms
            if (backhaul is >= 100 and <= 130) return "dxb"; // Дубай (ОАЭ) ~115ms
            if (backhaul is >= 70 and <= 95) return "iad";   // Вирджиния (US East) ~85ms
            if (backhaul is >= 96 and <= 118) return "ord";  // Чикаго (US Central) ~105ms
            if (backhaul is >= 130 and <= 165) return "eat"; // Сиэтл / US West ~145ms
            if (backhaul is >= 155 and <= 185) return "sgp"; // Сингапур ~165ms
            if (backhaul is >= 180 and <= 220) return "gru"; // Сан-Паулу (Бразилия) ~195ms
            if (backhaul is >= 221 and <= 260) return "scl"; // Сантьяго (Чили) ~230ms
            if (backhaul is >= 261 and <= 310) return "syd"; // Сидней (Австралия) ~280ms
            if (backhaul is >= 200 and <= 245) return "tyo"; // Токио (Япония) ~220ms
        }
        // Для североамериканских релеев (IAD, ORD, EAT, LAX)
        else if (ingress is "iad" or "ord" or "eat" or "lax")
        {
            if (backhaul is >= 65 and <= 95) return "fra";   // Европа ~80ms
            if (backhaul is >= 105 and <= 145) return "gru"; // Южная Америка ~120ms
            if (backhaul is >= 100 and <= 140) return "tyo"; // Япония ~115ms
            if (backhaul is >= 170 and <= 220) return "sgp"; // Сингапур ~190ms
            if (backhaul is >= 180 and <= 240) return "syd"; // Сидней ~200ms
        }

        return null;
    }

    private static string CleanRegionName(string desc)
    {
        if (string.IsNullOrWhiteSpace(desc))
            return "Неизвестный";

        // "Stockholm - Kista (Sweden)" -> "Stockholm (Kista)"
        // "Frankfurt (Germany)" -> "Frankfurt"
        int parenIndex = desc.IndexOf('(');
        if (parenIndex > 0)
        {
            string baseName = desc[..parenIndex].Trim();
            if (!string.IsNullOrEmpty(baseName))
            {
                return baseName.Replace(" - ", " ");
            }
        }

        return desc;
    }

    /// <summary>
    /// Поиск резервных / альтернативных IP-адресов релеев для указанного целевого сервера
    /// </summary>
    public List<string> GetAlternativeRelays(string targetName, string currentIp)
    {
        var result = new List<string>();
        lock (_pops)
        {
            // 1. Поиск по совпадению текущего IP среди пула PoP
            foreach (var pop in _pops)
            {
                if (pop.Relays.Contains(currentIp))
                {
                    foreach (var r in pop.Relays)
                    {
                        if (!r.Equals(currentIp, StringComparison.OrdinalIgnoreCase) && !result.Contains(r))
                        {
                            result.Add(r);
                        }
                    }
                }
            }

            // 2. Поиск по названию / коду региона
            if (result.Count == 0 && !string.IsNullOrWhiteSpace(targetName))
            {
                foreach (var pop in _pops)
                {
                    bool match = (!string.IsNullOrEmpty(pop.Desc) && targetName.Contains(pop.Desc, StringComparison.OrdinalIgnoreCase)) ||
                                 (!string.IsNullOrEmpty(pop.Code) && targetName.Contains(pop.Code, StringComparison.OrdinalIgnoreCase));

                    if (!match && !string.IsNullOrEmpty(pop.Desc))
                    {
                        var parts = pop.Desc.Split(new[] { ' ', '(', ')', '—', '-', '/' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var part in parts)
                        {
                            if (part.Length >= 4 && targetName.Contains(part, StringComparison.OrdinalIgnoreCase))
                            {
                                match = true;
                                break;
                            }
                        }
                    }

                    if (match)
                    {
                        foreach (var r in pop.Relays)
                        {
                            if (!r.Equals(currentIp, StringComparison.OrdinalIgnoreCase) && !result.Contains(r))
                            {
                                result.Add(r);
                            }
                        }
                    }
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Встроенный список основных POPs Valve для мгновенной инициализации до первого ответа сети
    /// </summary>
    private void InitializeDefaultPops()
    {
        _pops.AddRange(new[]
        {
            new ValvePop { Code = "sto", Desc = "Stockholm (Sweden)", Relays = new() { "162.254.198.41", "162.254.198.42", "162.254.198.43" } },
            new ValvePop { Code = "sto2", Desc = "Stockholm 2 (Sweden)", Relays = new() { "155.133.252.50", "155.133.252.51" } },
            new ValvePop { Code = "waw", Desc = "Warsaw (Poland)", Relays = new() { "155.133.230.98", "155.133.230.99", "155.133.230.100" } },
            new ValvePop { Code = "fra", Desc = "Frankfurt (Germany)", Relays = new() { "155.133.226.68", "155.133.226.70", "162.254.197.36" } },
            new ValvePop { Code = "vie", Desc = "Vienna (Austria)", Relays = new() { "155.133.242.4", "155.133.242.5" } },
            new ValvePop { Code = "ams", Desc = "Amsterdam (Netherlands)", Relays = new() { "155.133.248.50", "155.133.248.51" } },
            new ValvePop { Code = "lhr", Desc = "London (UK)", Relays = new() { "162.254.196.82", "162.254.196.83" } },
            new ValvePop { Code = "par", Desc = "Paris (France)", Relays = new() { "162.254.199.178", "162.254.199.179" } },
            new ValvePop { Code = "mad", Desc = "Madrid (Spain)", Relays = new() { "155.133.246.39", "155.133.246.40" } },
            new ValvePop { Code = "dxb", Desc = "Dubai (UAE)", Relays = new() { "185.25.183.65", "185.25.183.66" } },
            new ValvePop { Code = "sgp", Desc = "Singapore", Relays = new() { "103.10.124.116", "103.10.124.118" } },
            new ValvePop { Code = "tyo", Desc = "Tokyo (Japan)", Relays = new() { "45.121.184.24", "45.121.184.26" } },
            new ValvePop { Code = "seo", Desc = "Seoul (Korea)", Relays = new() { "155.133.234.98" } },
            new ValvePop { Code = "hkg", Desc = "Hong Kong", Relays = new() { "155.133.244.18" } },
            new ValvePop { Code = "syd", Desc = "Sydney (Australia)", Relays = new() { "103.10.125.146" } },
            new ValvePop { Code = "iad", Desc = "Washington (US East)", Relays = new() { "162.254.192.82" } },
            new ValvePop { Code = "ord", Desc = "Chicago (US Central)", Relays = new() { "162.254.193.7" } },
            new ValvePop { Code = "atl", Desc = "Atlanta (US)", Relays = new() { "162.254.199.42" } },
            new ValvePop { Code = "dfw", Desc = "Dallas (US)", Relays = new() { "162.254.195.70" } },
            new ValvePop { Code = "lax", Desc = "Los Angeles (US West)", Relays = new() { "162.254.194.42" } },
            new ValvePop { Code = "sea", Desc = "Seattle (US Northwest)", Relays = new() { "205.196.6.135", "205.196.6.149" } },
            new ValvePop { Code = "gru", Desc = "São Paulo (Brazil)", Relays = new() { "205.185.194.42" } },
            new ValvePop { Code = "scl", Desc = "Santiago (Chile)", Relays = new() { "155.133.249.18" } },
            new ValvePop { Code = "lim", Desc = "Lima (Peru)", Relays = new() { "190.217.33.42" } },
            new ValvePop { Code = "eze", Desc = "Buenos Aires (Argentina)", Relays = new() { "155.133.255.162" } },
            new ValvePop { Code = "bom2", Desc = "Mumbai (India)", Relays = new() { "155.133.233.98" } },
            new ValvePop { Code = "jnb", Desc = "Johannesburg (South Africa)", Relays = new() { "155.133.238.18" } }
        });
    }
}