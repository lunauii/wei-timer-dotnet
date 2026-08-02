using System;
using System.Threading;
using WeiTimer.Models;

namespace WeiTimer.Services;

/// <summary>
/// Watcher state machine.
///
/// Priority order, top short-circuits everything below it:
///   1. manual override switch OFF -> fully idle, no polling at all
///   2. timer already active -> sleep until deadline, no game/focus checks
///   3. game process not running -> cheap process-only poll
///   4. game running, not focused -> cheap poll
///   5. game running + focused -> the only state that actually captures
///      frames and hashes them, ~1s cadence
///
/// Unlike the Linux original, tier 4's focus check is always deterministic on
/// Windows (GetForegroundWindow never returns "unknown"), so there's no
/// degraded-mode branch to model here.
///
/// Runs on a dedicated background thread, the direct analog of the Python
/// version's threading.Thread + Event.wait(timeout). Callbacks fire on that
/// thread -- callers touching WPF/WinForms objects from them must marshal back
/// to the UI thread (e.g. via Dispatcher.BeginInvoke).
/// </summary>
public sealed class Watcher : IDisposable
{
    public const double AutorunDurationSeconds = 50 * 60;

    private const double IdlePollIntervalSec = 5.0;      // game not running -- launches take far longer than this anyway, no responsiveness need
    private const double UnfocusedPollIntervalSec = 1.0; // game running but unfocused -- a focus check is cheap (no capture/hash), so poll it as fast as
                                                          // the watching tier itself, or refocusing the game can sit unnoticed for up to IdlePollIntervalSec
    private const double FocusedPollIntervalSec = 1.0;   // only while actually capturing frames

    private readonly AppConfig _config;
    private readonly Action _onTimerComplete;
    private readonly Action<WatcherStatus>? _onStatusChange;

    private Thread? _thread;
    private readonly ManualResetEventSlim _stop = new(false);
    private DateTime? _timerDeadline; // UTC, or null

    public Watcher(AppConfig config, Action onTimerComplete, Action<WatcherStatus>? onStatusChange = null)
    {
        _config = config;
        _onTimerComplete = onTimerComplete;
        _onStatusChange = onStatusChange;
    }

    public void Start()
    {
        if (_thread is not null)
            return;
        _stop.Reset();
        _thread = new Thread(RunLoop) { IsBackground = true, Name = "WeiTimer.Watcher" };
        _thread.Start();
    }

    public void Stop()
    {
        _stop.Set();
        _thread?.Join(TimeSpan.FromSeconds(5));
        _thread = null;
    }

    /// <summary>Manually arm the timer (also called internally when the box is
    /// detected). Exposed so a "start manual timer" UI action, and debug
    /// completion, can reuse the exact same countdown/notification path.</summary>
    public void ArmTimer(double durationSeconds = AutorunDurationSeconds) =>
        _timerDeadline = DateTime.UtcNow.AddSeconds(durationSeconds);

    public void CancelTimer() => _timerDeadline = null;

    private void EmitStatus(WatcherState state)
    {
        if (_onStatusChange is null)
            return;
        double? remaining = null;
        if (_timerDeadline is { } deadline)
            remaining = Math.Max(0.0, (deadline - DateTime.UtcNow).TotalSeconds);
        _onStatusChange(new WatcherStatus { State = state, TimerRemainingSeconds = remaining });
    }

    private void RunLoop()
    {
        ReferenceHash? reference = null;
        if (_config.TimerRegionReferenceHash is { } hash && _config.TimerRegionMaxDistance is { } maxDistance)
            reference = ReferenceHash.FromHexString(hash, maxDistance);

        while (!_stop.IsSet)
        {
            // Tier 1: manual override.
            if (!_config.WatcherEnabled)
            {
                EmitStatus(WatcherState.Disabled);
                Sleep(IdlePollIntervalSec);
                continue;
            }

            // Tier 2: timer already running -- ignore everything else.
            if (_timerDeadline is { } deadline)
            {
                var remaining = (deadline - DateTime.UtcNow).TotalSeconds;
                if (remaining <= 0)
                {
                    _timerDeadline = null;
                    _onTimerComplete();
                    EmitStatus(WatcherState.GameNotRunning); // re-evaluated next loop
                    continue;
                }
                EmitStatus(WatcherState.TimerActive);
                Sleep(Math.Min(1.0, remaining));
                continue;
            }

            // Tier 3: is the game even running?
            if (!GameFocusService.IsProcessRunning(_config.GameProcessName))
            {
                EmitStatus(WatcherState.GameNotRunning);
                Sleep(IdlePollIntervalSec);
                continue;
            }

            // Tier 4: is it focused?
            if (!GameFocusService.IsGameFocused(_config.GameProcessName, _config.GameWindowTitleSubstring))
            {
                EmitStatus(WatcherState.GameUnfocused);
                Sleep(UnfocusedPollIntervalSec);
                continue;
            }

            // Tier 5: actually watching. Only reachable state that captures frames.
            EmitStatus(WatcherState.Watching);
            if (reference is not null && _config.TimerRegionGeometry is { } geometry)
            {
                try
                {
                    using var frame = ScreenCaptureService.CaptureRegion(geometry);
                    if (reference.IsTimerPresent(frame))
                        ArmTimer();
                }
                catch
                {
                    // Capture can fail transiently (e.g. the region briefly
                    // scrolled off-screen); don't let one bad frame kill the
                    // whole watcher thread.
                }
            }
            Sleep(FocusedPollIntervalSec);
        }
    }

    private void Sleep(double seconds) => _stop.Wait(TimeSpan.FromSeconds(seconds));

    public void Dispose()
    {
        Stop();
        _stop.Dispose();
    }
}
