namespace WeiTimer.Services;

/// <summary>
/// Desktop notifications via the tray icon's balloon tip. The Linux original
/// uses Gio.Notification over D-Bus; on Windows, staying an unpackaged plain
/// .exe (no MSIX/AppUserModelID) rules out modern toast notifications, so this
/// uses NotifyIcon.ShowBalloonTip instead -- zero setup, degrades silently if
/// nothing's listening, same as the Python original.
/// </summary>
public sealed class NotificationService
{
    private readonly TrayIconService _tray;

    public NotificationService(TrayIconService tray) => _tray = tray;

    public void Send(string title, string body) => _tray.ShowBalloonTip(title, body);
}
