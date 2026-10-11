using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Core.Services.Stage;

namespace WorshipPlayer.Presenter.Services;

/// <summary>
/// Information about a connected remote control client.
/// </summary>
public class RemoteClientInfo
{
    /// <summary>The WebSocket connection.</summary>
    public WebSocket WebSocket { get; set; } = null!;

    /// <summary>Authentication token for this client.</summary>
    public string Token { get; set; } = "";

    /// <summary>Display name for this remote (e.g., "Pastor's iPad").</summary>
    public string Name { get; set; } = "Remote";

    /// <summary>Client IP address.</summary>
    public string IP { get; set; } = "";

    /// <summary>When the client connected.</summary>
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Information about a connected WebSocket client (musician device).
/// </summary>
public class ClientInfo
{
    /// <summary>The WebSocket connection.</summary>
    public WebSocket WebSocket { get; set; } = null!;

    /// <summary>Display name for this musician (e.g., "Guitar 1").</summary>
    public string Name { get; set; } = "";

    /// <summary>Capo fret position for chord transposition.</summary>
    public int Capo { get; set; } = 0;

    /// <summary>Whether to show Nashville numbers.</summary>
    public bool Nashville { get; set; } = false;

    /// <summary>Layout view mode: "onecol" or "twocol".</summary>
    public string View { get; set; } = "onecol";

    /// <summary>Client IP address.</summary>
    public string IP { get; set; } = "";

    /// <summary>When the client connected.</summary>
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Embedded Kestrel-based web server that serves the stage display HTML page
/// and broadcasts slide updates over WebSocket to connected clients (iPads, etc.).
/// 
/// Supports per-musician personalization via URL query parameters:
///   /stage?capo=2&amp;nashville=true&amp;view=twocol&amp;name=Guitar1
/// 
/// Runs on a background thread alongside the WPF application.
/// </summary>
public class StageDisplayServer : IDisposable
{
    private readonly int _port;
    private WebApplication? _app;
    private Task? _serverTask;
    private readonly ConcurrentDictionary<string, ClientInfo> _connectedClients = new();
    private readonly ConcurrentDictionary<string, RemoteClientInfo> _remoteClients = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
    private CancellationTokenSource? _cts;
    private readonly MusicianViewRenderer _musicianRenderer;
    private StageDisplayData? _lastBroadcastData;
    private RemoteCommandHandler? _remoteCommandHandler;
    private PresenterSettings? _presenterSettings;

    /// <summary>
    /// The error message from the last failed <see cref="StartAsync"/> attempt, or null if the
    /// last start succeeded. Populated deterministically on a bind failure.
    /// </summary>
    public string? LastStartError { get; private set; }

    /// <summary>
    /// Creates a new StageDisplayServer instance.
    /// </summary>
    /// <param name="port">The port to listen on (default 5055).</param>
    /// <param name="chordProParser">Optional ChordProParserService for musician view rendering.</param>
    public StageDisplayServer(int port = 5055, ChordProParserService? chordProParser = null)
    {
        _port = port;
        _musicianRenderer = new MusicianViewRenderer(chordProParser ?? new ChordProParserService());
    }

    /// <summary>
    /// Creates a new StageDisplayServer with remote control support.
    /// </summary>
    /// <param name="port">The port to listen on.</param>
    /// <param name="chordProParser">Optional ChordProParserService for musician view rendering.</param>
    /// <param name="presenterEngine">The presenter engine for remote control commands.</param>
    /// <param name="presenterSettings">Settings including remote configuration.</param>
    public StageDisplayServer(int port, ChordProParserService? chordProParser, PresenterEngine presenterEngine, PresenterSettings presenterSettings)
        : this(port, chordProParser)
    {
        _presenterSettings = presenterSettings;
        _remoteCommandHandler = new RemoteCommandHandler(presenterEngine, presenterSettings);
    }

