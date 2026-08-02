using System;
using System.IO;
using System.Text.Json;
using WeiTimer.Models;

namespace WeiTimer.Services;

/// <summary>Single JSON file persistence for AppConfig, under %APPDATA%\WeiTimer.
/// Mirrors config.py's load_config/save_config: corrupt or unreadable config
/// never crashes the app, it just starts fresh; writes are atomic via a
/// temp-file-then-rename.</summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string ConfigDir()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(baseDir, "WeiTimer");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string ConfigPath() => Path.Combine(ConfigDir(), "config.json");

    public static AppConfig Load()
    {
        var path = ConfigPath();
        if (!File.Exists(path))
            return new AppConfig();

        try
        {
            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            return cfg ?? new AppConfig();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        var path = ConfigPath();
        var tmpPath = path + ".tmp";
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, path, overwrite: true);
    }
}
