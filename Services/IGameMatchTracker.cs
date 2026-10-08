using DotaPingMonitor.Models;

namespace DotaPingMonitor.Services;

/// <summary>
/// Универсальный интерфейс трекера активного матча для игр Valve (Dota 2, CS2, Deadlock).
/// 100% VAC-безопасно (пассивный стриминг console.log).
/// </summary>
public interface IGameMatchTracker : IDisposable
{
    string ActiveGameId { get; }
    string ActiveGameName { get; }
    bool IsInMatch { get; }
    bool IsRunningWithoutLog { get; }
    DotaMatchInfo? CurrentMatch { get; }
    string? LogFilePath { get; }

    event Action<DotaMatchInfo>? MatchConnected;
    event Action? MatchDisconnected;
    event Action<string>? StatusChanged;
    event Action<bool>? LoggingStatusChanged;

    void SetGame(string gameId);
    void SetDestinationResolver(Func<string, int, string?>? resolver);
    void Start();
    void Stop();
    void TriggerPollNow();
}
