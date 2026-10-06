using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace DotaPingMonitor.Services;

/// <summary>
/// Сервис мониторинга нагрузки центрального процессора (CPU %).
/// Использует счетчик "% Processor Utility" (Processor Information), в точности как Диспетчер задач Windows (Task Manager).
/// В случае недоступности счетчика автоматически откатывается на низкоуровневый системный вызов GetSystemTimes.
/// </summary>
public class CpuService : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;

        public ulong Value => ((ulong)dwHighDateTime << 32) | dwLowDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    private readonly object _lock = new();
    private Timer? _timer;
    private bool _isRunning;
    private bool _isDisposed;

    // Переменные для легковесного замера через GetSystemTimes
    private FILETIME _prevIdle;
    private FILETIME _prevKernel;
    private FILETIME _prevUser;
    private bool _hasPrevSample;

    public event Action<int>? CpuUpdated;

    public bool IsRunning => _isRunning;

    public int CurrentCpu { get; private set; }

    public void Start()
    {
        lock (_lock)
        {
            if (_isRunning || _isDisposed) return;
            _isRunning = true;
            _hasPrevSample = false;

            // Захватываем начальную точку отсчета через нативный вызов GetSystemTimes (0% overhead на System/ntoskrnl)
            if (GetSystemTimes(out _prevIdle, out _prevKernel, out _prevUser))
            {
                _hasPrevSample = true;
            }

            _timer = new Timer(OnCalculateCpu, null, 1500, 1500);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning) return;
            _isRunning = false;
            _hasPrevSample = false;

            _timer?.Dispose();
            _timer = null;
        }
    }

    private void OnCalculateCpu(object? state)
    {
        if (!_isRunning || _isDisposed) return;

        // Прямой легковесный опрос Win32 GetSystemTimes: 0 мс задержки, 0 аллокаций, отсутствие вызовов WMI/PDH
        CalculateCpuNative();
    }

    private void CalculateCpuNative()
    {
        if (!GetSystemTimes(out var currIdle, out var currKernel, out var currUser))
            return;

        if (!_hasPrevSample)
        {
            _prevIdle = currIdle;
            _prevKernel = currKernel;
            _prevUser = currUser;
            _hasPrevSample = true;
            return;
        }

        ulong deltaIdle = currIdle.Value - _prevIdle.Value;
        ulong deltaKernel = currKernel.Value - _prevKernel.Value;
        ulong deltaUser = currUser.Value - _prevUser.Value;

        _prevIdle = currIdle;
        _prevKernel = currKernel;
        _prevUser = currUser;

        ulong totalSystem = deltaKernel + deltaUser;
        if (totalSystem <= 0) return;

        ulong busyTime = totalSystem >= deltaIdle ? totalSystem - deltaIdle : 0;
        double cpuPercentDouble = (busyTime * 100.0) / totalSystem;
        int cpuPercent = Math.Clamp((int)Math.Round(cpuPercentDouble), 0, 100);
        CurrentCpu = cpuPercent;

        CpuUpdated?.Invoke(cpuPercent);
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
        }
        GC.SuppressFinalize(this);
    }
}
