using System;
using NAudio.Wave;

namespace WeiTimer.Services;

/// <summary>
/// Sound playback via NAudio. Replaces the Linux original's pw-play/paplay/aplay
/// subprocess sniffing with a single in-process API that natively supports mp3
/// decode, per-clip volume, and an explicit stop -- no backend detection needed.
/// </summary>
public sealed class SoundService : IDisposable
{
    private AudioFileReader? _reader;
    private WaveOutEvent? _output;

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    /// <summary>Stops any current playback, then plays the given file at the given
    /// volume (0.0-1.0). Returns false (and no-ops) if path is null/missing.</summary>
    public bool Play(string? path, double volume = 1.0)
    {
        Stop();

        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            return false;

        try
        {
            _reader = new AudioFileReader(path)
            {
                Volume = (float)Math.Clamp(volume, 0.0, 1.0),
            };
            _output = new WaveOutEvent();
            _output.Init(_reader);
            _output.Play();
            return true;
        }
        catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException)
        {
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        _output?.Stop();
        _output?.Dispose();
        _output = null;
        _reader?.Dispose();
        _reader = null;
    }

    public void Dispose() => Stop();
}
