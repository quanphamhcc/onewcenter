using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Presenter.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for NdiOutputService: state management, initialization, lifecycle,
/// and frame counting using the StubNdiOutput implementation.
/// </summary>
public class NdiOutputServiceTests
{
    private PresenterSettings CreateDefaultSettings() => new()
    {
        NdiEnabled = true,
        NdiSourceName = "Test NDI Source",
        NdiFps = 30,
        NdiResolutionWidth = 1920,
        NdiResolutionHeight = 1080
    };

    [Fact]
    public void Constructor_DoesNotThrow()
    {
        var settings = CreateDefaultSettings();
        var service = new NdiOutputService(settings);

        Assert.NotNull(service);
        Assert.False(service.IsActive);
        Assert.False(service.HasOutput);
    }

    [Fact]
    public void SetNdiOutput_SetsHasOutput()
    {
        var settings = CreateDefaultSettings();
        var service = new NdiOutputService(settings);
        var stub = new StubNdiOutput();

        service.SetNdiOutput(stub);

        Assert.True(service.HasOutput);
    }

    [Fact]
    public void Initialize_WithNoOutput_ReturnsFalse()
    {
        var settings = CreateDefaultSettings();
        var service = new NdiOutputService(settings);

        bool result = service.Initialize();

        Assert.False(result);
    }

    [Fact]
    public void Initialize_WithStub_ReturnsTrue()
    {
        var settings = CreateDefaultSettings();
        var service = new NdiOutputService(settings);
        var stub = new StubNdiOutput();
        service.SetNdiOutput(stub);

        bool result = service.Initialize();

        Assert.True(result);
        Assert.True(stub.IsInitialized);
        Assert.Equal("Test NDI Source", stub.SourceName);
    }

    [Fact]
    public void Initialize_UsesDefaultName_WhenSettingsEmpty()
    {
        var settings = new PresenterSettings { NdiSourceName = "" };
        var service = new NdiOutputService(settings);
        var stub = new StubNdiOutput();
        service.SetNdiOutput(stub);

        service.Initialize();

        Assert.Equal("OWC Presenter Output", stub.SourceName);
    }

    [Fact]
    public void Start_WithNoOutput_DoesNotThrow()
    {
        var settings = CreateDefaultSettings();
        var service = new NdiOutputService(settings);

        // Should not throw
        service.Start();

        Assert.False(service.IsActive);
    }

    [Fact]
    public void Start_ActivatesOutput()
    {
        var settings = CreateDefaultSettings();
        var service = new NdiOutputService(settings);
        var stub = new StubNdiOutput();
        service.SetNdiOutput(stub);

        service.Start();

        Assert.True(service.IsActive);
        Assert.True(stub.IsActive);
    }

    [Fact]
    public void Start_AutoInitializes_IfNotDone()
    {
        var settings = CreateDefaultSettings();
        var service = new NdiOutputService(settings);
        var stub = new StubNdiOutput();
        service.SetNdiOutput(stub);

        // Don't call Initialize explicitly
        service.Start();

        Assert.True(stub.IsInitialized);
        Assert.True(stub.IsActive);
    }

    [Fact]
    public void Stop_DeactivatesOutput()
    {
        var settings = CreateDefaultSettings();
        var service = new NdiOutputService(settings);
        var stub = new StubNdiOutput();
        service.SetNdiOutput(stub);
        service.Start();

        service.Stop();

        Assert.False(service.IsActive);
        Assert.False(stub.IsActive);
    }

    [Fact]
    public void Stop_WithNoOutput_DoesNotThrow()
    {
        var settings = CreateDefaultSettings();
        var service = new NdiOutputService(settings);

        // Should not throw
        service.Stop();
    }

    [Fact]
    public void SendFrame_WhileActive_CountsFrames()
    {
        var settings = CreateDefaultSettings();
        var service = new NdiOutputService(settings);
        var stub = new StubNdiOutput();
        service.SetNdiOutput(stub);
        service.Start();

        byte[] pixels = new byte[1920 * 1080 * 4];
        service.SendFrame(pixels, 1920, 1080);
        service.SendFrame(pixels, 1920, 1080);
        service.SendFrame(pixels, 1920, 1080);

        Assert.Equal(3, stub.FrameCount);
    }

    [Fact]
    public void SendFrame_WhileInactive_DoesNotCountFrames()
    {
        var settings = CreateDefaultSettings();
        var service = new NdiOutputService(settings);
        var stub = new StubNdiOutput();
        service.SetNdiOutput(stub);
        // Do not start

        byte[] pixels = new byte[1920 * 1080 * 4];
        service.SendFrame(pixels, 1920, 1080);

        Assert.Equal(0, stub.FrameCount);
    }

    [Fact]
    public void SendFrame_AfterStop_DoesNotCountFrames()
    {
        var settings = CreateDefaultSettings();
        var service = new NdiOutputService(settings);
        var stub = new StubNdiOutput();
        service.SetNdiOutput(stub);
        service.Start();

        byte[] pixels = new byte[100 * 100 * 4];
        service.SendFrame(pixels, 100, 100);
        service.Stop();
        service.SendFrame(pixels, 100, 100);

        Assert.Equal(1, stub.FrameCount);
    }

