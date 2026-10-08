using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DotaPingMonitor.Models;
using Microsoft.Win32;

namespace DotaPingMonitor.Services;

/// <summary>
/// Сервис автоматического отслеживания активного матча Dota 2.
/// Определяет выделенный игровой сервер Valve и первичный SDR-релей через passive console.log streaming.
/// 100% VAC-безопасно (только чтение текстового лога, никаких хуков и инъекций).
/// </summary>
public partial class DotaMatchTrackerService : IGameMatchTracker
{
    private readonly System.Timers.Timer _pollTimer;
    private readonly object _syncLock = new();
    private readonly Cs2MatchTrackerService _cs2Tracker = new();
    private readonly DeadlockMatchTrackerService _deadlockTracker = new();
    private string _activeGameId = "dota2";
    private string _activeGameName = "Dota 2";

    public string ActiveGameId => _activeGameId;
    public string ActiveGameName => _activeGameName;

    private string? _cachedLogPath;
    private FileStream? _fileStream;
    private long _lastPosition = 0;
    private bool _isInitialized = false;
    private bool _isDisposed = false;
    private DateTime _lastProcessCheckTime = DateTime.MinValue;
    private bool _wasDotaRunning = false;
    private Func<string, int, string?>? _destinationClusterResolver;

    public void SetDestinationResolver(Func<string, int, string?>? resolver)
    {
        _destinationClusterResolver = resolver;
        _cs2Tracker.SetDestinationResolver(resolver);
        _deadlockTracker.SetDestinationResolver(resolver);
    }

