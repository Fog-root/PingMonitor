using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using DotaPingMonitor.Models;
using Microsoft.Win32;

namespace DotaPingMonitor.Services;

/// <summary>
/// Адаптер отслеживания активных соревновательных матчей Deadlock через консольный лог Source 2.
/// 100% VAC-безопасно: пассивный опрос console.log (требуется параметр -condebug или con_logfile console.log).
/// </summary>
public partial class DeadlockMatchTrackerService : IGameMatchTracker
{
    private readonly System.Timers.Timer _pollTimer;
    private readonly object _syncLock = new();

    private string? _cachedLogPath;
    private FileStream? _fileStream;
    private long _lastPosition = 0;
    private bool _isInitialized = false;
    private bool _isDisposed = false;
    private DateTime _lastProcessCheckTime = DateTime.MinValue;
    private bool _wasGameRunning = false;
    private DateTime _gameStartTime = DateTime.MinValue;
    private int _lastPid = 0;
    private Func<string, int, string?>? _destinationClusterResolver;

    public string ActiveGameId => "deadlock";
    public string ActiveGameName => "Deadlock";

    public bool IsInMatch { get; private set; }
    public bool IsRunningWithoutLog { get; private set; }
    public DotaMatchInfo? CurrentMatch { get; private set; }
    public string? LogFilePath => _cachedLogPath;

    public event Action<DotaMatchInfo>? MatchConnected;
    public event Action? MatchDisconnected;
    public event Action<string>? StatusChanged;
    public event Action<bool>? LoggingStatusChanged;

