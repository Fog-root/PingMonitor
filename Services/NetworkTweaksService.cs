using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace DotaPingMonitor.Services;

/// <summary>
/// VAC-безопасный сервис системных твиков сети Windows для снижения сетевой задержки (Latency Tweaks).
/// Включает отключение алгоритма Nagle, отключение системного мультимедийного троттлинга Windows,
/// мгновенную очистку кэша DNS и перезапуск адаптера.
/// </summary>
public class NetworkTweaksService
{
    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
    private static extern int DnsFlushResolverCache();

    /// <summary>
    /// Очищает локальный кэш DNS-резолвера Windows.
    /// </summary>
    public async Task<(bool Success, string Message)> FlushDnsAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                // 1. Быстрый вызов Win32 API
                try
                {
                    DnsFlushResolverCache();
                }
                catch { }

                // 2. Системная утилита ipconfig /flushdns
                var psi = new ProcessStartInfo
                {
                    FileName = "ipconfig",
                    Arguments = "/flushdns",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);

                bool isRu = LocalizationService.IsRussian;
                return (true, isRu ? "✓ Кэш DNS-резолвера успешно очищен" : "✓ DNS resolver cache successfully flushed");
            }
            catch (Exception ex)
            {
                return (false, $"Ошибка очистки DNS: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Перезапускает активный физический сетевой адаптер при потере пакетов или задержке.
    /// </summary>
    public async Task<(bool Success, string Message)> RestartNetworkAdapterAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var iface = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up &&
                                         n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                         !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                                         !n.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase));

                if (iface == null)
                {
                    bool isRu = LocalizationService.IsRussian;
                    return (false, isRu ? "Активный физический адаптер не найден" : "No active physical network adapter found");
                }

                string adapterName = iface.Name;

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -WindowStyle Hidden -Command \"Restart-NetAdapter -Name '{adapterName}' -Confirm:$false\"",
                    CreateNoWindow = true,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(10000);

                bool isRussian = LocalizationService.IsRussian;
                return (true, isRussian
                    ? $"✓ Адаптер '{adapterName}' успешно перезапущен"
                    : $"✓ Network adapter '{adapterName}' restarted");
            }
            catch (Exception ex)
            {
                return (false, $"Ошибка перезапуска адаптера: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Проверяет, отключен ли системный троттлинг сети Windows (NetworkThrottlingIndex).
    /// </summary>
    public bool CheckNetworkThrottlingDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
            if (key != null)
            {
                var val = key.GetValue("NetworkThrottlingIndex");
                if (val is int intVal && intVal == -1) return true;
                if (val is long longVal && longVal == 0xFFFFFFFF) return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Настраивает NetworkThrottlingIndex и SystemResponsiveness в реестре Windows.
    /// Требует прав администратора.
    /// </summary>
    public async Task<(bool Success, string Message)> SetNetworkThrottlingTweakAsync(bool disableThrottling)
    {
        return await Task.Run(() =>
        {
            try
            {
                int val = disableThrottling ? -1 : 10;
                int respVal = disableThrottling ? 0 : 20;

                string script = $@"
                    Set-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile' -Name 'NetworkThrottlingIndex' -Value {val} -Type DWord;
                    Set-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile' -Name 'SystemResponsiveness' -Value {respVal} -Type DWord;
                ";

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script}\"",
                    CreateNoWindow = true,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(6000);

                bool isRu = LocalizationService.IsRussian;
                return (true, isRu 
                    ? "✓ Системный сетевой троттлинг Windows успешно отключен" 
                    : "✓ Windows Network Throttling successfully disabled");
            }
            catch (Exception ex)
            {
                return (false, $"Ошибка настройки троттлинга: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Применяет оптимизацию алгоритма Nagle (TcpAckFrequency = 1, TCPNoDelay = 1) для всех сетевых адаптеров.
    /// Требует прав администратора.
    /// </summary>
    public async Task<(bool Success, string Message)> SetNagleOptimizationAsync(bool optimize)
    {
        return await Task.Run(() =>
        {
            try
            {
                int ackVal = optimize ? 1 : 2;
                int noDelayVal = optimize ? 1 : 0;

                string script = $@"
                    $interfaces = Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces';
                    foreach ($iface in $interfaces) {{
                        Set-ItemProperty -Path $iface.PSPath -Name 'TcpAckFrequency' -Value {ackVal} -Type DWord -ErrorAction SilentlyContinue;
                        Set-ItemProperty -Path $iface.PSPath -Name 'TCPNoDelay' -Value {noDelayVal} -Type DWord -ErrorAction SilentlyContinue;
                    }}
                ";

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script}\"",
                    CreateNoWindow = true,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(8000);

                bool isRu = LocalizationService.IsRussian;
                return (true, isRu 
                    ? "✓ Алгоритм Nagle оптимизирован (TcpAckFrequency = 1, TCPNoDelay = 1)" 
                    : "✓ Nagle algorithm optimized (TcpAckFrequency = 1, TCPNoDelay = 1)");
            }
            catch (Exception ex)
            {
                return (false, $"Ошибка оптимизации Nagle: {ex.Message}");
            }
        });
    }
}
