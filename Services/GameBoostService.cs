using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace DotaPingMonitor.Services;

/// <summary>
/// VAC-безопасный сервис оптимизации приоритета игрового процесса (Game Boost Mode).
/// 100% VAC-безопасно: использует только легитимные WinAPI SetPriorityClass ядра Windows.
/// Никаких инъекций DLL или модификации памяти игры!
/// </summary>
public class GameBoostService
{
    private static readonly HashSet<string> KnownGameProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "dota2", "cs2", "deadlock", "project8", "tslgame", "r5apex", "fortniteclient-win64-shipping",
        "worldoftanks", "wot", "rustclient", "valorant-win64-shipping"
    };

    private static readonly HashSet<string> BackgroundThrottledProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "opera", "brave", "qbittorrent", "utorrent", "discord", "telegram"
    };

    private readonly Dictionary<int, ProcessPriorityClass> _originalPriorities = new();
    private int _boostedPid = 0;
    private bool _isBoostActive = false;

    public bool IsBoostActive => _isBoostActive;
    public string BoostedProcessName { get; private set; } = string.Empty;

    public event Action<bool, string>? BoostStateChanged;

    /// <summary>
    /// Активирует Game Boost для активной игры: переводит игру в High Priority,
    /// и снижает приоритет тяжелых фоновых приложений.
    /// </summary>
    public async Task<(bool Success, string Message)> EnableBoostAsync(int preferredPid = 0)
    {
        return await Task.Run(() =>
        {
            try
            {
                int targetPid = preferredPid;
                string targetName = string.Empty;

                if (targetPid <= 0)
                {
                    // Ищем запущенную игру
                    foreach (var name in KnownGameProcessNames)
                    {
                        var procs = Process.GetProcessesByName(name);
                        if (procs.Length > 0)
                        {
                            targetPid = procs[0].Id;
                            targetName = procs[0].ProcessName;
                            foreach (var p in procs) p.Dispose();
                            break;
                        }
                    }
                }
                else
                {
                    try
                    {
                        using var p = Process.GetProcessById(targetPid);
                        targetName = p.ProcessName;
                    }
                    catch { }
                }

                if (targetPid <= 0)
                {
                    bool isRu = LocalizationService.IsRussian;
                    return (false, isRu ? "Не найдено запущенных поддерживаемых игр" : "No running supported games found");
                }

                // 1. Повышаем приоритет игры до High
                using (var gameProc = Process.GetProcessById(targetPid))
                {
                    if (gameProc.PriorityClass != ProcessPriorityClass.High &&
                        gameProc.PriorityClass != ProcessPriorityClass.RealTime)
                    {
                        _originalPriorities[targetPid] = gameProc.PriorityClass;
                        gameProc.PriorityClass = ProcessPriorityClass.High;
                    }
                }

                // 2. Снижаем приоритет фоновых программ до BelowNormal
                foreach (var bgName in BackgroundThrottledProcesses)
                {
                    try
                    {
                        var bgProcs = Process.GetProcessesByName(bgName);
                        foreach (var bp in bgProcs)
                        {
                            try
                            {
                                if (bp.PriorityClass == ProcessPriorityClass.Normal)
                                {
                                    _originalPriorities[bp.Id] = bp.PriorityClass;
                                    bp.PriorityClass = ProcessPriorityClass.BelowNormal;
                                }
                            }
                            catch { }
                            finally
                            {
                                bp.Dispose();
                            }
                        }
                    }
                    catch { }
                }

                _boostedPid = targetPid;
                BoostedProcessName = targetName;
                _isBoostActive = true;

                bool isRussian = LocalizationService.IsRussian;
                string msg = isRussian
                    ? $"✓ Режим Game Boost включен: {targetName} установлен высокий приоритет (High)"
                    : $"✓ Game Boost active: {targetName} set to High Priority";

                BoostStateChanged?.Invoke(true, msg);
                return (true, msg);
            }
            catch (Exception ex)
            {
                return (false, $"Ошибка активации Game Boost: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Отключает Game Boost и восстанавливает системные приоритеты процессов.
    /// </summary>
    public async Task<(bool Success, string Message)> DisableBoostAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                foreach (var kvp in _originalPriorities)
                {
                    try
                    {
                        using var p = Process.GetProcessById(kvp.Key);
                        p.PriorityClass = kvp.Value;
                    }
                    catch { }
                }
                _originalPriorities.Clear();
                _boostedPid = 0;
                BoostedProcessName = string.Empty;
                _isBoostActive = false;

                bool isRussian = LocalizationService.IsRussian;
                string msg = isRussian
                    ? "Game Boost выключен. Приоритеты процессов восстановлены."
                    : "Game Boost disabled. Process priorities restored.";

                BoostStateChanged?.Invoke(false, msg);
                return (true, msg);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        });
    }
}
