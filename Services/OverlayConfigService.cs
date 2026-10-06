using System.IO;
using System.Text.Json;
using DotaPingMonitor.Models;

namespace DotaPingMonitor.Services;

public class OverlayConfigService
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private OverlayConfig? _cachedConfig;

    public OverlayConfigService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "DotaPingMonitor");

        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _filePath = Path.Combine(dir, "overlay_config.json");
    }

    public OverlayConfig Load()
    {
        lock (_lock)
        {
            if (_cachedConfig != null)
            {
                return _cachedConfig;
            }

            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    _cachedConfig = JsonSerializer.Deserialize<OverlayConfig>(json) ?? new OverlayConfig();
                    if (_cachedConfig.BaseOpacity <= 0)
                    {
                        _cachedConfig.BaseOpacity = _cachedConfig.Opacity > 0 ? _cachedConfig.Opacity : 0.92;
                    }
                    if (_cachedConfig.Opacity <= 0)
                    {
                        _cachedConfig.Opacity = _cachedConfig.BaseOpacity;
                    }
                    if (string.IsNullOrWhiteSpace(_cachedConfig.LayoutPreset))
                    {
                        _cachedConfig.LayoutPreset = "classic_bar";
                    }
                    return _cachedConfig;
                }
            }
            catch
            {
                // Fallback to defaults
            }

            _cachedConfig = new OverlayConfig();
            return _cachedConfig;
        }
    }

    public void Save(OverlayConfig config)
    {
        lock (_lock)
        {
            _cachedConfig = config;
            try
            {
                var json = JsonSerializer.Serialize(
                    config,
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // Ignore write errors
            }
        }
    }
}
