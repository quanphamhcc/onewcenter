using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Presenter.Services;

/// <summary>
/// Result of a remote command execution.
/// </summary>
public class RemoteCommandResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("stateUpdate")]
    public RemoteStateData? StateUpdate { get; set; }
}

/// <summary>
/// Result of a remote authentication attempt.
/// </summary>
public class AuthResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// State data sent to remote control clients. Contains the minimal info
/// needed to display current state and enable navigation.
/// </summary>
public class RemoteStateData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "state";

    [JsonPropertyName("currentText")]
    public string CurrentText { get; set; } = "";

    [JsonPropertyName("nextText")]
    public string NextText { get; set; } = "";

    [JsonPropertyName("slideIndex")]
    public int SlideIndex { get; set; }

    [JsonPropertyName("totalSlides")]
    public int TotalSlides { get; set; }

    [JsonPropertyName("isLive")]
    public bool IsLive { get; set; }

    [JsonPropertyName("isBlacked")]
    public bool IsBlacked { get; set; }

    [JsonPropertyName("currentItemIndex")]
    public int CurrentItemIndex { get; set; }

    [JsonPropertyName("items")]
    public List<RemoteItemInfo> Items { get; set; } = new();
}

/// <summary>
/// Minimal item info for the remote control's service order list.
/// </summary>
public class RemoteItemInfo
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
}

/// <summary>
/// Handles incoming commands from remote control clients.
/// Validates authentication tokens and permission settings before executing commands.
/// Includes rate limiting on authentication attempts and token expiry.
/// </summary>
public class RemoteCommandHandler
{
    private readonly PresenterEngine _engine;
    private readonly PresenterSettings _settings;
    private readonly Dictionary<string, DateTime> _validTokens = new();
    private readonly object _tokenLock = new();

    // Rate limiting state
    private int _failedAttempts;
    private DateTime _lockoutUntil = DateTime.MinValue;
    private readonly object _rateLimitLock = new();

    /// <summary>
    /// Maximum failed PIN attempts before lockout.
    /// </summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>
    /// Lockout duration after exceeding max failed attempts.
    /// </summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Token validity duration from time of issue.
    /// </summary>
    public static readonly TimeSpan TokenExpiry = TimeSpan.FromHours(24);

    public RemoteCommandHandler(PresenterEngine engine, PresenterSettings settings)
    {
        _engine = engine;
        _settings = settings;
    }

    /// <summary>
    /// Authenticate a remote client with a PIN code.
    /// Returns a session token on success.
    /// Enforces rate limiting: locks out after MaxFailedAttempts for LockoutDuration.
    /// </summary>
    public AuthResult Authenticate(string pin)
    {
        if (string.IsNullOrEmpty(pin))
        {
            return new AuthResult { Success = false, Error = "PIN is required" };
        }

        // Check rate limiting
        lock (_rateLimitLock)
        {
            if (DateTime.UtcNow < _lockoutUntil)
            {
                var remaining = (int)(_lockoutUntil - DateTime.UtcNow).TotalSeconds;
                return new AuthResult { Success = false, Error = $"Too many failed attempts. Try again in {remaining}s" };
            }
        }

        if (pin != _settings.RemotePin)
        {
            lock (_rateLimitLock)
            {
                _failedAttempts++;
                if (_failedAttempts >= MaxFailedAttempts)
                {
                    _lockoutUntil = DateTime.UtcNow.Add(LockoutDuration);
                    _failedAttempts = 0;
                }
            }
            return new AuthResult { Success = false, Error = "Invalid PIN" };
        }

        // Successful auth resets the failed attempt counter
        lock (_rateLimitLock)
        {
            _failedAttempts = 0;
        }

        // Generate a random 32-character hex token
        var tokenBytes = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(tokenBytes);
        }
        string token = Convert.ToHexString(tokenBytes).ToLowerInvariant();

        lock (_tokenLock)
        {
            _validTokens[token] = DateTime.UtcNow;
        }

