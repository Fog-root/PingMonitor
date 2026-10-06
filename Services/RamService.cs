using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace DotaPingMonitor.Services;

/// <summary>
/// Сервис легковесного мониторинга оперативной памяти (ОЗУ / RAM %).
/// Использует прямой системный Win32 вызов GlobalMemoryStatusEx для нулевой нагрузки на систему.
/// </summary>
public class RamService : IDisposable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    private readonly object _lock = new();
    private Timer? _timer;
    private bool _isRunning;
    private bool _isDisposed;

    public event Action<int, double, double>? RamUpdated;

    public bool IsRunning => _isRunning;

    public int CurrentRamPercent { get; private set; }
    public double CurrentUsedGb { get; private set; }
    public double CurrentTotalGb { get; private set; }

    public void Start()
    {
        lock (_lock)
        {
            if (_isRunning || _isDisposed) return;
            _isRunning = true;

            // Сразу производим первый замер
            SampleRam();

            _timer = new Timer(_ => SampleRam(), null, 1500, 1500);
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
        }
    }

    private void SampleRam()
    {
        if (_isDisposed) return;

        try
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                int percent = (int)Math.Clamp(mem.dwMemoryLoad, 0, 100);
                double totalGb = Math.Round((double)mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0), 1);
                ulong usedBytes = mem.ullTotalPhys > mem.ullAvailPhys ? mem.ullTotalPhys - mem.ullAvailPhys : 0;
                double usedGb = Math.Round((double)usedBytes / (1024.0 * 1024.0 * 1024.0), 1);

                CurrentRamPercent = percent;
                CurrentUsedGb = usedGb;
                CurrentTotalGb = totalGb;

                RamUpdated?.Invoke(percent, usedGb, totalGb);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RamService] Error querying GlobalMemoryStatusEx: {ex.Message}");
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
        }
    }
}