    [GeneratedRegex(@"(?:Selecting|Swapping primary to)\s+(?<cluster>[a-zA-Z0-9_-]+)(?:#\d+)?\s+\((?<ip>[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+):(?<port>[0-9]+)\)\s+as\s+primary(?:.*?(?:Ping\s*=\s*(?<total>\d+)\s*=\s*(?<front>\d+)\+(?<interior>\d+)))?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RelayPattern();

    [GeneratedRegex(@"Primary router:\s+(?<cluster>[a-zA-Z0-9_-]+)(?:#\d+)?\s+\((?<ip>[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+):(?<port>[0-9]+)\).*?Ping\s*=\s*(?<front>\d+)\+(?<back>\d+)=(?<total>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PrimaryRouterPattern();

    [GeneratedRegex(@"(?:Disconnecting|Disconnected)\s+from\s+server|NETWORK_DISCONNECT_", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DisconnectPattern();

    [GeneratedRegex(@"CL:\s+Connected to '\[A:1:(\d+:\d+)\]:\d+'", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ServerSteamIdPattern();

    public DeadlockMatchTrackerService()
    {
        _pollTimer = new System.Timers.Timer(400);
        _pollTimer.AutoReset = false;
        _pollTimer.Elapsed += (_, _) => OnPollTimerTick();
    }

    public void SetGame(string gameId)
    {
        // Специализированный адаптер для Deadlock
    }

    public void SetDestinationResolver(Func<string, int, string?>? resolver)
    {
        _destinationClusterResolver = resolver;
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

    public void TriggerPollNow()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            _pollTimer.Stop();
            _pollTimer.Start();
        }
    }

    private int GetCurrentDeadlockPid()
    {
        string[] candidateNames = ["project8", "deadlock"];
        foreach (var name in candidateNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                if (processes.Length > 0)
                {
                    int pid = processes[0].Id;
                    try { _gameStartTime = processes[0].StartTime.ToUniversalTime(); } catch { }
                    foreach (var p in processes) p.Dispose();
                    return pid;
                }
            }
            catch { }
        }
        return 0;
    }

    private void OnPollTimerTick()
    {
        try
        {
            if (_isDisposed) return;

            var now = DateTime.UtcNow;
            if ((now - _lastProcessCheckTime).TotalSeconds >= 1.5)
            {
                _lastProcessCheckTime = now;
                int currentPid = GetCurrentDeadlockPid();
                bool isRunning = currentPid > 0;

                if (!isRunning && _wasGameRunning)
                {
                    _wasGameRunning = false;
                    _lastPid = 0;
                    CloseStream();
                    _lastPosition = 0;
                    _isInitialized = false;
                    if (IsInMatch)
                    {
                        HandleMatchDisconnected();
                    }
                }
                else if (isRunning && (!_wasGameRunning || currentPid != _lastPid))
                {
                    _wasGameRunning = true;
                    _lastPid = currentPid;
                    CloseStream();
                    _lastPosition = 0;
                    _isInitialized = false;
                    _cachedLogPath = FindDeadlockConsoleLog();
                }
            }

            if (string.IsNullOrEmpty(_cachedLogPath) || !File.Exists(_cachedLogPath))
            {
                _cachedLogPath = FindDeadlockConsoleLog();
                if (string.IsNullOrEmpty(_cachedLogPath))
                {
                    return;
                }
            }

            if (_wasGameRunning)
            {
                bool stale = false;
                if (!string.IsNullOrEmpty(_cachedLogPath) && File.Exists(_cachedLogPath))
                {
                    try
                    {
                        var fi = new FileInfo(_cachedLogPath);
                        stale = _gameStartTime > DateTime.MinValue && (fi.LastWriteTimeUtc < _gameStartTime);
                    }
                    catch { }
                }
                else
                {
                    stale = true;
                }

                if (stale != IsRunningWithoutLog)
                {
                    IsRunningWithoutLog = stale;
                    LoggingStatusChanged?.Invoke(stale);
                }
            }
            else if (IsRunningWithoutLog)
            {
                IsRunningWithoutLog = false;
                LoggingStatusChanged?.Invoke(false);
            }

            ProcessLogUpdates();
        }
        catch { }
        finally
        {
            lock (_syncLock)
            {
                if (!_isDisposed)
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
            if (!fileInfo.Exists) return;

            long currentLength = fileInfo.Length;
            if (currentLength < _lastPosition)
            {
                _lastPosition = 0;
                CloseStream();
            }

            if (!_isInitialized)
            {
                _fileStream?.Dispose();
                _fileStream = new FileStream(_cachedLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                long initialTail = Math.Min(65536, currentLength);
                _lastPosition = currentLength - initialTail;
                _fileStream.Seek(_lastPosition, SeekOrigin.Begin);

                using var reader = new StreamReader(_fileStream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
                var initialLines = new List<string>();
                string? l;
                while ((l = reader.ReadLine()) != null)
                {
                    initialLines.Add(l);
                }
                _lastPosition = _fileStream.Position;
                _isInitialized = true;

                if (initialLines.Count > 0)
                {
                    ParseLines(initialLines, isInitialScan: true);
                }
                return;
            }

            if (currentLength == _lastPosition) return;

            if (_fileStream == null)
            {
                _fileStream = new FileStream(_cachedLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                _fileStream.Seek(_lastPosition, SeekOrigin.Begin);
            }

            var newLines = new List<string>();
            using (var reader = new StreamReader(_fileStream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    newLines.Add(line);
                }
                _lastPosition = _fileStream.Position;
            }

            if (newLines.Count > 0)
            {
                ParseLines(newLines, isInitialScan: false);
            }
        }
        catch { }
    }

    private void ParseLines(List<string> lines, bool isInitialScan)
    {
        DotaMatchInfo? pendingMatch = null;
        bool disconnectedFound = false;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var relayMatch = RelayPattern().Match(line);
            if (relayMatch.Success)
            {
                string cluster = relayMatch.Groups["cluster"].Value;
                string ip = relayMatch.Groups["ip"].Value;
                int port = int.TryParse(relayMatch.Groups["port"].Value, out int p) ? p : 27015;

                int totalPing = 0;
                int frontPing = 0;
                int backhaulPing = 0;

                if (relayMatch.Groups["total"].Success)
                {
                    _ = int.TryParse(relayMatch.Groups["total"].Value, out totalPing);
                    _ = int.TryParse(relayMatch.Groups["front"].Value, out frontPing);
                    _ = int.TryParse(relayMatch.Groups["interior"].Value, out backhaulPing);
                }

                var (ruName, enName) = DotaMatchTrackerService.GetClusterDisplayName(cluster);
                var match = new DotaMatchInfo
                {
                    GameId = "deadlock",
                    GameName = "Deadlock",
                    IsConnected = true,
                    ClusterCode = cluster,
                    ClusterDisplayName = LocalizationService.IsRussian ? ruName : enName,
                    RelayIp = ip,
                    RelayPort = port,
                    ConnectedAt = DateTime.Now,
                    FrontLatencyMs = frontPing,
                    BackhaulLatencyMs = backhaulPing,
                    LastReportedTotalPingMs = totalPing,
                    RawLogLine = line
                };

                PopulateDestinationInfo(match);

                if (isInitialScan)
                {
                    pendingMatch = match;
                    disconnectedFound = false;
                }
                else
                {
                    HandleMatchConnected(match);
                }
                continue;
            }

            var routerMatch = PrimaryRouterPattern().Match(line);
            if (routerMatch.Success)
            {
                string cluster = routerMatch.Groups["cluster"].Value;
                string ip = routerMatch.Groups["ip"].Value;
                int port = int.TryParse(routerMatch.Groups["port"].Value, out int p) ? p : 27015;
                _ = int.TryParse(routerMatch.Groups["total"].Value, out int total);
                _ = int.TryParse(routerMatch.Groups["front"].Value, out int front);
                _ = int.TryParse(routerMatch.Groups["back"].Value, out int back);

                var (ruName, enName) = DotaMatchTrackerService.GetClusterDisplayName(cluster);
                var match = new DotaMatchInfo
                {
                    GameId = "deadlock",
                    GameName = "Deadlock",
                    IsConnected = true,
                    ClusterCode = cluster,
                    ClusterDisplayName = LocalizationService.IsRussian ? ruName : enName,
                    RelayIp = ip,
                    RelayPort = port,
                    ConnectedAt = DateTime.Now,
                    FrontLatencyMs = front,
                    BackhaulLatencyMs = back,
                    LastReportedTotalPingMs = total,
                    RawLogLine = line
                };

                PopulateDestinationInfo(match);

                if (isInitialScan)
                {
                    pendingMatch = match;
                    disconnectedFound = false;
                }
                else
                {
                    HandleMatchConnected(match);
                }
                continue;
            }

            if (DisconnectPattern().IsMatch(line))
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

            if (IsInMatch && CurrentMatch != null && string.IsNullOrEmpty(CurrentMatch.ServerSteamId))
            {
                var steamMatch = ServerSteamIdPattern().Match(line);
                if (steamMatch.Success)
                {
                    CurrentMatch.ServerSteamId = steamMatch.Groups[1].Value;
                }
            }
        }

        if (isInitialScan && pendingMatch != null && !disconnectedFound)
        {
            HandleMatchConnected(pendingMatch);
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
            var (ru, en) = DotaMatchTrackerService.GetClusterDisplayName(matchInfo.TargetClusterCode);
            matchInfo.TargetClusterDisplayName = LocalizationService.IsRussian ? ru : en;
            matchInfo.RouteDescription = $"SDR: {matchInfo.IngressClusterCode.ToUpperInvariant()} ➔ {matchInfo.TargetClusterCode.ToUpperInvariant()} (+{matchInfo.BackhaulLatencyMs}ms)";
        }
    }

    private void HandleMatchConnected(DotaMatchInfo matchInfo)
    {
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
        StatusChanged?.Invoke($"🟢 В матче Deadlock: {target} ({matchInfo.RelayIp})");
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
        try { _fileStream?.Dispose(); } catch { }
        _fileStream = null;
    }

    public static string? FindDeadlockConsoleLog()
    {
        string[] processNames = ["project8", "deadlock"];
        foreach (var procName in processNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(procName);
                if (processes.Length > 0)
                {
                    var p = processes[0];
                    var mainModulePath = p.MainModule?.FileName;
                    foreach (var proc in processes) proc.Dispose();

                    if (!string.IsNullOrEmpty(mainModulePath))
                    {
                        var binDir = Path.GetDirectoryName(mainModulePath);
                        if (binDir != null)
                        {
                            var win64Parent = Directory.GetParent(binDir);
                            var binParent = win64Parent?.Parent;
                            if (binParent != null)
                            {
                                var logPath = Path.Combine(binParent.FullName, "citadel", "console.log");
                                if (File.Exists(logPath)) return logPath;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        var steamDirs = DotaMatchTrackerService.GetSteamInstallDirectories();
        foreach (var steamDir in steamDirs)
        {
            var logPath = Path.Combine(steamDir, "steamapps", "common", "Deadlock", "game", "citadel", "console.log");
            if (File.Exists(logPath)) return logPath;

            var logPath2 = Path.Combine(steamDir, "steamapps", "common", "project8", "game", "citadel", "console.log");
            if (File.Exists(logPath2)) return logPath2;
        }

        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                .Select(d => d.RootDirectory.FullName);

            foreach (var drive in drives)
            {
                string[] candidates =
                [
                    Path.Combine(drive, @"Steam\steamapps\common\Deadlock\game\citadel\console.log"),
                    Path.Combine(drive, @"SteamLibrary\steamapps\common\Deadlock\game\citadel\console.log"),
                    Path.Combine(drive, @"Steam\steamapps\common\project8\game\citadel\console.log"),
                    Path.Combine(drive, @"SteamLibrary\steamapps\common\project8\game\citadel\console.log")
                ];

                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        catch { }

        return null;
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _pollTimer.Dispose();
            CloseStream();
        }
        GC.SuppressFinalize(this);
    }
}
