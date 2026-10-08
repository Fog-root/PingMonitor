namespace DotaPingMonitor.Models;

public class OverlayConfig
{
    public double Left { get; set; } = 40;

    public double Top { get; set; } = 40;

    public bool IsLocked { get; set; } = false;

    public bool IsEnabled { get; set; } = false;

    public double Opacity { get; set; } = 0.92;

    public double BaseOpacity { get; set; } = 0.92;

    public string LayoutPreset { get; set; } = "classic_bar";

    public double Scale { get; set; } = 1.0;

    public bool ShowSparkline { get; set; } = true;

    public bool ShowFps { get; set; } = true;

    public bool ShowCpu { get; set; } = true;

    public bool ShowGpu { get; set; } = true;

    public bool ShowRam { get; set; } = true;

    public string Theme { get; set; } = "Linear";

    public bool IsOptimizedMode { get; set; } = false;

    public bool AutoDetectDotaMatch { get; set; } = true;

    public bool AutoHideWhenNoGame { get; set; } = false;

    public string Language { get; set; } = "";

    public string SelectedServer { get; set; } = "";

    public string SelectedGame { get; set; } = "dota2";

    public string SelectedServerDota2 { get; set; } = "Россия — Stockholm (Valve)";

    public string SelectedServerCs2 { get; set; } = "CS2: Stockholm (Sweden)";

    public string SelectedServerTf2 { get; set; } = "TF2: Europe — Stockholm (Valve)";

    public string SelectedServerFortnite { get; set; } = "Fortnite: eu-north-1 (Stockholm)";

    public Dictionary<string, string> SelectedServersByGame { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string GetSelectedServerForGame(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId)) return SelectedServer;
        if (SelectedServersByGame.TryGetValue(gameId, out var savedServer) && !string.IsNullOrWhiteSpace(savedServer))
        {
            return savedServer;
        }

        return gameId.ToLowerInvariant() switch
        {
            "cs2" => SelectedServerCs2,
            "tf2" => SelectedServerTf2,
            "fortnite" => SelectedServerFortnite,
            "dota2" => SelectedServerDota2,
            _ => SelectedServer
        };
    }

    public void SetSelectedServerForGame(string gameId, string serverName)
    {
        if (string.IsNullOrWhiteSpace(gameId)) return;
        SelectedServersByGame[gameId] = serverName;
        SelectedServer = serverName;

        switch (gameId.ToLowerInvariant())
        {
            case "cs2":
                SelectedServerCs2 = serverName;
                break;
            case "tf2":
                SelectedServerTf2 = serverName;
                break;
            case "fortnite":
                SelectedServerFortnite = serverName;
                break;
            case "dota2":
                SelectedServerDota2 = serverName;
                break;
        }
    }

    // Настраиваемые глобальные хоткеи
    public HotkeyBinding HotkeyOverlay { get; set; } = new(true, true, false, false, 0x4F, "Ctrl+Shift+O");

    public HotkeyBinding HotkeyLock { get; set; } = new(true, true, false, false, 0x4C, "Ctrl+Shift+L");

    public HotkeyBinding HotkeySparkline { get; set; } = new(true, true, false, false, 0x47, "Ctrl+Shift+G");

    public HotkeyBinding HotkeyFps { get; set; } = new(true, true, false, false, 0x46, "Ctrl+Shift+F");

    public HotkeyBinding HotkeyCpu { get; set; } = new(true, true, false, false, 0x43, "Ctrl+Shift+C");

    public HotkeyBinding HotkeyGpu { get; set; } = new(true, true, false, false, 0x55, "Ctrl+Shift+U");
 
    public HotkeyBinding HotkeyRam { get; set; } = new(true, true, false, false, 0x52, "Ctrl+Shift+R");
}
