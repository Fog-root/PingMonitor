using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace DotaPingMonitor.Services;

/// <summary>
/// Высокоточный, легковесный и VAC-безопасный сервис мониторинга FPS на базе отраслевого стандарта PresentMon / ETW.
/// Перехватывает события вывода кадра (Present) DirectX 10/11/12 (DXGI), DirectX 9 (D3D9) и Vulkan/OpenGL (DxgKrnl).
/// Использует математическую модель скользящего окна 1000 мс (Circular Buffer) по фактической временной шкале рендера
/// без потерь квантования и задержек буферизации ядра.
/// </summary>
public class FpsService : IDisposable
{
    private const string EtwSessionName = "DotaPingMonitor_FpsTrace";
    private static readonly Guid DxgiProviderGuid = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9ProviderGuid = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private static readonly Guid DxgkProviderGuid = new("802EC45A-1E99-4B83-9920-87C98277BA9D");

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetCompositionTimingInfo(IntPtr hwnd, IntPtr timingInfo);

    private static readonly HashSet<string> KnownGameProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "dota2", "cs2", "csgo", "deadlock", "project8", "valorant", "VALORANT-Win64-Shipping",
        "league of legends", "overwatch", "apex", "r5apex", "worldoftanks", "wot", "tslgame",
        "rustclient", "gta5", "fivem", "witcher3", "cyberpunk2077", "eldenring"
    };

    private static readonly HashSet<string> ExcludedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost",
        "ApplicationFrameHost", "SystemSettings", "LockApp", "Taskmgr", "taskhostw",
        "steam", "steamwebhelper", "discord", "telegram", "spotify",
        "chrome", "firefox", "msedge", "opera", "brave",
        "devenv", "code", "powershell", "cmd", "wt", "conhost", "obs64",
        "snippingtool", "ScreenClippingHost"
    };

    private readonly object _lock = new();
    private TraceEventSession? _session;
    private Thread? _etwThread;
    private System.Threading.Timer? _calcTimer;
    private bool _isRunning;
    private bool _isDisposed;
    private bool _useDwmFallback;

    private readonly int _ownPid;
    private volatile int _targetGamePid;
    private volatile string _targetGameName = string.Empty;
    private volatile bool _isGameFocused;

    // Временные метки и скользящее окно кадров (Ring Buffer)
    private readonly object _framesLock = new();
    private readonly Queue<long> _frameTimestamps = new(2048);
    private readonly long _minDeltaQpc = (long)(0.0005 * Stopwatch.Frequency); // 0.5 мс дедупликация (до 2000 FPS)

    private long _lastFrameRecordedQpc;
    private long _lastDxgiOrD3D9Qpc;
    private int _lastKnownGameFps;
    private int _lastReportedFps;

    // Буфер DWM для определения герцовки экрана
    private IntPtr _dwmBuffer = IntPtr.Zero;

    public event Action<int>? FpsUpdated;
    public event Action<bool, string>? GameFocusChanged;

    public bool IsGameFocused => _isGameFocused;
    public string TargetGameName => _targetGameName;

    public bool IsRunning => _isRunning;

    public FpsService()
    {
        _ownPid = Environment.ProcessId;
        InitDwmBuffer();
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_isRunning || _isDisposed) return;
            _isRunning = true;
            _lastFrameRecordedQpc = 0;
            _lastDxgiOrD3D9Qpc = 0;
            _lastKnownGameFps = 0;
            _lastReportedFps = 0;
            _targetGamePid = 0;
            _targetGameName = string.Empty;
            _isGameFocused = false;

            lock (_framesLock)
            {
                _frameTimestamps.Clear();
            }

            _useDwmFallback = false;
            StartEtwSession();

            // Интервал выборки 333 мс (3 раза в секунду) согласно спецификации UI
            _calcTimer = new System.Threading.Timer(OnCalculateFps, null, 333, 333);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning) return;
            _isRunning = false;

            _calcTimer?.Dispose();
            _calcTimer = null;

            StopEtwSession();
            CleanupDwmBuffer();
        }
    }

    private bool StartEtwSession()
    {
        lock (_lock)
        {
            if (_session != null) return true;
            if (!_isRunning || _isDisposed) return false;

            try
            {
                // Закрываем любую подвисшую сессию от предыдущих запусков
                try
                {
                    var existing = TraceEventSession.GetActiveSession(EtwSessionName);
                    existing?.Stop();
                    existing?.Dispose();
                }
                catch { }

                _session = new TraceEventSession(EtwSessionName, TraceEventSessionOptions.Create)
                {
                    BufferSizeMB = 1,
                    CpuSampleIntervalMSec = 0
                };

                // Включаем DXGI (DirectX 10/11/12)
                _session.EnableProvider(DxgiProviderGuid, TraceEventLevel.Informational, 0);
                // Включаем D3D9 (DirectX 9)
                _session.EnableProvider(D3D9ProviderGuid, TraceEventLevel.Informational, 0);
                // Включаем DxgKrnl (Vulkan / OpenGL fallback)
                _session.EnableProvider(DxgkProviderGuid, TraceEventLevel.Informational, 0);

                _session.Source.Dynamic.All += OnTraceEvent;

                _etwThread = new Thread(() =>
                {
                    try
                    {
                        _session?.Source.Process();
                    }
                    catch
                    {
                        // Сессия завершена
                    }
                })
                {
                    IsBackground = true,
                    Priority = ThreadPriority.BelowNormal,
                    Name = "FpsService_EtwWorker"
                };

                _etwThread.Start();
                return true;
            }
            catch
            {
                StopEtwSession();
                _useDwmFallback = true;
                return false;
            }
        }
    }

    private void StopEtwSession()
    {
        lock (_lock)
        {
            try
            {
                _session?.Stop();
                _session?.Dispose();
            }
            catch
            {
                // Ignore
            }
            finally
            {
                _session = null;
                _etwThread = null;
            }
        }
    }

    private static int FindRunningGamePid()
    {
        foreach (var name in KnownGameProcessNames)
        {
            try
            {
                var procs = Process.GetProcessesByName(name);
                if (procs.Length > 0)
                {
                    int id = procs[0].Id;
                    foreach (var p in procs) p.Dispose();
                    return id;
                }
            }
            catch { }
        }
        return 0;
    }

    private static bool ProcessHasExited(int pid)
    {
        if (pid <= 0) return true;
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.HasExited;
        }
        catch
        {
            return true;
        }
    }

    private void OnTraceEvent(TraceEvent data)
    {
        int targetPid = _targetGamePid;
        if (targetPid <= 0) return;

        // Фильтруем события строго для активного игрового процесса
        if (data.ProcessID != targetPid) return;

        // Если игра сейчас свернута/не в фокусе, не учитываем урезанные фоновые кадры
        if (!_isGameFocused) return;

        int eventId = (int)data.ID;
#pragma warning disable CS0618
        long qpc = data.TimeStampQPC;
#pragma warning restore CS0618
        if (qpc <= 0) qpc = Stopwatch.GetTimestamp();

        bool isFrame = false;

        // 1. DirectX 10/11/12 (DXGI: Event 42 Present_Start, Event 178 IDXGISwapChain_Present_Start)
        if (data.ProviderGuid == DxgiProviderGuid)
        {
            if (eventId == 42 || eventId == 178)
            {
                isFrame = true;
                Volatile.Write(ref _lastDxgiOrD3D9Qpc, qpc);
            }
        }
        // 2. DirectX 9 (D3D9: Event 1 Present_Start)
        else if (data.ProviderGuid == D3D9ProviderGuid)
        {
            if (eventId == 1)
            {
                isFrame = true;
                Volatile.Write(ref _lastDxgiOrD3D9Qpc, qpc);
            }
        }
        // 3. Vulkan и OpenGL (DxgKrnl: Event 184 D3DKMTPresent)
        // Учитывается ТОЛЬКО если игра не генерирует события DXGI/D3D9 (защита от двойного учета в DX11/12)
        else if (data.ProviderGuid == DxgkProviderGuid)
        {
            if (eventId == 184)
            {
                long lastDx = Volatile.Read(ref _lastDxgiOrD3D9Qpc);
                if (qpc - lastDx > Stopwatch.Frequency)
                {
                    isFrame = true;
                }
            }
        }

        if (!isFrame) return;

        // Дедупликация: исключаем суб-события одного и того же вызова Present (дельта < 0.5 мс)
        long prev = Volatile.Read(ref _lastFrameRecordedQpc);
        if (qpc - prev < _minDeltaQpc)
        {
            return;
        }
        Volatile.Write(ref _lastFrameRecordedQpc, qpc);

        lock (_framesLock)
        {
            _frameTimestamps.Enqueue(qpc);
            long cutoff2s = qpc - (Stopwatch.Frequency * 2);
            while (_frameTimestamps.Count > 0 && _frameTimestamps.Peek() < cutoff2s)
            {
                _frameTimestamps.Dequeue();
            }
        }
    }

    private void OnCalculateFps(object? state)
    {
        if (!_isRunning) return;

        UpdateTargetProcess();

        // Сбрасываем буферы ETW ядра Windows для предотвращения накапливания задержек
        try
        {
            _session?.Flush();
        }
        catch { }

        int refreshRate = GetMonitorRefreshRate();

        if (_useDwmFallback || _session == null)
        {
            ReportFps(refreshRate);
            return;
        }

        // 1. Если игра не запущена — отображаем герцовку рабочего стола
        if (_targetGamePid <= 0)
        {
            _lastKnownGameFps = 0;
            ReportFps(refreshRate);
            return;
        }

        // 2. Если игра запущена, но потеряла фокус / свернута:
        // Удерживаем последний актуальный замер FPS (защита от занижения до 30-35 FPS из-за фонового троттлинга)
        if (!_isGameFocused)
        {
            int heldFps = _lastKnownGameFps > 0 ? _lastKnownGameFps : refreshRate;
            ReportFps(heldFps);
            return;
        }

        // 3. Игра активна и в фокусе — расчет по скользящему окну 1000 мс
        long nowQpc = Stopwatch.GetTimestamp();
        long latestQpc = 0;
        long oldestQpc = 0;
        int frameCount = 0;

        lock (_framesLock)
        {
            if (_frameTimestamps.Count > 0)
            {
                latestQpc = _frameTimestamps.Last();
                long cutoff1s = latestQpc - Stopwatch.Frequency;

                // Скользящее окно: количество кадров за 1000 мс до крайнего зарегистрированного кадра
                while (_frameTimestamps.Count > 0 && _frameTimestamps.Peek() < cutoff1s)
                {
                    _frameTimestamps.Dequeue();
                }

                frameCount = _frameTimestamps.Count;
                if (frameCount > 0)
                {
                    oldestQpc = _frameTimestamps.Peek();
                }
            }
        }

        // Если за последние 1.5 секунды не было ни одного нового кадра (загрузочный экран, пауза):
        if (latestQpc == 0 || (nowQpc - latestQpc) > (long)(1.5 * Stopwatch.Frequency))
        {
            int displayFps = _lastKnownGameFps > 0 ? _lastKnownGameFps : refreshRate;
            ReportFps(displayFps);
            return;
        }

        int calculatedFps;
        double windowSpanSec = (double)(latestQpc - oldestQpc) / Stopwatch.Frequency;

        // Если данных еще меньше 1.0 сек (момент старта отслеживания / разгона буфера):
        if (windowSpanSec < 0.95 && windowSpanSec >= 0.20 && frameCount > 1)
        {
            calculatedFps = (int)Math.Round((frameCount - 1) / windowSpanSec);
        }
        else
        {
            // Полное 1000 мс скользящее окно: FPS = точное количество кадров
            calculatedFps = frameCount;
        }

        calculatedFps = Math.Max(1, calculatedFps);
        _lastKnownGameFps = calculatedFps;
        ReportFps(calculatedFps);
    }

    private void ReportFps(int fps)
    {
        if (_lastReportedFps == fps) return;
        _lastReportedFps = fps;
        FpsUpdated?.Invoke(fps);
    }

    private IntPtr _lastFgHwnd = IntPtr.Zero;
    private uint _lastFgPid = 0;

    private void UpdateTargetProcess()
    {
        bool prevFocused = _isGameFocused;
        string prevName = _targetGameName;

        try
        {
            IntPtr fgHwnd = GetForegroundWindow();
            uint fgPid = 0;
            if (fgHwnd != IntPtr.Zero)
            {
                GetWindowThreadProcessId(fgHwnd, out fgPid);
            }

            // 1. Если фокус находится на нашем собственном оверлее или главном окне — не сбрасываем игру!
            if (fgPid == _ownPid)
            {
                if (_targetGamePid > 0 && !ProcessHasExited(_targetGamePid))
                {
                    _isGameFocused = true;
                    return;
                }
            }

            string procName = string.Empty;
            if (fgPid > 0 && fgPid != _ownPid)
            {
                try
                {
                    using var p = Process.GetProcessById((int)fgPid);
                    procName = p.ProcessName;
                }
                catch { }
            }

            // 2. Если передний план — известная игра (dota2, cs2 и т.д.):
            if (KnownGameProcessNames.Contains(procName))
            {
                if (_targetGamePid != (int)fgPid)
                {
                    _targetGamePid = (int)fgPid;
                    _targetGameName = procName;
                    lock (_framesLock)
                    {
                        _frameTimestamps.Clear();
                    }
                    _lastFrameRecordedQpc = 0;
                }
                _isGameFocused = true;
                _lastFgHwnd = fgHwnd;
                _lastFgPid = fgPid;
                return;
            }

            // 3. Если передний план — системный рабочий стол или лаунчер (Discord, Telegram, Chrome, Steam):
            if (string.IsNullOrEmpty(procName) || ExcludedProcessNames.Contains(procName))
            {
                // Проверяем, запущена ли целевая игра в фоне
                if (_targetGamePid > 0 && !ProcessHasExited(_targetGamePid))
                {
                    _isGameFocused = false; // игра в фоне, удерживаем последний FPS
                    return;
                }

                int runningGamePid = FindRunningGamePid();
                if (runningGamePid > 0)
                {
                    _targetGamePid = runningGamePid;
                    _isGameFocused = false;
                    return;
                }

                // Игр нет — рабочий стол
                _targetGamePid = 0;
                _targetGameName = string.Empty;
                _isGameFocused = false;
                return;
            }

            // 4. Передний план — стороннее приложение (возможно, другая игра)
            if (_targetGamePid != (int)fgPid)
            {
                _targetGamePid = (int)fgPid;
                _targetGameName = procName;
                lock (_framesLock)
                {
                    _frameTimestamps.Clear();
                }
                _lastFrameRecordedQpc = 0;
            }

            _isGameFocused = true;
            _lastFgHwnd = fgHwnd;
            _lastFgPid = fgPid;
        }
        catch
        {
            // Безопасный перехват возможных сбоев WinAPI
        }
        finally
        {
            if (prevFocused != _isGameFocused || !string.Equals(prevName, _targetGameName, StringComparison.OrdinalIgnoreCase))
            {
                GameFocusChanged?.Invoke(_isGameFocused, _targetGameName);
            }
        }
    }

    private void InitDwmBuffer()
    {
        try
        {
            if (_dwmBuffer == IntPtr.Zero)
            {
                _dwmBuffer = Marshal.AllocHGlobal(292);
                Marshal.WriteInt32(_dwmBuffer, 292);
            }
        }
        catch { }
    }

    private void CleanupDwmBuffer()
    {
        if (_dwmBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_dwmBuffer);
            _dwmBuffer = IntPtr.Zero;
        }
    }

    private int GetMonitorRefreshRate()
    {
        try
        {
            if (_dwmBuffer == IntPtr.Zero)
            {
                InitDwmBuffer();
            }

            if (_dwmBuffer != IntPtr.Zero)
            {
                Marshal.WriteInt32(_dwmBuffer, 292);
                int hr = DwmGetCompositionTimingInfo(IntPtr.Zero, _dwmBuffer);
                if (hr == 0)
                {
                    uint num = (uint)Marshal.ReadInt32(_dwmBuffer, 4);
                    uint den = (uint)Marshal.ReadInt32(_dwmBuffer, 8);
                    if (den > 0)
                    {
                        int rate = (int)Math.Round((double)num / den);
                        if (rate >= 30 && rate <= 500)
                        {
                            return rate;
                        }
                    }
                }
            }
        }
        catch { }

        return 60;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Stop();
        }
        GC.SuppressFinalize(this);
    }
}
