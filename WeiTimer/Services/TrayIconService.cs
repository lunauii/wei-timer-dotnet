using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Interop;
using WeiTimer.Interop;

namespace WeiTimer.Services;

/// <summary>
/// System tray icon + menu, in-process. The Linux original runs a *separate*
/// GTK3 subprocess for this (talking to the main process over stdin/stdout)
/// because GTK4 itself has no tray API. On Windows System.Windows.Forms.NotifyIcon
/// works fine directly inside a WPF app (via UseWindowsForms), so that whole
/// subprocess/IPC architecture doesn't need to exist here -- this just raises
/// plain C# events that MainWindow subscribes to.
///
/// The icon/balloon-tip hosting stays WinForms (NotifyIcon has no WPF-native
/// equivalent that also supports balloon tips), but the right-click menu is a
/// real WPF ContextMenu opened by hand instead of a WinForms ContextMenuStrip --
/// WPF-UI's theme dictionaries restyle the plain ContextMenu/MenuItem types
/// globally (see App.xaml), so this gets Fluent styling for free, which a
/// ContextMenuStrip has no way to pick up short of owner-drawing it by hand.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Window _owner;
    private readonly ContextMenu _menu;
    private readonly MenuItem _timerItem;

    public event Action? OpenRequested;
    public event Action? CaptureCaratsRequested;
    public event Action? CalibrateRequested;
    public event Action? ForceCompleteRequested;
    public event Action? ExitRequested;

    public TrayIconService(Window owner)
    {
        _owner = owner;

        _timerItem = new MenuItem { Header = "Timer: not running", IsEnabled = false };

        // StaysOpen defaults to true for a ContextMenu opened via IsOpen=true
        // from code (it's only forced false by the normal right-click-gesture
        // path via ContextMenuService). Without this, clicking outside the
        // menu -- or the owner window losing focus -- doesn't dismiss it, so
        // a later unrelated click can land on whatever menu item is still
        // sitting there.
        _menu = new ContextMenu { StaysOpen = false };
        _menu.Items.Add(_timerItem);
        _menu.Items.Add(new Separator());
        _menu.Items.Add(BuildItem("Open", () => OpenRequested?.Invoke()));
        _menu.Items.Add(BuildItem("Capture carats from screen…", () => CaptureCaratsRequested?.Invoke()));
        _menu.Items.Add(BuildItem("Calibrate timer region…", () => CalibrateRequested?.Invoke()));
        _menu.Items.Add(BuildItem("Force completion trigger (DEBUG)", () => ForceCompleteRequested?.Invoke()));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(BuildItem("Exit Wei Timer", () => ExitRequested?.Invoke()));

        _notifyIcon = new NotifyIcon
        {
            Text = "lunaui's Wei Timer",
            Icon = LoadTrayIcon(),
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        _notifyIcon.MouseUp += OnNotifyIconMouseUp;
    }

    private static MenuItem BuildItem(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }

    private void OnNotifyIconMouseUp(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
            return;

        // Windows only auto-dismisses a popup menu on an outside click if some
        // window was the foreground window at the moment it opened -- with no
        // window active, there's no deactivation signal for the popup to react
        // to, so it just sits there until an item is clicked. WinForms'
        // NotifyIcon.ContextMenuStrip does this same call internally; opening
        // our own WPF ContextMenu by hand loses it unless done explicitly.
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(_owner).Handle);

        // PlacementMode.Absolute's offsets are NOT relative to the true screen
        // origin -- they're relative to PlacementTarget's own window position,
        // so manually computed cursor coordinates landed near wherever the
        // (hidden, centered) MainWindow happened to be instead of the cursor.
        // PlacementMode.Mouse positions at the cursor directly, with WPF's own
        // correct DPI handling and its normal on-screen keep-inside flipping.
        _menu.PlacementTarget = _owner;
        _menu.Placement = PlacementMode.Mouse;
        _menu.IsOpen = true;
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "wei-timer.ico");
        return File.Exists(path)
            ? new System.Drawing.Icon(path)
            : System.Drawing.SystemIcons.Application;
    }

    /// <summary>Updates the disabled top menu item with the live "MM:SS" countdown
    /// text (or a not-running placeholder), mirroring the Linux tray helper's
    /// settext: stdin protocol -- but as a direct property set, no IPC needed.</summary>
    public void SetTimerText(string text) => _timerItem.Header = $"Timer: {text}";

    public void ShowBalloonTip(string title, string body, int timeoutMs = 5000)
    {
        try
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = body;
            _notifyIcon.ShowBalloonTip(timeoutMs);
        }
        catch
        {
            // No shell notification area, or the call failed for some other
            // environment-specific reason -- degrade silently.
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
