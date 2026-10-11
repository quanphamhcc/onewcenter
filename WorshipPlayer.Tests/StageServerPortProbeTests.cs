using System.Net;
using System.Net.Sockets;
using WorshipPlayer.Core.Services.Stage;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Tests for StageServerPortProbe.TryReserve and StageServerStartResult.
/// Uses ephemeral/caller-supplied ports only (never a fixed port) so the tests
/// never collide with a real running service and are not flaky.
/// </summary>
public class StageServerPortProbeTests
{
    /// <summary>
    /// Binds a TcpListener to an ephemeral port, reads the assigned port, then stops it.
    /// The returned port was free a moment ago and is highly likely still free.
    /// </summary>
    private static int GetFreeEphemeralPort()
    {
        var probe = new TcpListener(IPAddress.Any, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    [Fact]
    public void TryReserve_PortInUse_ReturnsFalseWithError()
    {
        var occupied = new TcpListener(IPAddress.Any, 0);
        occupied.Start();
        var port = ((IPEndPoint)occupied.LocalEndpoint).Port;
        try
        {
            var result = StageServerPortProbe.TryReserve(port, out var error);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(error));
            Assert.Contains(port.ToString(), error);
        }
        finally
        {
            occupied.Stop();
        }
    }

    [Fact]
    public void TryReserve_FreePort_ReturnsTrueWithNullError()
    {
        var port = GetFreeEphemeralPort();

        var result = StageServerPortProbe.TryReserve(port, out var error);

        Assert.True(result);
        Assert.Null(error);
    }

    [Fact]
    public void StageServerStartResult_FromBusyPort_HasStartedFalseAndError()
    {
        var occupied = new TcpListener(IPAddress.Any, 0);
        occupied.Start();
        var port = ((IPEndPoint)occupied.LocalEndpoint).Port;
        try
        {
            var ok = StageServerPortProbe.TryReserve(port, out var error);
            var result = new StageServerStartResult(ok, error);

            Assert.False(result.Started);
            Assert.False(string.IsNullOrEmpty(result.Error));
        }
        finally
        {
            occupied.Stop();
        }
    }

    [Fact]
    public void StageServerStartResult_FromFreePort_HasStartedTrue()
    {
        var port = GetFreeEphemeralPort();

        var ok = StageServerPortProbe.TryReserve(port, out var error);
        var result = new StageServerStartResult(ok, error);

        Assert.True(result.Started);
        Assert.Null(result.Error);
    }
}
