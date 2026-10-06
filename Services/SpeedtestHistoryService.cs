using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DotaPingMonitor.Models;

namespace DotaPingMonitor.Services;

public class SpeedtestHistoryService
{
    private readonly string _filePath;
    private readonly object _lock = new();

    public SpeedtestHistoryService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "DotaPingMonitor");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        _filePath = Path.Combine(dir, "speedtest_history.json");
    }

    public List<SpeedtestResult> Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var list = JsonSerializer.Deserialize<List<SpeedtestResult>>(json);
                    return list ?? new List<SpeedtestResult>();
                }
            }
            catch
            {
                // Fallback
            }
            return new List<SpeedtestResult>();
        }
    }

    public void Save(List<SpeedtestResult> history)
    {
        lock (_lock)
        {
            try
            {
                string json = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // Fallback
            }
        }
    }

    public List<SpeedtestResult> AddResult(SpeedtestResult result, int maxCount = 10)
    {
        var list = Load();
        list.Insert(0, result);
        if (list.Count > maxCount)
        {
            list = list.GetRange(0, maxCount);
        }
        Save(list);
        return list;
    }

    public void Clear()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
            }
            catch { }
        }
    }
}
