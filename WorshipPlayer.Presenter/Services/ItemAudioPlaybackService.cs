using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Presenter.Services;

/// <summary>
/// Plays one audio file at a time. The NAudio-backed implementation; never
/// constructed in unit tests (they inject a fake). One file = one Play call;
/// natural end is reported via <see cref="PlaybackEnded"/>.
/// </summary>
public interface IItemAudioPlayer : IDisposable
{
    /// <summary>Raised when the current file finished playing naturally.</summary>
    event EventHandler? PlaybackEnded;

    /// <summary>Start playing a file, stopping whatever is current. May throw.</summary>
    void Play(string filePath);

    /// <summary>Stop playback immediately.</summary>
    void Stop();
}

/// <summary>
/// NAudio implementation of <see cref="IItemAudioPlayer"/> (Windows only).
/// </summary>
public sealed class NAudioItemAudioPlayer : IItemAudioPlayer
{
    private NAudio.Wave.WaveOutEvent? _output;
    private NAudio.Wave.AudioFileReader? _reader;
    private bool _disposed;

    public event EventHandler? PlaybackEnded;

    public void Play(string filePath)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException("Audio file not found.", filePath);

        StopCurrent();

        _reader = new NAudio.Wave.AudioFileReader(filePath);
        _output = new NAudio.Wave.WaveOutEvent();
        _output.PlaybackStopped += OnPlaybackStopped;
        _output.Init(_reader);
        _output.Play();
    }

    public void Stop()
    {
        StopCurrent();
    }

    private void OnPlaybackStopped(object? sender, NAudio.Wave.StoppedEventArgs e)
    {
        // Natural end (no exception) -> advance the queue; errors are swallowed
        // here because Play() already validates, and the service skips failures.
        if (e.Exception == null)
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    private void StopCurrent()
    {
        if (_output != null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            try { _output.Stop(); } catch { /* best effort */ }
            _output.Dispose();
            _output = null;
        }
        if (_reader != null)
        {
            _reader.Dispose();
            _reader = null;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NAudioItemAudioPlayer));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopCurrent();
    }
}

/// <summary>
/// Plays a service item's attached audio sequentially (đợt 2 T17 / R9).
/// Exactly one playback stream at a time: starting a new item stops the old one
/// first. Missing/unreadable files are skipped (with a log event) and never crash
/// the presenter. When the list ends, playback loops from the first file if the
/// item's loop flag is set, otherwise it stops.
/// </summary>
public sealed class ItemAudioPlaybackService : IDisposable
{
    private readonly Func<IItemAudioPlayer> _playerFactory;
    private readonly object _gate = new();
    private IItemAudioPlayer? _player;
    private List<string> _queue = new();
    private int _index;
    private bool _loop;
    private bool _disposed;

    /// <summary>Diagnostic log (skipped/missing files). Never throws.</summary>
    public event EventHandler<string>? LogMessage;

    /// <summary>True while a queue is actively playing.</summary>
    public bool IsPlaying
    {
        get { lock (_gate) { return _queue.Count > 0; } }
    }

    /// <summary>
    /// <paramref name="playerFactory"/> lets tests inject a fake player; when null
    /// the NAudio player is created lazily on first use (Windows only).
    /// </summary>
    public ItemAudioPlaybackService(Func<IItemAudioPlayer>? playerFactory = null)
    {
        _playerFactory = playerFactory ?? (() => new NAudioItemAudioPlayer());
    }

    /// <summary>
    /// Stop any current playback and start the item's resolved audio. An empty
    /// file list is a no-op (previous playback is still stopped).
    /// </summary>
    public void PlayForItem(ResolvedItemAudio audio)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            StopLocked();

            if (audio.Files.Count == 0) return;

            _queue = new List<string>(audio.Files);
            _loop = audio.Loop;
            _index = 0;
            EnsurePlayerLocked();
            PlayCurrentLocked();
        }
    }

    /// <summary>Stop playback immediately and clear the queue.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (_disposed) return;
            StopLocked();
        }
    }

    private void EnsurePlayerLocked()
    {
        if (_player == null)
        {
            _player = _playerFactory();
            _player.PlaybackEnded += OnPlaybackEnded;
        }
    }

    private void PlayCurrentLocked()
    {
        // Skip missing/unreadable files; give up (stop) if none are playable.
        while (_index < _queue.Count)
        {
            var file = _queue[_index];
            try
            {
                Debug.Assert(_player != null);
                _player!.Play(file);
                return;
            }
            catch (Exception ex)
            {
                Log($"Skipped audio file '{file}': {ex.Message}");
                _index++;
            }
        }
        StopLocked();
    }

    private void OnPlaybackEnded(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_disposed || _queue.Count == 0) return;
            _index++;
            if (_index >= _queue.Count)
            {
                if (_loop)
                {
                    _index = 0;
                    Log("Audio loop: restarting from the first file.");
                }
                else
                {
                    StopLocked();
                    return;
                }
            }
            PlayCurrentLocked();
        }
    }

    private void StopLocked()
    {
        try { _player?.Stop(); } catch { /* best effort */ }
        _queue = new List<string>();
        _index = 0;
        _loop = false;
    }

    private void Log(string message)
    {
        try
        {
            LogMessage?.Invoke(this, message);
            Debug.WriteLine($"[ItemAudio] {message}");
        }
        catch { /* logging must never break playback */ }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ItemAudioPlaybackService));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            StopLocked();
            if (_player != null)
            {
                _player.PlaybackEnded -= OnPlaybackEnded;
                _player.Dispose();
                _player = null;
            }
        }
    }
}
