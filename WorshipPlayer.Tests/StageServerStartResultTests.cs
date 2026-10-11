using System.Net;
using System.Net.Sockets;
using WorshipPlayer.Core.Services.Stage;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// F8 (host-free portion): StageServerPortProbe.TryReserve mapped into a
/// StageServerStartResult. A busy port yields Started=false with a non-empty
/// Error; a free ephemeral port yields Started=true with null Error.
/// </summary>
public class StageServerStartResultTests
{
    private static StageServerStartResult MapProbe(int port)
    {
        bool ok = StageServerPortProbe.TryReserve(port, out string? error);
        return ok
            ? new StageServerStartResult(true, null)
            : new StageServerStartResult(false, error);
    }

    [Fact]
    public void BusyPort_MapsToStartedFalse_WithNonEmptyError()
    {
        // Occupy an ephemeral port with a test-owned listener.
        var occupier = new TcpListener(IPAddress.Any, 0);
        occupier.Start();
        int busyPort = ((IPEndPoint)occupier.LocalEndpoint).Port;

        try
        {
            var result = MapProbe(busyPort);

            Assert.False(result.Started);
            Assert.False(string.IsNullOrEmpty(result.Error));
        }
        finally
        {
            occupier.Stop();
        }
    }

    [Fact]
    public void FreePort_MapsToStartedTrue_WithNullError()
    {
        // Reserve then release an ephemeral port to learn a known-free number.
        var probe = new TcpListener(IPAddress.Any, 0);
        probe.Start();
        int freePort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var result = MapProbe(freePort);

        Assert.True(result.Started);
        Assert.Null(result.Error);
    }
}
