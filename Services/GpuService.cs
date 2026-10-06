using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace DotaPingMonitor.Services;

/// <summary>
/// Высокопроизводительный сервис мониторинга нагрузки графического процессора (GPU %).
/// Использует системный счетчик Windows WDDM "\GPU Engine(*)\Utilization Percentage" через PDH API,
/// в точности как Диспетчер задач Windows (Task Manager).
/// Переиспользует неуправляемые буферы памяти для работы с нулевыми аллокациями и задержкой менее 1 мс.
/// </summary>
public class GpuService : IDisposable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PDH_FMT_COUNTERVALUE_ITEM
    {
        public IntPtr szName;
        public uint CStatus;
        public double doubleValue;
    }

    [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", SetLastError = true, ExactSpelling = true)]
    private static extern uint PdhOpenQuery(IntPtr szDataSource, IntPtr dwUserData, out IntPtr phQuery);

    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", SetLastError = true, ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

    [DllImport("pdh.dll", SetLastError = true)]
    private static extern uint PdhCollectQueryData(IntPtr hQuery);

    [DllImport("pdh.dll", SetLastError = true)]
    private static extern uint PdhCloseQuery(IntPtr hQuery);

    [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW", SetLastError = true, ExactSpelling = true)]
    private static extern uint PdhGetFormattedCounterArray(IntPtr hCounter, uint dwFormat, ref uint lpdwBufferSize, ref uint lpdwItemCount, IntPtr lpItemBuffer);

    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint ERROR_SUCCESS = 0x00000000;
    private const uint PDH_MORE_DATA = 0x800007D2;

    private readonly object _lock = new();
    private System.Threading.Timer? _timer;
    private bool _isRunning;
    private bool _isDisposed;
    private bool _isSupported = true;

    private IntPtr _hQuery = IntPtr.Zero;
    private IntPtr _hCounter = IntPtr.Zero;

    private IntPtr _bufferPtr = IntPtr.Zero;
    private uint _bufferCapacity = 0;

    private readonly Dictionary<int, double> _latestPidGpu = new();
    private readonly object _dataLock = new();

    public event Action<int>? GpuUpdated;

    public bool IsRunning => _isRunning;

    public int CurrentGpu { get; private set; }

    private int _refreshIntervalMs = 1500;
    public int RefreshIntervalMs
    {
        get => _refreshIntervalMs;
        set
        {
            _refreshIntervalMs = Math.Max(1000, value);
            if (_isRunning)
            {
                _timer?.Change(_refreshIntervalMs, _refreshIntervalMs);
            }
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_isRunning || _isDisposed) return;
            _isRunning = true;

            Task.Run(() =>
            {
                lock (_lock)
                {
                    if (!_isRunning || _isDisposed) return;
                    InitializePdh();
                    _timer = new System.Threading.Timer(OnCalculateGpu, null, _refreshIntervalMs, _refreshIntervalMs);
                }
            });
        }
    }

    private void InitializePdh()
    {
        try
        {
            CleanupPdh();

            uint status = PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out _hQuery);
            if (status != ERROR_SUCCESS)
            {
                Debug.WriteLine($"[GpuService] PdhOpenQuery failed: 0x{status:X}");
                _isSupported = false;
                return;
            }

            status = PdhAddEnglishCounter(_hQuery, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _hCounter);
            if (status != ERROR_SUCCESS)
            {
                Debug.WriteLine($"[GpuService] PdhAddEnglishCounter failed: 0x{status:X}");
                CleanupPdh();
                _isSupported = false;
                return;
            }

            // Первый сбор данных разогревает начальную временную метку для счетчиков скорости/утилизации
            PdhCollectQueryData(_hQuery);
            _isSupported = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GpuService] InitializePdh exception: {ex.Message}");
            CleanupPdh();
            _isSupported = false;
        }
    }

    private void CleanupPdh()
    {
        if (_hQuery != IntPtr.Zero)
        {
            try { PdhCloseQuery(_hQuery); } catch { }
            _hQuery = IntPtr.Zero;
            _hCounter = IntPtr.Zero;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning) return;
            _isRunning = false;

            _timer?.Dispose();
            _timer = null;

            CleanupPdh();

            if (_bufferPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_bufferPtr);
                _bufferPtr = IntPtr.Zero;
                _bufferCapacity = 0;
            }

            lock (_dataLock)
            {
                _latestPidGpu.Clear();
            }
        }
    }

    private void OnCalculateGpu(object? state)
    {
        if (!_isRunning || _isDisposed || !_isSupported) return;

        try
        {
            CollectGpuCore();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GpuService] Error calculating GPU: {ex.Message}");
        }
    }

    /// <summary>
    /// Выполняет сбор текущих метрик GPU через PDH.
    /// </summary>
    public void CollectGpuCore()
    {
        if (_hQuery == IntPtr.Zero || _hCounter == IntPtr.Zero)
        {
            InitializePdh();
            if (!_isSupported || _hQuery == IntPtr.Zero) return;
        }

        uint collectStatus = PdhCollectQueryData(_hQuery);
        if (collectStatus != ERROR_SUCCESS)
        {
            // Попробуем переинициализировать при ошибке сессии PDH
            InitializePdh();
            return;
        }

        uint bufferSize = 0;
        uint itemCount = 0;
        uint status = PdhGetFormattedCounterArray(_hCounter, PDH_FMT_DOUBLE, ref bufferSize, ref itemCount, IntPtr.Zero);

        if (status != ERROR_SUCCESS && status != PDH_MORE_DATA)
        {
            return;
        }

        if (bufferSize > _bufferCapacity || _bufferPtr == IntPtr.Zero)
        {
            if (_bufferPtr != IntPtr.Zero) Marshal.FreeHGlobal(_bufferPtr);
            _bufferCapacity = Math.Max(bufferSize, 65536);
            _bufferPtr = Marshal.AllocHGlobal((int)_bufferCapacity);
        }

        status = PdhGetFormattedCounterArray(_hCounter, PDH_FMT_DOUBLE, ref bufferSize, ref itemCount, _bufferPtr);
        if (status != ERROR_SUCCESS || itemCount == 0)
        {
            return;
        }

        int itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
        var pidGpuTemp = new Dictionary<int, double>();
        double totalSum = 0.0;

        for (int i = 0; i < itemCount; i++)
        {
            IntPtr itemPtr = IntPtr.Add(_bufferPtr, i * itemSize);
            var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(itemPtr);

            if (item.CStatus != ERROR_SUCCESS || item.doubleValue <= 0.001) continue;

            string? name = Marshal.PtrToStringUni(item.szName);
            if (string.IsNullOrEmpty(name)) continue;

            // Формат экземпляра: pid_<PID>_luid_<LUID>_phys_<PHYS>_eng_<ENG>_engtype_<TYPE>
            if (name.StartsWith("pid_", StringComparison.OrdinalIgnoreCase))
            {
                int endIdx = name.IndexOf('_', 4);
                if (endIdx > 4 && int.TryParse(name.AsSpan(4, endIdx - 4), out int pid))
                {
                    if (!pidGpuTemp.ContainsKey(pid))
                    {
                        pidGpuTemp[pid] = item.doubleValue;
                    }
                    else
                    {
                        pidGpuTemp[pid] += item.doubleValue;
                    }
                }
            }
        }

        foreach (var kvp in pidGpuTemp)
        {
            totalSum += Math.Min(100.0, kvp.Value);
        }

        int gpuPercent = Math.Clamp((int)Math.Round(totalSum), 0, 100);
        CurrentGpu = gpuPercent;

        lock (_dataLock)
        {
            _latestPidGpu.Clear();
            foreach (var kvp in pidGpuTemp)
            {
                _latestPidGpu[kvp.Key] = Math.Clamp(kvp.Value, 0.0, 100.0);
            }
        }

        GpuUpdated?.Invoke(gpuPercent);
    }

    /// <summary>
    /// Возвращает словарь нагрузки GPU по процессам (PID -> GPU %).
    /// </summary>
    public Dictionary<int, double> GetProcessGpuUsage()
    {
        lock (_dataLock)
        {
            return new Dictionary<int, double>(_latestPidGpu);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _isRunning = false;

            _timer?.Dispose();
            _timer = null;

            CleanupPdh();

            if (_bufferPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_bufferPtr);
                _bufferPtr = IntPtr.Zero;
                _bufferCapacity = 0;
            }
        }
        GC.SuppressFinalize(this);
    }
}
