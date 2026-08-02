using System;
using System.IO;
using System.Text.Json.Serialization;

namespace WeiTimer.Models;

public static class BundledSounds
{
    public const string DefaultSoundKey = "pop";

    public static readonly (string Key, string FileName, string Label)[] All =
    {
        ("pop", "notif.mp3", "generic pop effect"),
        ("pipe", "metal_pipe.mp3", "metal pipe"),
        ("harikitte ikou", "harikitte_ikou.mp3", "harikitte ikou!"),
    };

    public static string? FileNameFor(string key) =>
        Array.Find(All, s => s.Key == key).FileName;
}

/// <summary>Persisted app state. Mirrors the Python Config dataclass, minus the
/// Linux-compositor-only banner_dismissed_for field which has no Windows equivalent.</summary>
public sealed class AppConfig
{
    public bool WatcherEnabled { get; set; } = true;

    // "X,Y WxH" geometry string, plus the calibrated perceptual-hash reference.
    public string? TimerRegionGeometry { get; set; }
    public string? TimerRegionReferenceHash { get; set; }
    public int? TimerRegionMaxDistance { get; set; }

    // Process.ProcessName strips ".exe", so this is stored without extension.
    public string GameProcessName { get; set; } = "UmamusumePrettyDerby";
    public string GameWindowTitleSubstring { get; set; } = "Umamusume";

    public string SoundChoice { get; set; } = BundledSounds.DefaultSoundKey;
    public string? CustomSoundPath { get; set; }

    public bool SoundEnabled { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;

    public bool IsDoubleDropEvent { get; set; }

    public CaratLog CaratLog { get; set; } = new();

    public double SoundVolume { get; set; } = 0.66;
    public bool SeenMinimizeNotice { get; set; }
    public int WindowWidth { get; set; } = 720;
    public int WindowHeight { get; set; } = 640;

    [JsonIgnore]
    public int CaratCap => IsDoubleDropEvent ? 200 : 100;

    /// <summary>Resolves the currently selected sound to a playable file path, or null
    /// if nothing is resolvable (e.g. "custom" chosen but no path set yet).</summary>
    public string? ResolvedSoundPath()
    {
        if (SoundChoice == "custom")
            return CustomSoundPath;

        var fileName = BundledSounds.FileNameFor(SoundChoice);
        if (fileName is null)
            return null;

        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", fileName);
        return File.Exists(path) ? path : null;
    }
}