    [GeneratedRegex(@"(?:Selecting|Swapping primary to)\s+(?<cluster>[a-zA-Z0-9_-]+)(?:#\d+)?\s+\((?<ip>[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+):(?<port>[0-9]+)\)\s+as\s+primary(?:.*?(?:Ping\s*=\s*(?<total>\d+)\s*=\s*(?<front>\d+)\+(?<interior>\d+)))?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RelayPattern();

    [GeneratedRegex(@"Requesting session from\s+(?<cluster>[a-zA-Z0-9_-]+)(?:#\d+)?\s+\((?<ip>[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+):(?<port>[0-9]+)\).*?Ping\s*=\s*(?<total>\d+)\s*=\s*(?<front>\d+)\+(?<back>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SessionRequestPattern();

    [GeneratedRegex(@"Primary router:\s+(?<cluster>[a-zA-Z0-9_-]+)(?:#\d+)?\s+\((?<ip>[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+):(?<port>[0-9]+)\).*?Ping\s*=\s*(?<front>\d+)\+(?<back>\d+)=(?<total>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PrimaryRouterSummaryPattern();

    [GeneratedRegex(@"\[Networking\]\s+Ping:\s*(?<ping>\d+)ms", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex NetworkingPingPattern();

    [GeneratedRegex(@"(?:udp/ip\s*:\s*|Connected to\s+|Connecting to\s+public\()([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+):([0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DirectConnectionPattern();

    [GeneratedRegex(@"(?:Disconnecting|Disconnected)\s+from\s+server|NETWORK_DISCONNECT_", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DisconnectPattern();

    [GeneratedRegex(@"CL:\s+Connected to '\[A:1:(\d+:\d+)\]:\d+'", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ServerSteamIdPattern();

    public bool IsDotaRunningWithoutLog { get; private set; }
    public bool IsRunningWithoutLog => _activeGameId switch
    {
        "cs2" => _cs2Tracker.IsRunningWithoutLog,
        "deadlock" => _deadlockTracker.IsRunningWithoutLog,
        _ => IsDotaRunningWithoutLog
    };
    public event Action<bool>? DotaLoggingStatusChanged;
    public event Action<bool>? LoggingStatusChanged;
    private DateTime _dotaStartTime = DateTime.MinValue;

    // Карта известных кластеров Valve POP
    private static readonly Dictionary<string, (string Ru, string En)> ClusterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sto"] = ("Стокгольм (Швеция)", "Stockholm (Sweden)"),
        ["sto2"] = ("Стокгольм 2 (Швеция)", "Stockholm 2 (Sweden)"),
        ["fra"] = ("Франкфурт (Германия)", "Frankfurt (Germany)"),
        ["vie"] = ("Вена (Австрия)", "Vienna (Austria)"),
        ["lux"] = ("Люксембург", "Luxembourg"),
        ["ams"] = ("Амстердам (Нидерланды)", "Amsterdam (Netherlands)"),
        ["waw"] = ("Варшава (Польша)", "Warsaw (Poland)"),
        ["hel"] = ("Хельсинки (Финляндия)", "Helsinki (Finland)"),
        ["par"] = ("Париж (Франция)", "Paris (France)"),
        ["mad"] = ("Мадрид (Испания)", "Madrid (Spain)"),
        ["lhr"] = ("Лондон (Великобритания)", "London (UK)"),
        ["dxb"] = ("Дубай (ОАЭ)", "Dubai (UAE)"),
        ["sgp"] = ("Сингапур", "Singapore"),
        ["tyo"] = ("Токио (Япония)", "Tokyo (Japan)"),
        ["tyo1"] = ("Токио 1 (Япония)", "Tokyo 1 (Japan)"),
        ["tyo2"] = ("Токио 2 (Япония)", "Tokyo 2 (Japan)"),
        ["seo"] = ("Сеул (Южная Корея)", "Seoul (South Korea)"),
        ["hkg"] = ("Гонконг", "Hong Kong"),
        ["syd"] = ("Сидней (Австралия)", "Sydney (Australia)"),
        ["eat"] = ("Сиэтл (США)", "Seattle (US)"),
        ["iad"] = ("Вирджиния (США)", "Virginia (US East)"),
        ["ord"] = ("Чикаго (США)", "Chicago (US Central)"),
        ["lax"] = ("Лос-Анджелес (США)", "Los Angeles (US West)"),
        ["gru"] = ("Сан-Паулу (Бразилия)", "São Paulo (Brazil)"),
        ["scl"] = ("Сантьяго (Чили)", "Santiago (Chile)"),
        ["lim"] = ("Лима (Перу)", "Lima (Peru)"),
        ["jnb"] = ("Йоханнесбург (ЮАР)", "Johannesburg (South Africa)"),
        ["bom"] = ("Мумбаи (Индия)", "Mumbai (India)"),
        ["maa"] = ("Ченнаи (Индия)", "Chennai (India)"),
        ["can"] = ("Гуанчжоу (Китай)", "Guangzhou (China)"),
        ["sha"] = ("Шанхай (Китай)", "Shanghai (China)"),
        ["tsn"] = ("Тяньцзинь (Китай)", "Tianjin (China)"),
        ["pwg"] = ("Гуанчжоу (Perfect World)", "Guangzhou (PW)"),
        ["pwj"] = ("Чжэцзян (Perfect World)", "Zhejiang (PW)"),
        ["pwh"] = ("Хэбэй (Perfect World)", "Hebei (PW)"),
        ["pwu"] = ("Ухань (Perfect World)", "Wuhan (PW)"),
        ["pwz"] = ("Цзыбо (Perfect World)", "Zibo (PW)")
    };

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            if (_isEnabled)
            {
                CloseStream();
                _lastPosition = 0;
                _isInitialized = false;
                _cachedLogPath = FindDotaConsoleLog();
                TriggerPollNow();
            }
            else
            {
                CloseStream();
                if (IsInMatch)
                {
                    HandleMatchDisconnected();
                }
            }
        }
    }

    public bool IsInMatch { get; private set; }
    public DotaMatchInfo? CurrentMatch { get; private set; }
    public string? LogFilePath => _cachedLogPath;

    public event Action<DotaMatchInfo>? MatchConnected;
    public event Action? MatchDisconnected;
    public event Action<string>? StatusChanged;

    public void TriggerPollNow()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            _pollTimer.Stop();
            _pollTimer.Start();
        }
    }

    public DotaMatchTrackerService()
    {
        _pollTimer = new System.Timers.Timer(400); // Опрос лога каждые 400 мс
        _pollTimer.AutoReset = false;
        _pollTimer.Elapsed += (_, _) => OnPollTimerTick();

        _cs2Tracker.MatchConnected += m => { if (_activeGameId == "cs2") { IsInMatch = true; CurrentMatch = m; MatchConnected?.Invoke(m); } };
        _cs2Tracker.MatchDisconnected += () => { if (_activeGameId == "cs2") { IsInMatch = false; CurrentMatch = null; MatchDisconnected?.Invoke(); } };
        _cs2Tracker.StatusChanged += s => { if (_activeGameId == "cs2") StatusChanged?.Invoke(s); };
        _cs2Tracker.LoggingStatusChanged += b => { if (_activeGameId == "cs2") { DotaLoggingStatusChanged?.Invoke(b); LoggingStatusChanged?.Invoke(b); } };

        _deadlockTracker.MatchConnected += m => { if (_activeGameId == "deadlock") { IsInMatch = true; CurrentMatch = m; MatchConnected?.Invoke(m); } };
        _deadlockTracker.MatchDisconnected += () => { if (_activeGameId == "deadlock") { IsInMatch = false; CurrentMatch = null; MatchDisconnected?.Invoke(); } };
        _deadlockTracker.StatusChanged += s => { if (_activeGameId == "deadlock") StatusChanged?.Invoke(s); };
        _deadlockTracker.LoggingStatusChanged += b => { if (_activeGameId == "deadlock") { DotaLoggingStatusChanged?.Invoke(b); LoggingStatusChanged?.Invoke(b); } };
    }

    public void SetGame(string gameId)
    {
        lock (_syncLock)
        {
            if (string.IsNullOrWhiteSpace(gameId)) gameId = "dota2";
            string norm = gameId.Trim().ToLowerInvariant();
            if (_activeGameId == norm) return;

            _activeGameId = norm;
            _activeGameName = norm switch
            {
                "cs2" => "Counter-Strike 2",
                "deadlock" => "Deadlock",
                _ => "Dota 2"
            };

            if (_activeGameId == "cs2")
            {
                _cs2Tracker.SetDestinationResolver(_destinationClusterResolver);
                _cs2Tracker.Start();
                _deadlockTracker.Stop();
            }
            else if (_activeGameId == "deadlock")
            {
                _deadlockTracker.SetDestinationResolver(_destinationClusterResolver);
                _deadlockTracker.Start();
                _cs2Tracker.Stop();
            }
            else
            {
                _cs2Tracker.Stop();
                _deadlockTracker.Stop();
            }

            CloseStream();
            _lastPosition = 0;
            _isInitialized = false;
            _cachedLogPath = FindDotaConsoleLog();
            if (IsInMatch)
            {
                HandleMatchDisconnected();
            }
            TriggerPollNow();
        }
    }

    public void Start()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            _pollTimer.Start();
        }
    }

    public void Stop()
    {
        lock (_syncLock)
        {
            _pollTimer.Stop();
            CloseStream();
            if (IsInMatch)
            {
                IsInMatch = false;
                CurrentMatch = null;
                MatchDisconnected?.Invoke();
            }
        }
    }

    private int _lastDotaPid = 0;

    private int GetCurrentDotaPid()
    {
        try
        {
            var processes = Process.GetProcessesByName("dota2");
            if (processes.Length > 0)
            {
                int pid = processes[0].Id;
                try { _dotaStartTime = processes[0].StartTime.ToUniversalTime(); } catch { }
                foreach (var p in processes) p.Dispose();
                return pid;
            }
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private void OnPollTimerTick()
    {
        try
        {
            if (!IsEnabled || _isDisposed) return;

            // 1. Динамический watcher жизненного цикла процесса Dota 2 (каждые 1.5 сек)
            var now = DateTime.UtcNow;
            if ((now - _lastProcessCheckTime).TotalSeconds >= 1.5)
            {
                _lastProcessCheckTime = now;
                int currentPid = GetCurrentDotaPid();
                bool isDotaRunning = currentPid > 0;

                // Случай А: Dota 2 закрылась
                if (!isDotaRunning && _wasDotaRunning)
                {
                    _wasDotaRunning = false;
                    _lastDotaPid = 0;
                    CloseStream();
                    _lastPosition = 0;
                    _isInitialized = false;
                    if (IsInMatch)
                    {
                        HandleMatchDisconnected();
                    }
                }
                // Случай Б: Dota 2 запустилась или перезапустилась (новый PID)!
                else if (isDotaRunning && (!_wasDotaRunning || currentPid != _lastDotaPid))
                {
                    _wasDotaRunning = true;
                    _lastDotaPid = currentPid;
                    CloseStream();
                    _lastPosition = 0;
                    _isInitialized = false;
                    _cachedLogPath = FindDotaConsoleLog();
                }
            }

            // 2. Убеждаемся, что console.log найден
            if (string.IsNullOrEmpty(_cachedLogPath) || !File.Exists(_cachedLogPath))
            {
                _cachedLogPath = FindDotaConsoleLog();
                if (string.IsNullOrEmpty(_cachedLogPath))
                {
                    return;
                }
            }

            // 3. Проверяем, пишет ли Dota в лог или запущена без -condebug
            if (_wasDotaRunning)
            {
                bool stale = false;
                if (!string.IsNullOrEmpty(_cachedLogPath) && File.Exists(_cachedLogPath))
                {
                    try
                    {
                        var fi = new FileInfo(_cachedLogPath);
                        stale = _dotaStartTime > DateTime.MinValue && (fi.LastWriteTimeUtc < _dotaStartTime);
                    }
                    catch { }
                }
                else
                {
                    stale = true;
                }

                if (stale != IsDotaRunningWithoutLog)
                {
                    IsDotaRunningWithoutLog = stale;
                    DotaLoggingStatusChanged?.Invoke(stale);
                }
            }
            else if (IsDotaRunningWithoutLog)
            {
                IsDotaRunningWithoutLog = false;
                DotaLoggingStatusChanged?.Invoke(false);
            }

            // 4. Читаем новые строки лога
            ProcessLogUpdates();
        }
        catch
        {
            // Игнорируем временные сбои ввода-вывода
        }
        finally
        {
            lock (_syncLock)
            {
                if (!_isDisposed && IsEnabled)
                {
                    _pollTimer.Start();
                }
            }
        }
    }

    private void ProcessLogUpdates()
    {
        if (string.IsNullOrEmpty(_cachedLogPath)) return;

        try
        {
            var fileInfo = new FileInfo(_cachedLogPath);
            fileInfo.Refresh();
            if (!fileInfo.Exists) return;

            long currentLength = fileInfo.Length;

            // Проверяем, не уменьшился ли файл (очистка/перезапуск лога игрой)
            if (currentLength < _lastPosition)
            {
                _lastPosition = 0;
            }

            // Первый запуск: сканируем конец файла (до 256 КБ), чтобы гарантированно определить,
            // находится ли игрок УЖЕ в матче на момент открытия монитора или включения функции
            if (!_isInitialized || _lastPosition == 0)
            {
                _isInitialized = true;
                using (var fs = new FileStream(_cachedLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    const int initialScanBytes = 262144;
                    if (fs.Length > initialScanBytes)
                    {
                        fs.Seek(-initialScanBytes, SeekOrigin.End);
                    }
                    else
                    {
                        fs.Seek(0, SeekOrigin.Begin);
                    }
                    ReadAndAnalyzeLines(fs, isInitialScan: true);
                    _lastPosition = fs.Length;
                }
                return;
            }

            // Обычный цикл: если движок Dota 2 дописал новые байты в лог
            if (currentLength > _lastPosition)
            {
                using (var fs = new FileStream(_cachedLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    fs.Seek(_lastPosition, SeekOrigin.Begin);
                    ReadAndAnalyzeLines(fs, isInitialScan: false);
                    _lastPosition = fs.Length;
                }
            }
        }
        catch (IOException)
        {
            // Файл временно занят игровым процессом
        }
        catch (Exception)
        {
        }
    }

    private void ReadAndAnalyzeLines(FileStream stream, bool isInitialScan)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        string? line;

        DotaMatchInfo? pendingMatch = null;
        bool disconnectedFound = false;

        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            // 1. Проверяем выбор первичного релея Valve
            var relayMatch = RelayPattern().Match(line);
            if (relayMatch.Success)
            {
                string clusterCode = relayMatch.Groups["cluster"].Value.Trim().ToLowerInvariant();
                string relayIp = relayMatch.Groups["ip"].Value.Trim();
                int port = int.TryParse(relayMatch.Groups["port"].Value, out int p) ? p : 27015;

                int total = 0, front = 0, interior = 0;
                if (relayMatch.Groups["total"].Success) int.TryParse(relayMatch.Groups["total"].Value, out total);
                if (relayMatch.Groups["front"].Success) int.TryParse(relayMatch.Groups["front"].Value, out front);
                if (relayMatch.Groups["interior"].Success) int.TryParse(relayMatch.Groups["interior"].Value, out interior);

                var (ru, en) = GetClusterDisplayName(clusterCode);
                string displayName = LocalizationService.IsRussian ? ru : en;

                var matchInfo = new DotaMatchInfo
                {
                    IsConnected = true,
                    ClusterCode = clusterCode,
                    ClusterDisplayName = displayName,
                    RelayIp = relayIp,
                    RelayPort = port,
                    FrontLatencyMs = front,
                    BackhaulLatencyMs = interior,
                    LastReportedTotalPingMs = total > 0 ? total : (front + interior),
                    ConnectedAt = DateTime.Now,
                    RawLogLine = line
                };

                PopulateDestinationInfo(matchInfo);

                if (isInitialScan)
                {
                    pendingMatch = matchInfo;
                    disconnectedFound = false;
                }
                else
                {
                    HandleMatchConnected(matchInfo);
                }
                continue;
            }

            // 1b. Сводка задержки основного роутера (Primary router: ams#90 ... Ping = 46+160=206)
            var summaryMatch = PrimaryRouterSummaryPattern().Match(line);
            if (summaryMatch.Success && CurrentMatch != null)
            {
                int.TryParse(summaryMatch.Groups["front"].Value, out int front);
                int.TryParse(summaryMatch.Groups["back"].Value, out int back);
                int.TryParse(summaryMatch.Groups["total"].Value, out int total);

                CurrentMatch.FrontLatencyMs = front;
                CurrentMatch.BackhaulLatencyMs = back;
                CurrentMatch.LastReportedTotalPingMs = total;
                PopulateDestinationInfo(CurrentMatch);
                MatchConnected?.Invoke(CurrentMatch);
                continue;
            }

            // 1c. Запрос сессии (Requesting session from ... Ping = 201 = 41+160)
            var sessMatch = SessionRequestPattern().Match(line);
            if (sessMatch.Success && CurrentMatch != null && CurrentMatch.BackhaulLatencyMs == 0)
            {
                int.TryParse(sessMatch.Groups["front"].Value, out int front);
                int.TryParse(sessMatch.Groups["back"].Value, out int back);
                int.TryParse(sessMatch.Groups["total"].Value, out int total);

                CurrentMatch.FrontLatencyMs = front;
                CurrentMatch.BackhaulLatencyMs = back;
                CurrentMatch.LastReportedTotalPingMs = total;
                PopulateDestinationInfo(CurrentMatch);
                MatchConnected?.Invoke(CurrentMatch);
                continue;
            }

            // 1d. Периодический сетевой пинг движка ([Networking] Ping:207ms)
            var netPingMatch = NetworkingPingPattern().Match(line);
            if (netPingMatch.Success && CurrentMatch != null)
            {
                if (int.TryParse(netPingMatch.Groups["ping"].Value, out int ping) && ping > 0)
                {
                    CurrentMatch.LastReportedTotalPingMs = ping;
                }
                continue;
            }

            // 1e. Проверяем прямые IP-адреса и udp/ip
            var directMatch = DirectConnectionPattern().Match(line);
            if (directMatch.Success)
            {
                string ip = directMatch.Groups[1].Value.Trim();
                int port = int.TryParse(directMatch.Groups[2].Value, out int p) ? p : 27015;

                if (ip != "127.0.0.1" && !ip.StartsWith("192.168.") && !ip.StartsWith("10.") && !ip.StartsWith("0.0.0."))
                {
                    string clusterCode = GuessClusterCodeFromIp(ip);
                    var (ru, en) = GetClusterDisplayName(clusterCode);
                    string displayName = LocalizationService.IsRussian ? ru : en;

                    var matchInfo = new DotaMatchInfo
                    {
                        IsConnected = true,
                        ClusterCode = clusterCode,
                        ClusterDisplayName = displayName,
                        RelayIp = ip,
                        RelayPort = port,
                        ConnectedAt = DateTime.Now,
                        RawLogLine = line
                    };

                    PopulateDestinationInfo(matchInfo);

                    if (isInitialScan)
                    {
                        pendingMatch = matchInfo;
                        disconnectedFound = false;
                    }
                    else
                    {
                        HandleMatchConnected(matchInfo);
                    }
                    continue;
                }
            }

            // 2. Проверяем отключение от сервера (исключаем внутренний переход интерфейса/лобби LOOPDEACTIVATE)
            if (DisconnectPattern().IsMatch(line) && !line.Contains("LOOPDEACTIVATE", StringComparison.OrdinalIgnoreCase))
            {
                if (isInitialScan)
                {
                    disconnectedFound = true;
                    pendingMatch = null;
                }
                else
                {
                    HandleMatchDisconnected();
                }
                continue;
            }

            // 3. Привязка SteamID сервера, если доступен
            if (IsInMatch && CurrentMatch != null && string.IsNullOrEmpty(CurrentMatch.ServerSteamId))
            {
                var steamMatch = ServerSteamIdPattern().Match(line);
                if (steamMatch.Success)
                {
                    CurrentMatch.ServerSteamId = steamMatch.Groups[1].Value;
                }
            }
        }

        if (isInitialScan)
        {
            if (pendingMatch != null && !disconnectedFound)
            {
                HandleMatchConnected(pendingMatch);
            }
        }
    }

    private void PopulateDestinationInfo(DotaMatchInfo matchInfo)
    {
        matchInfo.IngressClusterCode = matchInfo.ClusterCode.ToLowerInvariant();
        if (matchInfo.BackhaulLatencyMs <= 15)
        {
            matchInfo.TargetClusterCode = matchInfo.IngressClusterCode;
            matchInfo.TargetClusterDisplayName = matchInfo.ClusterDisplayName;
            matchInfo.RouteDescription = $"Прямой SDR ({matchInfo.IngressClusterCode.ToUpperInvariant()})";
        }
        else
        {
            string? targetCode = _destinationClusterResolver?.Invoke(matchInfo.IngressClusterCode, matchInfo.BackhaulLatencyMs);
            if (string.IsNullOrEmpty(targetCode))
            {
                targetCode = matchInfo.IngressClusterCode;
            }

            matchInfo.TargetClusterCode = targetCode.ToLowerInvariant();
            var (ru, en) = GetClusterDisplayName(matchInfo.TargetClusterCode);
            matchInfo.TargetClusterDisplayName = LocalizationService.IsRussian ? ru : en;
            matchInfo.RouteDescription = $"SDR: {matchInfo.IngressClusterCode.ToUpperInvariant()} ➔ {matchInfo.TargetClusterCode.ToUpperInvariant()} (+{matchInfo.BackhaulLatencyMs}ms)";
        }
    }

    private void HandleMatchConnected(DotaMatchInfo matchInfo)
    {
        // Если уже подключены к тому же релею, обновляем динамические сетевые параметры
        if (IsInMatch && CurrentMatch != null &&
            CurrentMatch.RelayIp == matchInfo.RelayIp &&
            CurrentMatch.ClusterCode == matchInfo.ClusterCode)
        {
            CurrentMatch.BackhaulLatencyMs = matchInfo.BackhaulLatencyMs;
            CurrentMatch.FrontLatencyMs = matchInfo.FrontLatencyMs;
            CurrentMatch.LastReportedTotalPingMs = matchInfo.LastReportedTotalPingMs;
            CurrentMatch.TargetClusterCode = matchInfo.TargetClusterCode;
            CurrentMatch.TargetClusterDisplayName = matchInfo.TargetClusterDisplayName;
            CurrentMatch.RouteDescription = matchInfo.RouteDescription;
            MatchConnected?.Invoke(CurrentMatch);
            return;
        }

        IsInMatch = true;
        CurrentMatch = matchInfo;
        MatchConnected?.Invoke(matchInfo);
        string target = !string.IsNullOrEmpty(matchInfo.TargetClusterDisplayName) ? matchInfo.TargetClusterDisplayName : matchInfo.ClusterDisplayName;
        StatusChanged?.Invoke($"🟢 В матче: {target} ({matchInfo.RelayIp})");
    }

    private void HandleMatchDisconnected()
    {
        if (!IsInMatch) return;

        IsInMatch = false;
        CurrentMatch = null;
        MatchDisconnected?.Invoke();
        StatusChanged?.Invoke("Вне матча");
    }

    private void CloseStream()
    {
        try
        {
            _fileStream?.Dispose();
        }
        catch { }
        finally
        {
            _fileStream = null;
        }
    }

    /// <summary>
    /// Автоматический поиск пути к console.log Dota 2
    /// </summary>
    public static string? FindDotaConsoleLog()
    {
        // 1. Поиск через активный процесс dota2.exe (мгновенно и 100% точно)
        try
        {
            var processes = Process.GetProcessesByName("dota2");
            if (processes.Length > 0)
            {
                var p = processes[0];
                var mainModulePath = p.MainModule?.FileName;
                foreach (var proc in processes) proc.Dispose();

                if (!string.IsNullOrEmpty(mainModulePath))
                {
                    // Путь: <SteamLibrary>\steamapps\common\dota 2 beta\game\bin\win64\dota2.exe
                    // Идем вверх до папки game, затем \dota\console.log
                    var binDir = Path.GetDirectoryName(mainModulePath);
                    if (binDir != null)
                    {
                        var win64Parent = Directory.GetParent(binDir); // bin
                        var binParent = win64Parent?.Parent;          // game
                        if (binParent != null)
                        {
                            var logPath = Path.Combine(binParent.FullName, "dota", "console.log");
                            return logPath;
                        }
                    }
                }
            }
        }
        catch { }

        // 2. Поиск через реестр Steam и libraryfolders.vdf
        var steamDirs = GetSteamInstallDirectories();
        foreach (var steamDir in steamDirs)
        {
            var logPath = Path.Combine(steamDir, "steamapps", "common", "dota 2 beta", "game", "dota", "console.log");
            if (File.Exists(logPath))
            {
                return logPath;
            }
        }

        // 3. Проверка стандартных путей по всем дискам
        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                .Select(d => d.RootDirectory.FullName);

            foreach (var drive in drives)
            {
                string[] candidates =
                [
                    Path.Combine(drive, @"Steam\steamapps\common\dota 2 beta\game\dota\console.log"),
                    Path.Combine(drive, @"SteamLibrary\steamapps\common\dota 2 beta\game\dota\console.log"),
                    Path.Combine(drive, @"Games\Steam\steamapps\common\dota 2 beta\game\dota\console.log"),
                    Path.Combine(drive, @"Program Files (x86)\Steam\steamapps\common\dota 2 beta\game\dota\console.log"),
                    Path.Combine(drive, @"Program Files\Steam\steamapps\common\dota 2 beta\game\dota\console.log")
                ];

                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Извлекает пути всех библиотек Steam из реестра Windows и libraryfolders.vdf
    /// </summary>
    public static List<string> GetSteamInstallDirectories()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Проверяем HKCU
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                if (key?.GetValue("SteamPath") is string steamPath && Directory.Exists(steamPath))
                {
                    result.Add(steamPath.Replace('/', '\\'));
                }
            }

            // Проверяем HKLM (32 и 64 бит)
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam") ??
                             Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam"))
            {
                if (key?.GetValue("InstallPath") is string installPath && Directory.Exists(installPath))
                {
                    result.Add(installPath.Replace('/', '\\'));
                }
            }
        }
        catch { }

        // Добавляем библиотеки из libraryfolders.vdf
        var libFolders = new List<string>(result);
        foreach (var mainDir in libFolders)
        {
            var vdfPath = Path.Combine(mainDir, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdfPath))
            {
                try
                {
                    var lines = File.ReadAllLines(vdfPath);
                    var pathRegex = new Regex(@"""path""\s+""([^""]+)""", RegexOptions.IgnoreCase);
                    foreach (var line in lines)
                    {
                        var m = pathRegex.Match(line);
                        if (m.Success)
                        {
                            var rawPath = m.Groups[1].Value.Replace(@"\\", @"\");
                            if (Directory.Exists(rawPath))
                            {
                                result.Add(rawPath);
                            }
                        }
                    }
                }
                catch { }
            }
        }

        return result.ToList();
    }

    /// <summary>
    /// Возвращает локализованное название кластера по его коду (например: "sto2" -> "Стокгольм (Швеция)")
    /// </summary>
    public static (string Ru, string En) GetClusterDisplayName(string clusterCode)
    {
        if (string.IsNullOrWhiteSpace(clusterCode))
            return ("Неизвестный сервер", "Unknown Server");

        string cleanCode = clusterCode.Trim().ToLowerInvariant();

        if (ClusterNames.TryGetValue(cleanCode, out var directName))
            return directName;

        // Удаляем цифры на конце (например, "sto2" -> "sto")
        string alphaOnly = Regex.Replace(cleanCode, @"\d+", "");
        if (ClusterNames.TryGetValue(alphaOnly, out var alphaName))
            return alphaName;

        string upper = cleanCode.ToUpperInvariant();
        return ($"Сервер Valve ({upper})", $"Valve Server ({upper})");
    }

    public static string GuessClusterCodeFromIp(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return "valve";
        if (ip.StartsWith("155.133.252.") || ip.StartsWith("185.25.182.")) return "sto2";
        if (ip.StartsWith("162.254.198.")) return "sto";
        if (ip.StartsWith("155.133.226.") || ip.StartsWith("162.254.197.")) return "fra";
        if (ip.StartsWith("155.133.242.") || ip.StartsWith("146.66.155.")) return "vie";
        if (ip.StartsWith("155.133.230.")) return "waw";
        if (ip.StartsWith("155.133.248.")) return "ams";
        if (ip.StartsWith("162.254.192.") || ip.StartsWith("162.254.193.")) return "iad";
        if (ip.StartsWith("162.254.194.")) return "ord";
        if (ip.StartsWith("162.254.195.")) return "lax";
        if (ip.StartsWith("162.254.199.")) return "par";
        if (ip.StartsWith("155.133.246.")) return "lhr";
        if (ip.StartsWith("155.133.244.")) return "mad";
        if (ip.StartsWith("155.133.238.")) return "hel";
        if (ip.StartsWith("103.28.54.")) return "sgp";
        if (ip.StartsWith("155.133.239.")) return "dxb";
        if (ip.StartsWith("155.133.245.")) return "tyo";
        if (ip.StartsWith("155.133.234.")) return "syd";
        return "valve";
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            _isDisposed = true;
            _pollTimer.Dispose();
            _cs2Tracker.Dispose();
            _deadlockTracker.Dispose();
            CloseStream();
        }
        GC.SuppressFinalize(this);
    }
}
