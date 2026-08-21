using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Services;

/// <summary>
/// Manages countdown timers displayed on outputs.
/// State machine: Stopped -> Running -> Paused -> Running/Stopped.
/// Fires Tick events every second and a Completed event when done.
/// Uses System.Timers.Timer internally for cross-platform timing.
/// </summary>
public class CountdownService : ICountdownService, IDisposable
{
    private System.Timers.Timer? _timer;
    private CountdownConfig? _currentConfig;
    private long _remainingMs;
    private long _elapsedMs;
    private DateTime _startTime;
    private CountdownState _state = CountdownState.Stopped;

    /// <inheritdoc />
    public bool IsRunning => _state == CountdownState.Running;

    /// <summary>
    /// Whether the countdown is paused.
    /// </summary>
    public bool IsPaused => _state == CountdownState.Paused;

    /// <summary>
    /// The current state of the countdown.
    /// </summary>
    public CountdownState State => _state;

    /// <summary>
    /// The current countdown configuration (null if not started).
    /// </summary>
    public CountdownConfig? CurrentConfig => _currentConfig;

    /// <inheritdoc />
    public event EventHandler<long>? Tick;

    /// <inheritdoc />
    public event EventHandler? Completed;

    /// <inheritdoc />
    public Task StartAsync(CountdownConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        // Stop any existing countdown
        StopInternal();

        _currentConfig = config;
        _startTime = DateTime.UtcNow;
        _elapsedMs = 0;

        switch (config.Type)
        {
            case CountdownType.CountdownToZero:
                _remainingMs = config.DurationMs;
                break;

            case CountdownType.CountdownToTime:
                if (config.TargetTime.HasValue)
                {
                    var diff = config.TargetTime.Value - DateTime.Now;
                    _remainingMs = Math.Max(0, (long)diff.TotalMilliseconds);
                }
                else
                {
                    _remainingMs = config.DurationMs;
                }
                break;

            case CountdownType.CountUp:
                _remainingMs = 0;
                break;
        }

        _state = CountdownState.Running;

        // Create and start the timer (ticks every 1 second)
        _timer = new System.Timers.Timer(1000);
        _timer.Elapsed += OnTimerElapsed;
        _timer.AutoReset = true;
        _timer.Start();

        // Fire initial tick
        FireTick();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync()
    {
        StopInternal();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PauseAsync()
    {
        if (_state != CountdownState.Running)
            return Task.CompletedTask;

        _state = CountdownState.Paused;
        _timer?.Stop();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ResumeAsync()
    {
        if (_state != CountdownState.Paused)
            return Task.CompletedTask;

        _state = CountdownState.Running;
        _startTime = DateTime.UtcNow;
        _timer?.Start();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public long? GetRemainingMs()
    {
        if (_state == CountdownState.Stopped)
            return null;

        if (_currentConfig?.Type == CountdownType.CountUp)
            return _elapsedMs;

        return _remainingMs;
    }

    private void OnTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_state != CountdownState.Running || _currentConfig == null)
            return;

        switch (_currentConfig.Type)
        {
            case CountdownType.CountdownToZero:
                _remainingMs -= 1000;
                if (_remainingMs <= 0)
                {
                    _remainingMs = 0;
                    FireTick();
                    StopInternal();
                    Completed?.Invoke(this, EventArgs.Empty);
                    return;
                }
                break;

            case CountdownType.CountdownToTime:
                if (_currentConfig.TargetTime.HasValue)
                {
                    var diff = _currentConfig.TargetTime.Value - DateTime.Now;
                    _remainingMs = Math.Max(0, (long)diff.TotalMilliseconds);
                    if (_remainingMs <= 0)
                    {
                        _remainingMs = 0;
                        FireTick();
                        StopInternal();
                        Completed?.Invoke(this, EventArgs.Empty);
                        return;
                    }
                }
                break;

            case CountdownType.CountUp:
                _elapsedMs += 1000;
                _remainingMs = _elapsedMs;
                break;
        }

        FireTick();
    }

    private void FireTick()
    {
        var value = _currentConfig?.Type == CountdownType.CountUp ? _elapsedMs : _remainingMs;
        Tick?.Invoke(this, value);
    }

    private void StopInternal()
    {
        _state = CountdownState.Stopped;

        if (_timer != null)
        {
            _timer.Stop();
            _timer.Elapsed -= OnTimerElapsed;
            _timer.Dispose();
            _timer = null;
        }

        _currentConfig = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        StopInternal();
    }
}

/// <summary>
/// Countdown timer states.
/// </summary>
public enum CountdownState
{
    Stopped,
    Running,
    Paused
}
