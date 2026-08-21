using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services.Interfaces;

/// <summary>
/// Manages countdown timers displayed on outputs.
/// </summary>
public interface ICountdownService
{
    /// <summary>
    /// Start a countdown with the given configuration.
    /// </summary>
    Task StartAsync(CountdownConfig config);

    /// <summary>
    /// Stop the currently running countdown.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Pause the current countdown.
    /// </summary>
    Task PauseAsync();

    /// <summary>
    /// Resume a paused countdown.
    /// </summary>
    Task ResumeAsync();

    /// <summary>
    /// Get the current remaining time in milliseconds.
    /// Returns null if no countdown is active.
    /// </summary>
    long? GetRemainingMs();

    /// <summary>
    /// Whether a countdown is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Event raised every second with the remaining time.
    /// </summary>
    event EventHandler<long>? Tick;

    /// <summary>
    /// Event raised when the countdown completes.
    /// </summary>
    event EventHandler? Completed;
}