        return new AuthResult { Success = true, Token = token };
    }

    /// <summary>
    /// Check if a token is valid (authenticated and not expired).
    /// Tokens expire after TokenExpiry (24 hours) from issue time.
    /// </summary>
    public bool IsTokenValid(string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;

        lock (_tokenLock)
        {
            if (_validTokens.TryGetValue(token, out var issuedAt))
            {
                if (DateTime.UtcNow - issuedAt > TokenExpiry)
                {
                    _validTokens.Remove(token);
                    return false;
                }
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Remove a token (logout / disconnect).
    /// </summary>
    public void RevokeToken(string token)
    {
        lock (_tokenLock)
        {
            _validTokens.Remove(token);
        }
    }

    /// <summary>
    /// Clear all session tokens (server restart, PIN change).
    /// </summary>
    public void ClearAllTokens()
    {
        lock (_tokenLock)
        {
            _validTokens.Clear();
        }
    }

    /// <summary>
    /// Handle an incoming command from a remote client.
    /// </summary>
    /// <param name="commandJson">JSON string with command data.</param>
    /// <param name="clientToken">The client's authentication token.</param>
    /// <returns>Result indicating success or failure.</returns>
    public RemoteCommandResult HandleCommand(string commandJson, string clientToken)
    {
        // Validate token
        if (!IsTokenValid(clientToken))
        {
            return new RemoteCommandResult { Success = false, Error = "Not authenticated" };
        }

        // Parse command
        string? action;
        JsonElement? data = null;

        try
        {
            using var doc = JsonDocument.Parse(commandJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("action", out var actionProp))
            {
                // Also support "type" field for compatibility with WebSocket messages
                if (!root.TryGetProperty("type", out actionProp))
                {
                    return new RemoteCommandResult { Success = false, Error = "Missing action field" };
                }
            }

            action = actionProp.GetString();

            if (root.TryGetProperty("data", out var dataProp))
            {
                data = dataProp.Clone();
            }
        }
        catch (JsonException)
        {
            return new RemoteCommandResult { Success = false, Error = "Invalid JSON" };
        }

        if (string.IsNullOrEmpty(action))
        {
            return new RemoteCommandResult { Success = false, Error = "Empty action" };
        }

        // Execute command based on type
        return ExecuteAction(action, data);
    }

    private RemoteCommandResult ExecuteAction(string action, JsonElement? data)
    {
        switch (action)
        {
            case "nextSlide":
                if (!_settings.RemoteAllowSlideAdvance)
                    return new RemoteCommandResult { Success = false, Error = "Slide advance not permitted" };
                _engine.NextSlideAction();
                return new RemoteCommandResult { Success = true, StateUpdate = GetCurrentState() };

            case "prevSlide":
                if (!_settings.RemoteAllowSlideAdvance)
                    return new RemoteCommandResult { Success = false, Error = "Slide advance not permitted" };
                _engine.PreviousSlideAction();
                return new RemoteCommandResult { Success = true, StateUpdate = GetCurrentState() };

            case "nextItem":
                if (!_settings.RemoteAllowItemNavigation)
                    return new RemoteCommandResult { Success = false, Error = "Item navigation not permitted" };
                _engine.NextItemAction();
                return new RemoteCommandResult { Success = true, StateUpdate = GetCurrentState() };

            case "prevItem":
                if (!_settings.RemoteAllowItemNavigation)
                    return new RemoteCommandResult { Success = false, Error = "Item navigation not permitted" };
                _engine.PreviousItemAction();
                return new RemoteCommandResult { Success = true, StateUpdate = GetCurrentState() };

            case "goLive":
                if (!_settings.RemoteAllowDisplayControl)
                    return new RemoteCommandResult { Success = false, Error = "Display control not permitted" };
                _engine.GoLive();
                return new RemoteCommandResult { Success = true, StateUpdate = GetCurrentState() };

            case "goBlack":
                if (!_settings.RemoteAllowDisplayControl)
                    return new RemoteCommandResult { Success = false, Error = "Display control not permitted" };
                _engine.GoBlack();
                return new RemoteCommandResult { Success = true, StateUpdate = GetCurrentState() };

            case "showLogo":
                if (!_settings.RemoteAllowDisplayControl)
                    return new RemoteCommandResult { Success = false, Error = "Display control not permitted" };
                _engine.ShowLogo();
                return new RemoteCommandResult { Success = true, StateUpdate = GetCurrentState() };

            case "goToSlide":
                if (!_settings.RemoteAllowSlideAdvance)
                    return new RemoteCommandResult { Success = false, Error = "Slide advance not permitted" };
                if (data.HasValue && data.Value.TryGetInt32(out int slideIndex))
                {
                    _engine.GoToSlide(slideIndex);
                    return new RemoteCommandResult { Success = true, StateUpdate = GetCurrentState() };
                }
                return new RemoteCommandResult { Success = false, Error = "Missing slide index" };

            case "goToItem":
                if (!_settings.RemoteAllowItemNavigation)
                    return new RemoteCommandResult { Success = false, Error = "Item navigation not permitted" };
                if (data.HasValue && data.Value.TryGetInt32(out int itemIndex))
                {
                    _engine.GoToItem(itemIndex);
                    return new RemoteCommandResult { Success = true, StateUpdate = GetCurrentState() };
                }
                return new RemoteCommandResult { Success = false, Error = "Missing item index" };

            default:
                return new RemoteCommandResult { Success = false, Error = $"Unknown action: {action}" };
        }
    }

    /// <summary>
    /// Get the current presenter state formatted for remote clients.
    /// </summary>
    public RemoteStateData GetCurrentState()
    {
        var state = new RemoteStateData
        {
            SlideIndex = _engine.CurrentSlideIndex,
            TotalSlides = _engine.TotalSlides,
            IsLive = _engine.IsLive,
            IsBlacked = _engine.IsBlacked,
            CurrentItemIndex = _engine.CurrentItemIndex,
            CurrentText = _engine.CurrentSlide?.Content?.Text ?? "",
            NextText = _engine.NextSlide?.Content?.Text ?? ""
        };

        // Build items list from service order
        if (_engine.CurrentServiceOrder != null)
        {
            foreach (var item in _engine.CurrentServiceOrder.Items)
            {
                state.Items.Add(new RemoteItemInfo
                {
                    Title = item.Title,
                    Type = item.Type.ToString()
                });
            }
        }

        return state;
    }
}
