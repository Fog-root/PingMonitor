using System.Diagnostics;
using Microsoft.Win32;

namespace DotaPingMonitor.Services;

/// <summary>
/// Сервис управления автозагрузкой приложения в Windows (Диспетчер задач -> вкладка Автозагрузка).
/// </summary>
public static class StartupService
{
    public const string AppName = "Ping Monitoring";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Проверяет, включена ли автозагрузка приложения в реестре Windows.
    /// </summary>
    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            if (key == null) return false;

            object? val = key.GetValue(AppName);
            return val != null && !string.IsNullOrWhiteSpace(val.ToString());
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Включает или отключает автозагрузку приложения в реестре Windows.
    /// Запись автоматически отображается в Диспетчере задач на вкладке «Автозагрузка».
    /// </summary>
    public static bool SetStartupEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return false;

            if (enable)
            {
                string exePath = Environment.ProcessPath ?? 
                                 Process.GetCurrentProcess().MainModule?.FileName ?? 
                                 string.Empty;

                if (string.IsNullOrEmpty(exePath)) return false;

                // Записываем путь с ключом --startup для бесшумного запуска в системный трей
                key.SetValue(AppName, $"\"{exePath}\" --startup");
                return true;
            }
            else
            {
                key.DeleteValue(AppName, false);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Проверяет, запущено ли приложение через автозагрузку в свёрнутом режиме.
    /// </summary>
    public static bool IsStartupLaunch(string[]? args = null)
    {
        args ??= Environment.GetCommandLineArgs();
        return args.Any(a => 
            a.Equals("--startup", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-silent", StringComparison.OrdinalIgnoreCase));
    }
}
