using System;
using System.Diagnostics;
using System.Linq;
using WeiTimer.Interop;

namespace WeiTimer.Services;

/// <summary>
/// Process presence + window focus checks. Two independent, cheap questions:
///   - is the game process running at all?
///   - is the game's window currently focused?
///
/// On Linux the Python original had a third answer, "unknown" (no focus IPC
/// available on some compositors), which made the watcher fall through to full
/// polling. On Windows GetForegroundWindow is always available, so both checks
/// here are deterministic bools -- there is no "unknown" state to model.
/// </summary>
public static class GameFocusService
{
    public static bool IsProcessRunning(string processName)
    {
        var candidates = Process.GetProcessesByName(processName);
        try
        {
            return candidates.Length > 0;
        }
        finally
        {
            foreach (var p in candidates)
                p.Dispose();
        }
    }

    /// <summary>True if the foreground window belongs to the named process (and,
    /// as a secondary sanity check mirroring the Python title-substring match,
    /// its title contains the configured substring when the title is non-empty).</summary>
    public static bool IsGameFocused(string processName, string titleSubstring)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return false;

        NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundPid);
        if (foregroundPid == 0)
            return false;

        var candidates = Process.GetProcessesByName(processName);
        bool matched;
        try
        {
            matched = candidates.Any(p => p.Id == foregroundPid);
        }
        finally
        {
            foreach (var p in candidates)
                p.Dispose();
        }
        if (!matched)
            return false;

        var title = NativeMethods.GetWindowTitle(foreground);
        if (string.IsNullOrEmpty(title))
            return true; // matched by PID; window just has no title text

        return title.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Restores foreground focus to the game's main window. Needed after
    /// the region-picker overlay steals focus for calibration -- many games pause
    /// or drop to a loading/splash screen while unfocused, so without this,
    /// calibration silently ends up hashing that splash screen instead of the
    /// live timer. Returns false if the process/window couldn't be found.</summary>
    public static bool TryActivateGame(string processName)
    {
        var candidates = Process.GetProcessesByName(processName);
        try
        {
            var withWindow = candidates.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
            return withWindow is not null && NativeMethods.SetForegroundWindow(withWindow.MainWindowHandle);
        }
        finally
        {
            foreach (var p in candidates)
                p.Dispose();
        }
    }
}
