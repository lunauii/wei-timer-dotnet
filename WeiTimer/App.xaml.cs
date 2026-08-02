using System;
using System.Windows;
using WeiTimer.Services;
using WeiTimer.Views;

namespace WeiTimer;

/// <summary>
/// Application entry point. Mirrors app.py's Adw.Application, which gets
/// single-instance-by-application_id and window re-presentation for free via
/// GLib/D-Bus; here that's built explicitly via SingleInstanceGuard. The
/// window hides rather than closes (see MainWindow.OnWindowClosing), so actual
/// shutdown only happens via the tray's Exit action.
/// </summary>
public partial class App : System.Windows.Application
{
    private SingleInstanceGuard? _guard;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _guard = new SingleInstanceGuard();
        if (!_guard.IsPrimaryInstance)
        {
            SingleInstanceGuard.SignalPrimaryInstance();
            _guard.Dispose();
            Shutdown();
            return;
        }

        _guard.ActivateRequested += () => Dispatcher.BeginInvoke(() => _mainWindow?.ShowAndActivate());
        _guard.StartListening();

        _mainWindow = new MainWindow();
        _mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _guard?.Dispose();
        base.OnExit(e);
    }
}
