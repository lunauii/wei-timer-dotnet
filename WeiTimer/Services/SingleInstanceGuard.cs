using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace WeiTimer.Services;

/// <summary>
/// Single-instance enforcement, new for the Windows port -- the Linux original
/// gets this for free via GLib/D-Bus application_id (Adw.Application re-presents
/// the existing window instead of starting a second instance). A named Mutex
/// detects whether we're first; a named pipe lets a second launch hand off to
/// the first instance and ask it to come to the foreground.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\WeiTimer-SingleInstance";
    private const string PipeName = "WeiTimerActivate";

    private readonly Mutex _mutex;

    public bool IsPrimaryInstance { get; }

    /// <summary>Fires (on a background thread) when a later launch asks this
    /// instance to come to the foreground.</summary>
    public event Action? ActivateRequested;

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsPrimaryInstance = createdNew;
    }

    /// <summary>Starts a background thread accepting "activate" pings from later
    /// instances. Only meaningful when this is the primary instance.</summary>
    public void StartListening()
    {
        if (!IsPrimaryInstance)
            return;

        var thread = new Thread(ServerLoop) { IsBackground = true, Name = "WeiTimer.SingleInstancePipe" };
        thread.Start();
    }

    private void ServerLoop()
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                server.WaitForConnection();
                using var reader = new StreamReader(server);
                if (reader.ReadLine() == "activate")
                    ActivateRequested?.Invoke();
            }
            catch (IOException)
            {
                // Pipe error (e.g. client disconnected mid-handshake) -- just
                // recreate the server and keep listening.
            }
        }
    }

    /// <summary>Called by a non-primary instance to ask the primary to come to
    /// the foreground, before this instance exits.</summary>
    public static void SignalPrimaryInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine("activate");
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            // Primary instance not listening / pipe busy -- nothing more we can do.
        }
    }

    public void Dispose()
    {
        if (IsPrimaryInstance)
            _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