    [Fact]
    public void SendFrame_WithNoOutput_DoesNotThrow()
    {
        var settings = CreateDefaultSettings();
        var service = new NdiOutputService(settings);

        byte[] pixels = new byte[100];
        // Should not throw
        service.SendFrame(pixels, 10, 10);
    }

    [Fact]
    public void StateChanged_FiresOnStart()
    {
        var settings = CreateDefaultSettings();
        var service = new NdiOutputService(settings);
        var stub = new StubNdiOutput();
        service.SetNdiOutput(stub);

        bool eventFired = false;
        bool? eventActive = null;
        service.StateChanged += (s, e) =>
        {
            eventFired = true;
            eventActive = e.IsActive;
        };

        service.Start();

        Assert.True(eventFired);
        Assert.True(eventActive);
    }

    [Fact]
    public void StateChanged_FiresOnStop()
    {
        var settings = CreateDefaultSettings();
        var service = new NdiOutputService(settings);
        var stub = new StubNdiOutput();
        service.SetNdiOutput(stub);
        service.Start();

        bool eventFired = false;
        bool? eventActive = null;
        service.StateChanged += (s, e) =>
        {
            eventFired = true;
            eventActive = e.IsActive;
        };

        service.Stop();

        Assert.True(eventFired);
        Assert.False(eventActive);
    }

    [Fact]
    public void Dispose_StopsAndCleansUp()
    {
        var settings = CreateDefaultSettings();
        var service = new NdiOutputService(settings);
        var stub = new StubNdiOutput();
        service.SetNdiOutput(stub);
        service.Start();

        service.Dispose();

        Assert.False(service.IsActive);
        Assert.False(service.HasOutput);
    }

    [Fact]
    public void SourceName_ReturnsConfiguredName()
    {
        var settings = CreateDefaultSettings();
        var service = new NdiOutputService(settings);
        var stub = new StubNdiOutput();
        service.SetNdiOutput(stub);
        service.Initialize();

        Assert.Equal("Test NDI Source", service.SourceName);
    }

    [Fact]
    public void SourceName_ReturnsSettingsName_WhenNoOutput()
    {
        var settings = CreateDefaultSettings();
        var service = new NdiOutputService(settings);

        Assert.Equal("Test NDI Source", service.SourceName);
    }

    [Fact]
    public void SetNdiOutput_DisposesOldOutput()
    {
        var settings = CreateDefaultSettings();
        var service = new NdiOutputService(settings);
        var stub1 = new StubNdiOutput();
        var stub2 = new StubNdiOutput();

        service.SetNdiOutput(stub1);
        stub1.Initialize("First");
        stub1.Start();

        service.SetNdiOutput(stub2);

        // The first stub should be stopped after dispose
        Assert.False(stub1.IsActive);
    }
}

/// <summary>
/// Unit tests for StubNdiOutput: verifying stub behavior and diagnostics.
/// </summary>
public class StubNdiOutputTests
{
    [Fact]
    public void Initialize_SetsSourceName()
    {
        var stub = new StubNdiOutput();

        bool result = stub.Initialize("My Source");

        Assert.True(result);
        Assert.Equal("My Source", stub.SourceName);
        Assert.True(stub.IsInitialized);
    }

    [Fact]
    public void Start_SetsIsActive()
    {
        var stub = new StubNdiOutput();
        stub.Initialize("Test");

        stub.Start();

        Assert.True(stub.IsActive);
    }

    [Fact]
    public void Start_WithoutInitialize_DoesNotActivate()
    {
        var stub = new StubNdiOutput();

        stub.Start();

        Assert.False(stub.IsActive);
    }

    [Fact]
    public void Stop_ClearsIsActive()
    {
        var stub = new StubNdiOutput();
        stub.Initialize("Test");
        stub.Start();

        stub.Stop();

        Assert.False(stub.IsActive);
    }

    [Fact]
    public void SendFrame_IncrementsFrameCount()
    {
        var stub = new StubNdiOutput();
        stub.Initialize("Test");
        stub.Start();

        byte[] pixels = new byte[100];
        stub.SendFrame(pixels, 10, 10, 40);
        stub.SendFrame(pixels, 10, 10, 40);

        Assert.Equal(2, stub.FrameCount);
    }

    [Fact]
    public void SendFrame_WhenNotActive_DoesNotCount()
    {
        var stub = new StubNdiOutput();
        stub.Initialize("Test");
        // Not started

        byte[] pixels = new byte[100];
        stub.SendFrame(pixels, 10, 10, 40);

        Assert.Equal(0, stub.FrameCount);
    }

    [Fact]
    public void Dispose_StopsAndClearsInitialized()
    {
        var stub = new StubNdiOutput();
        stub.Initialize("Test");
        stub.Start();

        stub.Dispose();

        Assert.False(stub.IsActive);
        Assert.False(stub.IsInitialized);
    }

    [Fact]
    public void FrameCount_ResetOnInitialize()
    {
        var stub = new StubNdiOutput();
        stub.Initialize("Test");
        stub.Start();

        byte[] pixels = new byte[100];
        stub.SendFrame(pixels, 10, 10, 40);
        Assert.Equal(1, stub.FrameCount);

        // Re-initialize resets count
        stub.Initialize("Test2");
        Assert.Equal(0, stub.FrameCount);
    }
}
