using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WeiTimer.Models;
using WeiTimer.Services;
using RadioButton = System.Windows.Controls.RadioButton;

namespace WeiTimer.Views;

/// <summary>
/// Main application window. Mirrors window.py's MainWindow, minus the
/// compatibility banner and "Change capture monitor…" button -- both existed
/// only to explain Linux compositor/portal ambiguity that cannot occur here.
/// </summary>
public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly AppConfig _config;
    private readonly Watcher _watcher;
    private readonly TrayIconService _tray;
    private readonly NotificationService _notifications;
    private readonly SoundService _sound = new();
    private readonly DispatcherTimer _countdownTimer;
    private DispatcherTimer? _previewPollTimer;

    private double? _localDeadlineMirror; // ticked locally between watcher status callbacks
    private bool _suppressEvents;         // guards programmatic control updates from re-saving config

    public MainWindow()
    {
        InitializeComponent();

        _config = ConfigStore.Load();
        Width = _config.WindowWidth;
        Height = _config.WindowHeight;

        _watcher = new Watcher(_config, OnTimerCompleteBackgroundThread, OnStatusChangeBackgroundThread);

        _tray = new TrayIconService(this);
        _tray.OpenRequested += ShowAndActivate;
        _tray.CaptureCaratsRequested += () => { ShowAndActivate(); OnCaptureCaratsClick(this, new RoutedEventArgs()); };
        _tray.CalibrateRequested += () => { ShowAndActivate(); OnCalibrateClick(this, new RoutedEventArgs()); };
        _tray.ForceCompleteRequested += () => { _watcher.CancelTimer(); ApplyTimerComplete(); };
        _tray.ExitRequested += ExitApplication;

        _notifications = new NotificationService(_tray);

        _watcher.Start();

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => TickCountdownDisplay();
        _countdownTimer.Start();

        LoadConfigIntoControls();

        Closing += OnWindowClosing;
    }

    // -- initial control state -------------------------------------------

    private void LoadConfigIntoControls()
    {
        _suppressEvents = true;
        try
        {
            WatcherToggle.IsChecked = _config.WatcherEnabled;
            EventToggle.IsChecked = _config.IsDoubleDropEvent;
            NotifToggle.IsChecked = _config.NotificationsEnabled;
            SoundToggle.IsChecked = _config.SoundEnabled;
            VolumeSlider.Value = _config.SoundVolume * 100;
            CaratTotalLabel.Text = CaratLabelText();

            SoundRadioPop.IsChecked = _config.SoundChoice == "pop";
            SoundRadioPipe.IsChecked = _config.SoundChoice == "pipe";
            SoundRadioHarikitte.IsChecked = _config.SoundChoice == "harikitte ikou";
            SoundRadioCustom.IsChecked = _config.SoundChoice == "custom";
            CustomSoundPathBox.Text = _config.CustomSoundPath ?? string.Empty;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private string CaratLabelText() => $"{_config.CaratLog.TodayTotal()}/{_config.CaratCap}";

    // -- settings handlers -------------------------------------------------

    private void OnWatcherToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        _config.WatcherEnabled = WatcherToggle.IsChecked ?? false;
        ConfigStore.Save(_config);
    }

    private void OnEventToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        _config.IsDoubleDropEvent = EventToggle.IsChecked ?? false;
        ConfigStore.Save(_config);
        CaratTotalLabel.Text = CaratLabelText();
    }

    private void OnNotifToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        _config.NotificationsEnabled = NotifToggle.IsChecked ?? false;
        ConfigStore.Save(_config);
    }

    private void OnSoundToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        _config.SoundEnabled = SoundToggle.IsChecked ?? false;
        ConfigStore.Save(_config);
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        VolumeValueLabel.Text = $"{(int)VolumeSlider.Value}%";
        if (_suppressEvents) return;
        _config.SoundVolume = VolumeSlider.Value / 100.0;
        ConfigStore.Save(_config);
    }

    private void OnSoundChoiceChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        if (sender is not RadioButton { Tag: string key })
            return;
        _config.SoundChoice = key;
        ConfigStore.Save(_config);
    }

    private void OnCustomPathChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents) return;
        _config.CustomSoundPath = string.IsNullOrWhiteSpace(CustomSoundPathBox.Text) ? null : CustomSoundPathBox.Text;
        if (_config.SoundChoice == "custom")
            ConfigStore.Save(_config);
    }

    private void OnBrowseCustomSoundClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a sound file",
            Filter = "Audio files (*.mp3;*.wav;*.ogg)|*.mp3;*.wav;*.ogg|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        SoundRadioCustom.IsChecked = true;       // fires OnSoundChoiceChecked -> saves SoundChoice
        CustomSoundPathBox.Text = dialog.FileName; // fires OnCustomPathChanged -> saves CustomSoundPath
    }

    private void OnSoundPreviewClick(object sender, RoutedEventArgs e)
    {
        if (_sound.IsPlaying)
        {
            _sound.Stop();
            SoundPreviewButton.Content = "▶";
            _previewPollTimer?.Stop();
            _previewPollTimer = null;
            return;
        }

        var played = _sound.Play(_config.ResolvedSoundPath(), _config.SoundVolume);
        if (!played)
        {
            ShowSnackbar("Nothing played", "Check the file exists / a sound backend is installed");
            return;
        }

        SoundPreviewButton.Content = "⏹";
        _previewPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _previewPollTimer.Tick += (_, _) => CheckPreviewFinished();
        _previewPollTimer.Start();
    }

    private void CheckPreviewFinished()
    {
        if (_sound.IsPlaying)
            return;
        SoundPreviewButton.Content = "▶";
        _previewPollTimer?.Stop();
        _previewPollTimer = null;
    }

    private void OnDebugTriggerClick(object sender, RoutedEventArgs e)
    {
        _watcher.CancelTimer();
        ApplyTimerComplete();
    }

    // -- calibration / capture ---------------------------------------------

    private async void OnCalibrateClick(object sender, RoutedEventArgs e)
    {
        var region = RegionPickerWindow.PickRegion();
        if (region is null)
            return;

        var geometry = ScreenCaptureService.ToGeometryString(region.Value);
        _config.TimerRegionGeometry = geometry;
        ConfigStore.Save(_config);

        // The picker overlay just stole focus from the game to let the user
        // drag-select; many games pause or drop to a loading/splash screen
        // while unfocused, so without restoring focus here, every sample below
        // would hash that splash screen instead of the live timer.
        GameFocusService.TryActivateGame(_config.GameProcessName);
        await Task.Delay(500);

        ShowSnackbar("Calibrating", "Keep the autorun timer visible for a few seconds…");

        var samples = new List<Bitmap>();
        for (var i = 0; i < 4; i++)
        {
            await Task.Delay(1000);
            try
            {
                samples.Add(ScreenCaptureService.CaptureRegion(geometry));
            }
            catch (Exception ex) when (ex is ExternalException or ArgumentException)
            {
                // Transient capture failure -- skip this sample.
            }
        }

        if (samples.Count == 0)
        {
            ShowSnackbar("Calibration failed", "No frames captured");
            return;
        }

        var reference = ReferenceHash.Calibrate(samples);
        foreach (var s in samples)
            s.Dispose();

        _config.TimerRegionReferenceHash = reference.ToHexString();
        _config.TimerRegionMaxDistance = reference.MaxDistance;
        ConfigStore.Save(_config);
        ShowSnackbar("Timer region calibrated", "Restart watching (toggle it off/on) to pick up the new region.");
    }

    private async void OnCaptureCaratsClick(object sender, RoutedEventArgs e)
    {
        var region = RegionPickerWindow.PickRegion();
        if (region is null)
            return;

        using var img = ScreenCaptureService.CaptureRegion(region.Value);
        var parsed = await OcrService.ExtractCaratCountAsync(img);

        var dialog = new CaratConfirmDialog(parsed) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ConfirmedAmount is not { } amount)
            return;

        var newTotal = _config.CaratLog.Add(amount);
        ConfigStore.Save(_config);
        CaratTotalLabel.Text = CaratLabelText();

        if (newTotal >= _config.CaratCap)
            ShowSnackbar("Cap reached", $"{newTotal}/{_config.CaratCap} carats today");
    }

    // -- watcher callbacks (fire on the watcher's background thread) -------

    private void OnStatusChangeBackgroundThread(WatcherStatus status) =>
        Dispatcher.BeginInvoke(() => ApplyStatus(status));

    private void OnTimerCompleteBackgroundThread() =>
        Dispatcher.BeginInvoke(ApplyTimerComplete);

    private void ApplyStatus(WatcherStatus status)
    {
        StatusLabel.Text = status.State switch
        {
            WatcherState.Disabled => "Watcher disabled",
            WatcherState.TimerActive => "Autorun timer active",
            WatcherState.GameNotRunning => "Game not running",
            WatcherState.GameUnfocused => "Game running (not focused)",
            WatcherState.Watching => "Watching for autorun…",
            _ => "—",
        };
        StatusDot.Fill = (System.Windows.Media.Brush)FindResource(status.State switch
        {
            WatcherState.GameNotRunning => "SystemFillColorCriticalBrush",
            WatcherState.GameUnfocused => "SystemFillColorCautionBrush",
            WatcherState.Watching => "SystemFillColorSuccessBrush",
            WatcherState.TimerActive => "SystemFillColorSuccessBrush",
            _ => "SystemFillColorSolidNeutralBrush",
        });
        _localDeadlineMirror = status.TimerRemainingSeconds;
    }

    private void ApplyTimerComplete()
    {
        if (_config.NotificationsEnabled)
            _notifications.Send("Autorun finished", "Your 50-minute autorun timer has ended.");
        if (_config.SoundEnabled)
            _sound.Play(_config.ResolvedSoundPath(), _config.SoundVolume);
        ShowSnackbar("Autorun timer finished", string.Empty);
    }

    private void TickCountdownDisplay()
    {
        var remaining = _localDeadlineMirror;
        if (remaining is null)
        {
            CountdownLabel.Text = "—";
            _tray.SetTimerText("not running");
            return;
        }

        var clamped = Math.Max(0.0, remaining.Value);
        var mins = (int)clamped / 60;
        var secs = (int)clamped % 60;
        var text = $"{mins:D2}:{secs:D2}";
        CountdownLabel.Text = text;
        _tray.SetTimerText(text);
        _localDeadlineMirror = Math.Max(0.0, clamped - 1);
    }

    // -- window lifecycle ----------------------------------------------------

    internal void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;

        _config.WindowWidth = (int)Width;
        _config.WindowHeight = (int)Height;
        ConfigStore.Save(_config);

        if (!_config.SeenMinimizeNotice)
        {
            _notifications.Send(
                "Wei Timer is still running!",
                "It's just hiding in the tray... Right-click the tray icon and choose Exit Wei Timer to fully close it.");
            _config.SeenMinimizeNotice = true;
            ConfigStore.Save(_config);
        }

        Hide();
    }

    private void ExitApplication()
    {
        _countdownTimer.Stop();
        _previewPollTimer?.Stop();
        _watcher.Stop();
        _sound.Dispose();
        _tray.Dispose();
        Closing -= OnWindowClosing;
        System.Windows.Application.Current.Shutdown();
    }

    private void ShowSnackbar(string title, string message)
    {
        var snackbar = new Wpf.Ui.Controls.Snackbar(RootSnackbarPresenter)
        {
            Title = title,
            Content = message,
            Timeout = TimeSpan.FromSeconds(4),
        };
        snackbar.Show();
    }
}
