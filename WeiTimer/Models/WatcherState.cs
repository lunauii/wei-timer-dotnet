namespace WeiTimer.Models;

/// <summary>Mirrors watcher.py's WatcherState enum. GameUnfocused is retained for UI
/// status-label parity even though Windows focus detection is always deterministic
/// (no "unknown" tri-state like the Linux focus-IPC fallback).</summary>
public enum WatcherState
{
    Disabled,
    TimerActive,
    GameNotRunning,
    GameUnfocused,
    Watching,
}

public sealed class WatcherStatus
{
    public required WatcherState State { get; init; }
    public double? TimerRemainingSeconds { get; init; }
}
