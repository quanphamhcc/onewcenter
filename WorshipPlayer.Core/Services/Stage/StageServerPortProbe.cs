using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace WorshipPlayer.Core.Services.Stage;

/// <summary>
/// Outcome of attempting to start the stage display server.
/// </summary>
/// <param name="Started">True if the server was started successfully.</param>
/// <param name="Error">A human-readable error message when <paramref name="Started"/> is false; otherwise null.</param>
public record StageServerStartResult(bool Started, string? Error);

/// <summary>
/// Host-free check that a TCP port can be bound before the stage display server
/// commits to starting its ASP.NET Core host. Uses only BCL sockets so it stays
/// cross-platform and free of any Kestrel/WebApplication dependency.
/// </summary>
public static class StageServerPortProbe
{
    /// <summary>
    /// Attempts to bind the given port on all interfaces to verify it is available.
    /// The listener is stopped immediately so the port is left free for the real host.
    /// </summary>
    /// <param name="port">The TCP port to probe.</param>
    /// <param name="error">A human-readable message describing the failure, or null on success.</param>
    /// <returns>True if the port could be reserved; otherwise false.</returns>
    public static bool TryReserve(int port, out string? error)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            error = null;
            return true;
        }
        catch (SocketException ex)
        {
            error = $"Port {port} is not available: {ex.Message}";
            return false;
        }
        catch (IOException ex)
        {
            error = $"Port {port} is not available: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            error = $"Could not reserve port {port}: {ex.Message}";
            return false;
        }
        finally
        {
            listener?.Stop();
        }
    }
}
