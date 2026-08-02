# Wei Timer (.NET port)

![Wei Timer logo](WeiTimer/Assets/wei-timer.png)

A WPF-based independent training timer detector and daily carat tracker for Umamusume, for Windows.

This is the Windows-native .NET port of [wei-timer](https://github.com/lunauii/wei-timer), the original Linux (GTK4/libadwaita) app.

> [!NOTE]
> A complete port of wei-timer's feature set, verified end-to-end on a real Windows machine — timer calibration, autorun detection, carat OCR, sound, notifications, tray, single-instance activation, config persistence, and the installer have all been tested working. Still early (v0.1.0); see [Known limitations](#known-limitations) for what's untested.

## What it does

- Watches for the in-game autorun timer box to appear on screen (using perceptual image hashing) and starts a 50-minute countdown when it does, notifying you and playing a sound when it ends.
- Watching is gated behind whether Umamusume is even running, and whether its window is focused, so there's near-zero overhead the rest of the time.
- Lets you drag-select the carat-count region on the results screen after a run, OCRs the number, and tracks a running daily total against a configurable cap (100 normally, 200 during a 2x drop event).
- Minimizes to the system tray instead of closing, with a live countdown in the tray menu.

## Installation

**Installer (recommended)**

Download `WeiTimerSetup-x.y.z.exe` from the [latest release](https://github.com/lunauii/wei-timer-dotnet/releases/latest) and run it. It's a per-user install (no admin/UAC prompt), with an optional "launch at Windows startup" checkbox.

The installer and exe aren't code-signed, so Windows SmartScreen will likely flag them as "unrecognized" on first run — click **More info → Run anyway**. This is purely a reputation-tracking artifact of being unsigned freeware, not a sign anything's wrong; there's no signing certificate for this project.

**Build from source**

```
git clone https://github.com/lunauii/wei-timer-dotnet.git
cd wei-timer-dotnet
dotnet build WeiTimer.sln
dotnet run --project WeiTimer
```
Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (Windows Desktop workload) and Windows 10 1903+ / Windows 11 (needed for `Windows.Media.Ocr`, which also requires the Windows OCR language pack — present by default on most systems).

## First-run setup

1. **Timer region calibration**: click "Calibrate timer region…" in the app while the autorun timer's container is visible, then select something static like the Time Left area. Recalibrate any time you move or resize the game window, or change monitors.
2. **Process and window matching**: the default process name is `UmamusumePrettyDerby`. If your setup differs, find the real process name in Task Manager's Details tab and update `GameProcessName` in the config file to match. Config lives at `%APPDATA%\WeiTimer\config.json`.

## Tray icon

Closing the window doesn't quit Wei Timer, it minimizes to the system tray, and the watcher keeps running in the background. The first time you do this, a notification explains it; after that it's silent.

To bring the window back, click **Open** in the tray menu, or relaunch the app (it re-presents the existing window rather than starting a second instance). A live countdown also shows at the top of the tray menu whenever a timer's running.

## Known limitations

- **No game-input automation anywhere**. This is *not* an auto-runner script. Wei Timer only serves to automate setting a timer and counting your daily carats.
- **A drag that spans two monitors mid-drag is untested**. Starting a calibration/carat-capture selection on one monitor and releasing it on another, where the two have different DPI scaling, hasn't been verified. Starting and finishing the drag on the *same* monitor works correctly regardless of that monitor's scaling — confirmed on a real mixed-DPI (125%/100%) dual-monitor setup — since the capture rect is built from physical cursor coordinates, not a DPI-scaled conversion.
- Please file an issue if something doesn't work as described — this is still an early release.

## Project layout

```
WeiTimer/
  App.xaml(.cs)        - application entry point, single-instance handling
  Models/               - AppConfig, CaratLog, WatcherState
  Services/             - ConfigStore, PerceptualHash (autorun-box detection),
                          GameFocusService, ScreenCaptureService, OcrService,
                          SoundService, NotificationService, TrayIconService,
                          Watcher (core state machine), SingleInstanceGuard
  Interop/              - Win32 P/Invoke (NativeMethods)
  Views/                - MainWindow, RegionPickerWindow, CaratConfirmDialog
  Assets/               - bundled sounds, app icon
installer/
  WeiTimer.iss          - Inno Setup script for the Windows installer
```

## Attribution and AI Notice

Default notification SFX by [Universfield](https://pixabay.com/users/universfield-28281460/?utm_source=link-attribution&utm_medium=referral&utm_campaign=music&utm_content=493469) from [Pixabay](https://pixabay.com/sound-effects//?utm_source=link-attribution&utm_medium=referral&utm_campaign=music&utm_content=493469) \
Metal pipe SFX: origin unconfirmed \
Harikitte Ikou SFX: sourced from Umamusume

This port's development was carried out with Claude Code (Anthropic).

## License

[MIT](LICENSE.md)