    /// <summary>
    /// Starts the web server, binding Kestrel on the fixed port and observing the bind outcome
    /// deterministically. On a bind failure (port already in use, blocked) the exception is caught
    /// here and converted into a <see cref="StageServerStartResult"/> with <c>Started = false</c> and
    /// a human-readable message; it never throws out of the normal start path. The start/observe path
    /// does not dereference the optional collaborators (<see cref="_presenterSettings"/>,
    /// <see cref="_remoteCommandHandler"/>, presenter engine), which are null on the single-arg ctor.
    /// </summary>
    public async Task<StageServerStartResult> StartAsync(System.Threading.CancellationToken ct = default)
    {
        _cts = new CancellationTokenSource();

        // Clear any stale tokens from previous sessions on server start
        _remoteCommandHandler?.ClearAllTokens();

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(_port);
        });
        // Suppress default logging noise in WPF context
        builder.Logging.ClearProviders();

        _app = builder.Build();

        // Enable WebSocket middleware
        _app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30)
        });

        // GET /stage - serves the stage display HTML page
        _app.MapGet("/stage", (HttpContext context) =>
        {
            return Results.Content(StageDisplayHtml.GetHtml(_port), "text/html");
        });

        // GET /stage/connections - returns JSON array of connected clients (requires remote auth token)
        _app.MapGet("/stage/connections", (HttpContext context) =>
        {
            // Require a valid remote auth token to view connection info (prevents LAN information disclosure)
            var token = context.Request.Query["token"].FirstOrDefault()
                        ?? context.Request.Headers["Authorization"].FirstOrDefault()?.Replace("Bearer ", "");

            if (_remoteCommandHandler == null || !_remoteCommandHandler.IsTokenValid(token))
            {
                return Results.Json(new { error = "Authentication required" }, _jsonOptions, statusCode: 401);
            }

            var connections = _connectedClients.Values
                .Where(c => c.WebSocket.State == WebSocketState.Open)
                .Select(c => new
                {
                    name = c.Name,
                    capo = c.Capo,
                    nashville = c.Nashville,
                    view = c.View,
                    connectedAt = c.ConnectedAt.ToString("o")
                })
                .ToArray();

            return Results.Json(connections, _jsonOptions);
        });

        // WebSocket /ws/stage - real-time slide updates
        _app.Map("/ws/stage", async (HttpContext context) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("WebSocket connections only");
                return;
            }

            // Parse query parameters for client preferences
            var query = context.Request.Query;
            int capo = 0;
            bool nashville = false;
            string view = "onecol";
            string name = "";

            if (query.TryGetValue("capo", out var capoVal) && int.TryParse(capoVal, out int parsedCapo))
                capo = Math.Clamp(parsedCapo, 0, 12);

            if (query.TryGetValue("nashville", out var nashvilleVal))
                nashville = string.Equals(nashvilleVal, "true", StringComparison.OrdinalIgnoreCase);

            if (query.TryGetValue("view", out var viewVal) && !string.IsNullOrEmpty(viewVal))
                view = viewVal.ToString().ToLowerInvariant() == "twocol" ? "twocol" : "onecol";

            if (query.TryGetValue("name", out var nameVal) && !string.IsNullOrEmpty(nameVal))
                name = nameVal.ToString();

            var ws = await context.WebSockets.AcceptWebSocketAsync();
            string clientId = Guid.NewGuid().ToString();
            string clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var clientInfo = new ClientInfo
            {
                WebSocket = ws,
                Name = name,
                Capo = capo,
                Nashville = nashville,
                View = view,
                IP = clientIp,
                ConnectedAt = DateTime.UtcNow
            };

            _connectedClients[clientId] = clientInfo;

            try
            {
                // Send current state immediately if available
                if (_lastBroadcastData != null)
                {
                    await SendPersonalizedData(clientInfo, _lastBroadcastData);
                }

                // Keep connection alive by reading (handles close frames and config messages)
                var buffer = new byte[4096];
                while (ws.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }

                    // Handle config messages from the client
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        HandleClientMessage(clientInfo, message);
                    }
                }
            }
            catch (WebSocketException)
            {
                // Client disconnected
            }
            catch (OperationCanceledException)
            {
                // Server shutting down
            }
            finally
            {
                _connectedClients.TryRemove(clientId, out _);
            }
        });

        // ─── Remote Control Endpoints ───────────────────────────────────

        // GET /remote - serves the remote control HTML page
        _app.MapGet("/remote", (HttpContext context) =>
        {
            if (_presenterSettings?.RemoteEnabled != true)
            {
                return Results.Content("<h1>Remote control is disabled</h1>", "text/html");
            }
            return Results.Content(RemoteHtml.GetHtml(_port), "text/html");
        });

        // POST /remote/auth - authenticates with PIN, returns session token
        _app.MapPost("/remote/auth", async (HttpContext context) =>
        {
            if (_remoteCommandHandler == null || _presenterSettings?.RemoteEnabled != true)
            {
                return Results.Json(new { success = false, error = "Remote control is disabled" }, _jsonOptions);
            }

            try
            {
                using var reader = new System.IO.StreamReader(context.Request.Body);
                var body = await reader.ReadToEndAsync();
                using var doc = JsonDocument.Parse(body);
                var pin = doc.RootElement.TryGetProperty("pin", out var pinProp) ? pinProp.GetString() ?? "" : "";

                var result = _remoteCommandHandler.Authenticate(pin);
                return Results.Json(result, _jsonOptions);
            }
            catch (JsonException)
            {
                return Results.Json(new { success = false, error = "Invalid request body" }, _jsonOptions);
            }
        });

        // GET /remote/status - returns connected remote count and info
        _app.MapGet("/remote/status", () =>
        {
            var remotes = _remoteClients.Values
                .Where(c => c.WebSocket.State == WebSocketState.Open)
                .Select(c => new
                {
                    name = c.Name,
                    ip = c.IP,
                    connectedAt = c.ConnectedAt.ToString("o")
                })
                .ToArray();

            return Results.Json(new
            {
                connectedRemotes = remotes.Length,
                clients = remotes
            }, _jsonOptions);
        });

        // WebSocket /ws/remote - remote control WebSocket
        _app.Map("/ws/remote", async (HttpContext context) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("WebSocket connections only");
                return;
            }

            if (_remoteCommandHandler == null || _presenterSettings?.RemoteEnabled != true)
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsync("Remote control is disabled");
                return;
            }

            // Get token from query string
            var query = context.Request.Query;
            string token = "";
            if (query.TryGetValue("token", out var tokenVal))
                token = tokenVal.ToString();

            var ws = await context.WebSockets.AcceptWebSocketAsync();
            string clientId = Guid.NewGuid().ToString();
            string clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var remoteClient = new RemoteClientInfo
            {
                WebSocket = ws,
                Token = token,
                Name = "Remote",
                IP = clientIp,
                ConnectedAt = DateTime.UtcNow
            };

            bool authenticated = false;

            try
            {
                var buffer = new byte[4096];
                while (ws.State == WebSocketState.Open && !_cts!.Token.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        await HandleRemoteMessage(remoteClient, clientId, message, authenticated,
                            (val) => { authenticated = val; });
                    }
                }
            }
            catch (WebSocketException)
            {
                // Client disconnected
            }
            catch (OperationCanceledException)
            {
                // Server shutting down
            }
            finally
            {
                _remoteClients.TryRemove(clientId, out _);
            }
        });

        // Start the server and OBSERVE the bind outcome on the awaited call (not fire-and-forget).
        // StartAsync starts Kestrel and binds the port; a bind failure surfaces here, where we can
        // catch it and map it to a StageServerStartResult instead of losing it on a background task.
        try
        {
            await _app.StartAsync(ct);
            // StartAsync (unlike the old fire-and-forget RunAsync) returns once the host is
            // listening, so a bind failure surfaces here. StopAsync stops/disposes _app directly,
            // so no background _serverTask is needed to tear down.
            _serverTask = null;
            LastStartError = null;
            return new StageServerStartResult(true, null);
        }
        catch (Exception ex)
        {
            // Bind failure (SocketException/IOException) or any other start error: fail gracefully.
            var message = $"Stage display server could not bind on port {_port}: {ex.Message}";
            LastStartError = message;

            // Tear down the half-started app so a later Stop/Dispose is clean.
            try
            {
                if (_app != null)
                {
                    await _app.DisposeAsync();
                }
            }
            catch
            {
                // Ignore cleanup errors on an already-failed start.
            }
            _app = null;
            _serverTask = null;

            return new StageServerStartResult(false, message);
        }
    }

    /// <summary>
    /// Handle incoming messages from a remote control WebSocket client.
    /// </summary>
    private async Task HandleRemoteMessage(RemoteClientInfo client, string clientId, string message, bool authenticated, Action<bool> setAuthenticated)
    {
        if (_remoteCommandHandler == null) return;

        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp)) return;
            string type = typeProp.GetString() ?? "";

            if (type == "auth")
            {
                // Authenticate with token
                string authToken = root.TryGetProperty("token", out var tokenProp) ? tokenProp.GetString() ?? "" : client.Token;

                if (_remoteCommandHandler.IsTokenValid(authToken))
                {
                    setAuthenticated(true);
                    client.Token = authToken;
                    _remoteClients[clientId] = client;

                    // Send current state immediately
                    var state = _remoteCommandHandler.GetCurrentState();
                    var stateJson = JsonSerializer.Serialize(state, _jsonOptions);
                    var stateBytes = Encoding.UTF8.GetBytes(stateJson);
                    await client.WebSocket.SendAsync(new ArraySegment<byte>(stateBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
                else
                {
                    // Send auth failed message
                    var failMsg = JsonSerializer.Serialize(new { type = "authFailed", error = "Invalid token" }, _jsonOptions);
                    var failBytes = Encoding.UTF8.GetBytes(failMsg);
                    await client.WebSocket.SendAsync(new ArraySegment<byte>(failBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            else if (type == "command" && authenticated)
            {
                // Extract action and data
                string action = root.TryGetProperty("action", out var actionProp) ? actionProp.GetString() ?? "" : "";
                string? dataStr = root.TryGetProperty("data", out var dataProp) ? dataProp.ToString() : null;

                // Use JsonSerializer to construct the command JSON safely (avoids injection via unescaped strings)
                object commandObj;
                if (dataStr != null)
                {
                    using var dataDoc = JsonDocument.Parse(dataStr);
                    commandObj = new { action, data = dataDoc.RootElement.Clone() };
                }
                else
                {
                    commandObj = new { action };
                }
                string commandJson = JsonSerializer.Serialize(commandObj, _jsonOptions);

                var result = _remoteCommandHandler.HandleCommand(commandJson, client.Token);

                // Send result back to this client
                var resultJson = JsonSerializer.Serialize(new { type = "result", success = result.Success, error = result.Error }, _jsonOptions);
                var resultBytes = Encoding.UTF8.GetBytes(resultJson);
                await client.WebSocket.SendAsync(new ArraySegment<byte>(resultBytes), WebSocketMessageType.Text, true, CancellationToken.None);

                // Broadcast state update to all remote clients after command execution
                if (result.Success)
                {
                    await BroadcastRemoteState();
                }
            }
        }
        catch (JsonException)
        {
            // Ignore malformed messages
        }
    }

    /// <summary>
    /// Broadcasts current state to all authenticated remote control clients.
    /// Called after any command execution or state change.
    /// </summary>
    public async Task BroadcastRemoteState()
    {
        if (_remoteCommandHandler == null) return;

        var state = _remoteCommandHandler.GetCurrentState();
        var json = JsonSerializer.Serialize(state, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(bytes);

        var deadClients = new List<string>();

        foreach (var kvp in _remoteClients)
        {
            var clientId = kvp.Key;
            var client = kvp.Value;

            if (client.WebSocket.State == WebSocketState.Open)
            {
                try
                {
                    await client.WebSocket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch (WebSocketException)
                {
                    deadClients.Add(clientId);
                }
            }
            else
            {
                deadClients.Add(clientId);
            }
        }

        foreach (var clientId in deadClients)
        {
            if (_remoteClients.TryRemove(clientId, out var removed))
            {
                try { removed.WebSocket.Dispose(); } catch { }
            }
        }
    }

    /// <summary>
    /// Gets the number of currently connected remote control clients.
    /// </summary>
    public int ConnectedRemoteCount => _remoteClients.Count(c => c.Value.WebSocket.State == WebSocketState.Open);

    /// <summary>
    /// Gets the RemoteCommandHandler for external use (e.g., direct state broadcasting).
    /// </summary>
    public RemoteCommandHandler? RemoteHandler => _remoteCommandHandler;

    /// <summary>
    /// Handle incoming JSON messages from a connected client.
    /// Supports config messages to update client preferences dynamically.
    /// </summary>
    private void HandleClientMessage(ClientInfo client, string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            if (root.TryGetProperty("type", out var typeProp) &&
                typeProp.GetString() == "config")
            {
                if (root.TryGetProperty("capo", out var capoProp) && capoProp.TryGetInt32(out int newCapo))
                    client.Capo = Math.Clamp(newCapo, 0, 12);

                if (root.TryGetProperty("nashville", out var nashProp))
                    client.Nashville = nashProp.GetBoolean();

                if (root.TryGetProperty("view", out var viewProp))
                {
                    string viewStr = viewProp.GetString() ?? "onecol";
                    client.View = viewStr == "twocol" ? "twocol" : "onecol";
                }

                if (root.TryGetProperty("name", out var nameProp))
                    client.Name = nameProp.GetString() ?? "";
            }
        }
        catch (JsonException)
        {
            // Ignore malformed messages
        }
    }

    /// <summary>
    /// Stops the web server and disconnects all clients.
    /// </summary>
    public async Task StopAsync()
    {
        if (_cts != null)
        {
            _cts.Cancel();
        }

        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }

        if (_serverTask != null)
        {
            try
            {
                await _serverTask;
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
            _serverTask = null;
        }

        _connectedClients.Clear();
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// Broadcasts a slide update to all connected WebSocket clients.
    /// Each client receives personalized data based on their preferences.
    /// </summary>
    /// <param name="data">The base stage display data to send.</param>
    public async Task BroadcastSlideUpdate(StageDisplayData data)
    {
        _lastBroadcastData = data;

        var deadClients = new List<string>();

        foreach (var kvp in _connectedClients)
        {
            var clientId = kvp.Key;
            var client = kvp.Value;

            if (client.WebSocket.State == WebSocketState.Open)
            {
                try
                {
                    await SendPersonalizedData(client, data);
                }
                catch (WebSocketException)
                {
                    deadClients.Add(clientId);
                }
            }
            else
            {
                deadClients.Add(clientId);
            }
        }

        // Remove dead clients
        foreach (var clientId in deadClients)
        {
            if (_connectedClients.TryRemove(clientId, out var removed))
            {
                try { removed.WebSocket.Dispose(); } catch { }
            }
        }
    }

    /// <summary>
    /// Send personalized data to a specific client based on their preferences.
    /// </summary>
    private async Task SendPersonalizedData(ClientInfo client, StageDisplayData baseData)
    {
        StageDisplayData dataToSend;

        // Only personalize if client has non-default preferences
        if (client.Capo > 0 || client.Nashville)
        {
            dataToSend = _musicianRenderer.CreateMusicianData(baseData, client.Capo, client.Nashville);
        }
        else
        {
            dataToSend = baseData;
        }

        var json = JsonSerializer.Serialize(dataToSend, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(bytes);

        await client.WebSocket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    /// <summary>
    /// Gets the number of currently connected clients.
    /// </summary>
    public int ConnectedClientCount => _connectedClients.Count(c => c.Value.WebSocket.State == WebSocketState.Open);

    /// <summary>
    /// Gets information about all connected clients.
    /// </summary>
    public IReadOnlyList<ClientInfo> GetConnectedClients()
    {
        return _connectedClients.Values
            .Where(c => c.WebSocket.State == WebSocketState.Open)
            .ToList()
            .AsReadOnly();
    }

    public void Dispose()
    {
        try
        {
            // Use a timeout to prevent deadlock on UI thread shutdown
            var stopTask = StopAsync();
            if (!stopTask.Wait(TimeSpan.FromSeconds(3)))
            {
                // Server didn't stop in time — force cancel
                System.Diagnostics.Debug.WriteLine("[StageDisplayServer] Stop timed out, forcing shutdown");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StageDisplayServer] Dispose error: {ex.Message}");
        }
    }
}
