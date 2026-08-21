using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Presenter.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for CountdownService.
/// Tests state transitions (start, pause, stop) and countdown type configurations.
/// </summary>
public class CountdownServiceTests : IDisposable
{
    private readonly CountdownService _service;

    public CountdownServiceTests()
    {
        _service = new CountdownService();
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    [Fact]
    public void InitialState_IsStopped()
    {
        Assert.Equal(CountdownState.Stopped, _service.State);
        Assert.False(_service.IsRunning);
        Assert.False(_service.IsPaused);
    }

    [Fact]
    public async Task StartAsync_TransitionsToRunning()
    {
        var config = new CountdownConfig
        {
            Type = CountdownType.CountdownToZero,
            DurationMs = 60000
        };

        await _service.StartAsync(config);

        Assert.Equal(CountdownState.Running, _service.State);
        Assert.True(_service.IsRunning);
    }

    [Fact]
    public async Task StartAsync_SetsCurrentConfig()
    {
        var config = new CountdownConfig
        {
            Name = "Pre-Service",
            Type = CountdownType.CountdownToZero,
            DurationMs = 300000
        };

        await _service.StartAsync(config);

        Assert.NotNull(_service.CurrentConfig);
        Assert.Equal("Pre-Service", _service.CurrentConfig!.Name);
    }

    [Fact]
    public async Task StartAsync_NullConfig_ThrowsException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.StartAsync(null!));
    }

    [Fact]
    public async Task PauseAsync_FromRunning_TransitionsToPaused()
    {
        await StartCountdown();

        await _service.PauseAsync();

        Assert.Equal(CountdownState.Paused, _service.State);
        Assert.True(_service.IsPaused);
        Assert.False(_service.IsRunning);
    }

    [Fact]
    public async Task PauseAsync_FromStopped_StaysStopped()
    {
        await _service.PauseAsync();

        Assert.Equal(CountdownState.Stopped, _service.State);
    }

    [Fact]
    public async Task ResumeAsync_FromPaused_TransitionsToRunning()
    {
        await StartCountdown();
        await _service.PauseAsync();

        await _service.ResumeAsync();

        Assert.Equal(CountdownState.Running, _service.State);
        Assert.True(_service.IsRunning);
    }

    [Fact]
    public async Task ResumeAsync_FromStopped_StaysStopped()
    {
        await _service.ResumeAsync();

        Assert.Equal(CountdownState.Stopped, _service.State);
    }

    [Fact]
    public async Task StopAsync_FromRunning_TransitionsToStopped()
    {
        await StartCountdown();

        await _service.StopAsync();

        Assert.Equal(CountdownState.Stopped, _service.State);
        Assert.False(_service.IsRunning);
        Assert.Null(_service.CurrentConfig);
    }

    [Fact]
    public async Task StopAsync_FromPaused_TransitionsToStopped()
    {
        await StartCountdown();
        await _service.PauseAsync();

        await _service.StopAsync();

        Assert.Equal(CountdownState.Stopped, _service.State);
    }

    [Fact]
    public async Task StopAsync_ClearsConfig()
    {
        await StartCountdown();

        await _service.StopAsync();

        Assert.Null(_service.CurrentConfig);
    }

    [Fact]
    public void GetRemainingMs_WhenStopped_ReturnsNull()
    {
        var result = _service.GetRemainingMs();
        Assert.Null(result);
    }

    [Fact]
    public async Task GetRemainingMs_WhenRunning_ReturnsValue()
    {
        var config = new CountdownConfig
        {
            Type = CountdownType.CountdownToZero,
            DurationMs = 60000
        };

        await _service.StartAsync(config);

        var remaining = _service.GetRemainingMs();
        Assert.NotNull(remaining);
        Assert.True(remaining >= 0);
    }

    [Fact]
    public async Task StartAsync_CountdownToZero_SetsRemainingFromDuration()
    {
        var config = new CountdownConfig
        {
            Type = CountdownType.CountdownToZero,
            DurationMs = 120000
        };

        await _service.StartAsync(config);

        var remaining = _service.GetRemainingMs();
        Assert.Equal(120000, remaining);
    }

    [Fact]
    public async Task StartAsync_CountUp_StartsFromZero()
    {
        var config = new CountdownConfig
        {
            Type = CountdownType.CountUp,
            DurationMs = 0
        };

        await _service.StartAsync(config);

        var remaining = _service.GetRemainingMs();
        Assert.Equal(0, remaining);
    }

    [Fact]
    public async Task StartAsync_CountdownToTime_CalculatesFromTarget()
    {
        var config = new CountdownConfig
        {
            Type = CountdownType.CountdownToTime,
            TargetTime = DateTime.Now.AddMinutes(5)
        };

        await _service.StartAsync(config);

        var remaining = _service.GetRemainingMs();
        Assert.NotNull(remaining);
        // Should be approximately 5 minutes in ms (allow 2s tolerance)
        Assert.InRange(remaining!.Value, 298000, 302000);
    }

    [Fact]
    public async Task StartAsync_SecondStart_StopsFirstCountdown()
    {
        await StartCountdown();

        var config2 = new CountdownConfig
        {
            Name = "Second Countdown",
            Type = CountdownType.CountdownToZero,
            DurationMs = 30000
        };

        await _service.StartAsync(config2);

        Assert.Equal("Second Countdown", _service.CurrentConfig!.Name);
        Assert.Equal(CountdownState.Running, _service.State);
    }

    [Fact]
    public async Task Tick_EventFires_OnStart()
    {
        long? tickValue = null;
        _service.Tick += (sender, value) => tickValue = value;

        var config = new CountdownConfig
        {
            Type = CountdownType.CountdownToZero,
            DurationMs = 60000
        };

        await _service.StartAsync(config);

        Assert.NotNull(tickValue);
        Assert.Equal(60000, tickValue);
    }

    [Fact]
    public async Task Dispose_StopsCountdown()
    {
        var service = new CountdownService();
        await service.StartAsync(new CountdownConfig
        {
            Type = CountdownType.CountdownToZero,
            DurationMs = 60000
        });

        service.Dispose();

        Assert.Equal(CountdownState.Stopped, service.State);
    }

    private async Task StartCountdown()
    {
        var config = new CountdownConfig
        {
            Type = CountdownType.CountdownToZero,
            DurationMs = 60000
        };
        await _service.StartAsync(config);
    }
}
