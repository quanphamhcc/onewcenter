namespace WorshipPlayer.Presenter.Services;

/// <summary>
/// Contains the embedded HTML/CSS/JS for the remote control page.
/// Served by StageDisplayServer at the /remote endpoint.
/// Mobile-optimized, touch-friendly design for phone/tablet control.
/// </summary>
public static class RemoteHtml
{
    /// <summary>
    /// Returns the complete HTML page for the remote control interface.
    /// </summary>
    /// <param name="port">The WebSocket server port.</param>
    public static string GetHtml(int port)
    {
        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no"">
    <title>Presenter Remote</title>
    <style>
        * {{
            margin: 0;
            padding: 0;
            box-sizing: border-box;
            -webkit-tap-highlight-color: transparent;
        }}
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            background: #1E1E2E;
            color: #CDD6F4;
            min-height: 100vh;
            overflow-x: hidden;
        }}
        .pin-screen {{
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
            min-height: 100vh;
            padding: 20px;
        }}
        .pin-screen h1 {{
            font-size: 1.5rem;
            margin-bottom: 8px;
            color: #CDD6F4;
        }}
        .pin-screen p {{
            font-size: 0.9rem;
            color: #A6ADC8;
            margin-bottom: 24px;
        }}
        .pin-input {{
            font-size: 2rem;
            letter-spacing: 12px;
            text-align: center;
            width: 200px;
            padding: 12px;
            border: 2px solid #45475A;
            border-radius: 8px;
            background: #313244;
            color: #CDD6F4;
            outline: none;
        }}
        .pin-input:focus {{
            border-color: #89B4FA;
        }}
        .pin-submit {{
            margin-top: 20px;
            padding: 14px 48px;
            font-size: 1.1rem;
            font-weight: 600;
            background: #89B4FA;
            color: #1E1E2E;
            border: none;
            border-radius: 8px;
            cursor: pointer;
        }}
        .pin-submit:active {{
            background: #74C7EC;
        }}
        .pin-error {{
            color: #F38BA8;
            margin-top: 12px;
            font-size: 0.9rem;
        }}
        .remote-app {{
            display: none;
            flex-direction: column;
            min-height: 100vh;
        }}
        .header {{
            display: flex;
            align-items: center;
            justify-content: space-between;
            padding: 10px 16px;
            background: #181825;
            border-bottom: 1px solid #313244;
        }}
        .header h1 {{
            font-size: 1rem;
            font-weight: 600;
        }}
        .connection-status {{
            display: flex;
            align-items: center;
            gap: 6px;
            font-size: 0.8rem;
            color: #A6ADC8;
        }}
        .status-dot {{
            width: 8px;
            height: 8px;
            border-radius: 50%;
            background: #F38BA8;
        }}
        .status-dot.connected {{
            background: #A6E3A1;
        }}
        .slide-preview {{
            padding: 16px;
            flex-shrink: 0;
        }}
        .slide-preview .label {{
            font-size: 0.75rem;
            text-transform: uppercase;
            color: #A6ADC8;
            margin-bottom: 4px;
            font-weight: 600;
        }}
        .current-slide {{
            background: #313244;
            border-radius: 8px;
            padding: 14px;
            margin-bottom: 10px;
            min-height: 60px;
            font-size: 0.95rem;
            line-height: 1.4;
            white-space: pre-wrap;
            border-left: 3px solid #89B4FA;
        }}
        .next-slide {{
            background: #1E1E2E;
            border: 1px solid #313244;
            border-radius: 8px;
            padding: 10px;
            min-height: 40px;
            font-size: 0.8rem;
            line-height: 1.3;
            color: #A6ADC8;
            white-space: pre-wrap;
        }}
        .slide-counter {{
            text-align: center;
            font-size: 0.8rem;
            color: #A6ADC8;
            margin-top: 8px;
        }}
        .controls {{
            padding: 16px;
            display: flex;
            flex-direction: column;
            gap: 12px;
        }}
        .nav-row {{
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 10px;
        }}
        .btn {{
            padding: 16px;
            font-size: 1rem;
            font-weight: 600;
            border: none;
            border-radius: 8px;
            cursor: pointer;
            text-align: center;
            transition: transform 0.1s;
        }}
        .btn:active {{
            transform: scale(0.95);
        }}
        .btn-primary {{
            background: #89B4FA;
            color: #1E1E2E;
            padding: 22px 16px;
            font-size: 1.2rem;
        }}
        .btn-secondary {{
            background: #45475A;
            color: #CDD6F4;
        }}
        .btn-live {{
            background: #A6E3A1;
            color: #1E1E2E;
        }}
        .btn-black {{
            background: #F38BA8;
            color: #1E1E2E;
        }}
        .btn-logo {{
            background: #6C7086;
            color: #CDD6F4;
        }}
        .btn-disabled {{
            opacity: 0.4;
            pointer-events: none;
        }}
        .display-row {{
            display: grid;
            grid-template-columns: 1fr 1fr 1fr;
            gap: 10px;
        }}
        .service-order {{
            padding: 0 16px 16px;
            flex: 1;
            overflow-y: auto;
        }}
        .service-order .label {{
            font-size: 0.75rem;
            text-transform: uppercase;
            color: #A6ADC8;
            margin-bottom: 8px;
            font-weight: 600;
        }}
        .service-item {{
            padding: 12px;
            margin-bottom: 6px;
            background: #313244;
            border-radius: 6px;
            cursor: pointer;
            display: flex;
            justify-content: space-between;
            align-items: center;
        }}
        .service-item:active {{
            background: #45475A;
        }}
        .service-item.active {{
            border-left: 3px solid #89B4FA;
            background: #45475A;
        }}
        .service-item .item-title {{
            font-size: 0.9rem;
            font-weight: 500;
        }}
        .service-item .item-type {{
            font-size: 0.7rem;
            color: #A6ADC8;
            text-transform: uppercase;
        }}
        .disconnect-warning {{
            display: none;
            position: fixed;
            top: 0;
            left: 0;
            right: 0;
            background: #F38BA8;
            color: #1E1E2E;
            text-align: center;
            padding: 10px;
            font-weight: 600;
            font-size: 0.9rem;
            z-index: 100;
        }}
        .live-indicator {{
            display: none;
            position: fixed;
            bottom: 0;
            left: 0;
            right: 0;
            text-align: center;
            padding: 6px;
            font-weight: 600;
            font-size: 0.8rem;
            z-index: 100;
        }}
        .live-indicator.is-live {{
            display: block;
            background: #A6E3A1;
            color: #1E1E2E;
        }}
        .live-indicator.is-blacked {{
            display: block;
            background: #F38BA8;
            color: #1E1E2E;
        }}
        @media (orientation: landscape) {{
            .remote-app {{
                flex-direction: row;
                flex-wrap: wrap;
            }}
            .header {{
                width: 100%;
                flex-shrink: 0;
            }}
            .slide-preview {{
                width: 50%;
                padding: 12px;
            }}
            .controls {{
                width: 50%;
                padding: 12px;
            }}
            .service-order {{
                width: 100%;
            }}
        }}
    </style>
</head>
<body>
    <div class=""disconnect-warning"" id=""disconnectWarning"">Connection lost - reconnecting...</div>

    <div class=""pin-screen"" id=""pinScreen"">
        <h1>Presenter Remote</h1>
        <p>Enter the PIN to connect</p>
        <input type=""tel"" class=""pin-input"" id=""pinInput"" maxlength=""8"" autocomplete=""off"" inputmode=""numeric"">
        <button class=""pin-submit"" id=""pinSubmit"">Connect</button>
        <div class=""pin-error"" id=""pinError""></div>
    </div>

    <div class=""remote-app"" id=""remoteApp"">
        <div class=""header"">
            <h1>Presenter Remote</h1>
            <div class=""connection-status"">
                <span class=""status-dot"" id=""statusDot""></span>
                <span id=""statusText"">Connecting...</span>
            </div>
        </div>

        <div class=""slide-preview"">
            <div class=""label"">Current Slide</div>
            <div class=""current-slide"" id=""currentSlide"">--</div>
            <div class=""label"">Next Slide</div>
            <div class=""next-slide"" id=""nextSlide"">--</div>
            <div class=""slide-counter"" id=""slideCounter"">- / -</div>
        </div>

        <div class=""controls"">
            <div class=""nav-row"">
                <button class=""btn btn-primary"" id=""btnPrevSlide"">PREV SLIDE</button>
                <button class=""btn btn-primary"" id=""btnNextSlide"">NEXT SLIDE</button>
            </div>
            <div class=""nav-row"">
                <button class=""btn btn-secondary"" id=""btnPrevItem"">PREV ITEM</button>
                <button class=""btn btn-secondary"" id=""btnNextItem"">NEXT ITEM</button>
            </div>
            <div class=""display-row"">
                <button class=""btn btn-live"" id=""btnGoLive"">LIVE</button>
                <button class=""btn btn-black"" id=""btnGoBlack"">BLACK</button>
                <button class=""btn btn-logo"" id=""btnShowLogo"">LOGO</button>
            </div>
        </div>

        <div class=""service-order"">
            <div class=""label"">Service Order</div>
            <div id=""serviceItems""></div>
        </div>

        <div class=""live-indicator"" id=""liveIndicator""></div>
    </div>

    <script>
        const PORT = {port};
        let ws = null;
        let token = localStorage.getItem('remoteToken');
        let reconnectTimer = null;
        let state = {{}};

        // Show PIN or app based on stored token
        if (token) {{
            showApp();
            connectWs();
        }}

        document.getElementById('pinSubmit').addEventListener('click', doAuth);
        document.getElementById('pinInput').addEventListener('keydown', function(e) {{
            if (e.key === 'Enter') doAuth();
        }});

        function doAuth() {{
            const pin = document.getElementById('pinInput').value;
            if (!pin) return;
            fetch('/remote/auth', {{
                method: 'POST',
                headers: {{ 'Content-Type': 'application/json' }},
                body: JSON.stringify({{ pin: pin }})
            }})
            .then(r => r.json())
            .then(data => {{
                if (data.success) {{
                    token = data.token;
                    localStorage.setItem('remoteToken', token);
                    document.getElementById('pinError').textContent = '';
                    showApp();
                    connectWs();
                }} else {{
                    document.getElementById('pinError').textContent = data.error || 'Invalid PIN';
                }}
            }})
            .catch(() => {{
                document.getElementById('pinError').textContent = 'Connection error';
            }});
        }}

        function showApp() {{
            document.getElementById('pinScreen').style.display = 'none';
            document.getElementById('remoteApp').style.display = 'flex';
        }}

        function showPin() {{
            document.getElementById('pinScreen').style.display = 'flex';
            document.getElementById('remoteApp').style.display = 'none';
            localStorage.removeItem('remoteToken');
            token = null;
        }}

        function connectWs() {{
            if (ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING)) return;

            const host = window.location.hostname;
            ws = new WebSocket('ws://' + host + ':' + PORT + '/ws/remote?token=' + encodeURIComponent(token));

            ws.onopen = function() {{
                document.getElementById('statusDot').classList.add('connected');
                document.getElementById('statusText').textContent = 'Connected';
                document.getElementById('disconnectWarning').style.display = 'none';
                // Send auth message
                ws.send(JSON.stringify({{ type: 'auth', token: token }}));
            }};

            ws.onmessage = function(e) {{
                try {{
                    const msg = JSON.parse(e.data);
                    if (msg.type === 'state') {{
                        updateState(msg);
                    }} else if (msg.type === 'authFailed') {{
                        showPin();
                    }} else if (msg.type === 'result' && !msg.success && msg.error === 'Not authenticated') {{
                        showPin();
                    }}
                }} catch(ex) {{}}
            }};

            ws.onclose = function() {{
                document.getElementById('statusDot').classList.remove('connected');
                document.getElementById('statusText').textContent = 'Disconnected';
                document.getElementById('disconnectWarning').style.display = 'block';
                scheduleReconnect();
            }};

            ws.onerror = function() {{
                ws.close();
            }};
        }}

        function scheduleReconnect() {{
            if (reconnectTimer) clearTimeout(reconnectTimer);
            reconnectTimer = setTimeout(function() {{
                if (token) connectWs();
            }}, 3000);
        }}

        function sendCommand(action, data) {{
            if (!ws || ws.readyState !== WebSocket.OPEN) return;
            const msg = {{ type: 'command', action: action }};
            if (data !== undefined) msg.data = data;
            ws.send(JSON.stringify(msg));
        }}

        function updateState(s) {{
            state = s;
            document.getElementById('currentSlide').textContent = s.currentText || '--';
            document.getElementById('nextSlide').textContent = s.nextText || '--';
            document.getElementById('slideCounter').textContent = (s.slideIndex + 1) + ' / ' + s.totalSlides;

            // Live indicator
            const li = document.getElementById('liveIndicator');
            li.className = 'live-indicator';
            if (s.isBlacked) {{
                li.classList.add('is-blacked');
                li.textContent = 'BLACKED OUT';
            }} else if (s.isLive) {{
                li.classList.add('is-live');
                li.textContent = 'LIVE';
            }}

            // Service items
            const container = document.getElementById('serviceItems');
            container.innerHTML = '';
            if (s.items && s.items.length > 0) {{
                s.items.forEach(function(item, idx) {{
                    const div = document.createElement('div');
                    div.className = 'service-item' + (idx === s.currentItemIndex ? ' active' : '');
                    div.innerHTML = '<span class=""item-title"">' + escapeHtml(item.title) + '</span><span class=""item-type"">' + escapeHtml(item.type) + '</span>';
                    div.addEventListener('click', function() {{
                        sendCommand('goToItem', idx);
                    }});
                    container.appendChild(div);
                }});
            }}
        }}

        function escapeHtml(str) {{
            if (!str) return '';
            return str.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
        }}

        // Button bindings
        document.getElementById('btnNextSlide').addEventListener('click', function() {{ sendCommand('nextSlide'); }});
        document.getElementById('btnPrevSlide').addEventListener('click', function() {{ sendCommand('prevSlide'); }});
        document.getElementById('btnNextItem').addEventListener('click', function() {{ sendCommand('nextItem'); }});
        document.getElementById('btnPrevItem').addEventListener('click', function() {{ sendCommand('prevItem'); }});
        document.getElementById('btnGoLive').addEventListener('click', function() {{ sendCommand('goLive'); }});
        document.getElementById('btnGoBlack').addEventListener('click', function() {{ sendCommand('goBlack'); }});
        document.getElementById('btnShowLogo').addEventListener('click', function() {{ sendCommand('showLogo'); }});
    </script>
</body>
</html>";
    }
}
