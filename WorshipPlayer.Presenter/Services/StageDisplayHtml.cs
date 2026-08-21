namespace WorshipPlayer.Presenter.Services;

/// <summary>
/// Contains the embedded HTML/CSS/JS for the stage display page.
/// Served by StageDisplayServer at the /stage endpoint.
/// Supports URL query parameters for per-musician personalized views:
///   /stage?capo=2&amp;nashville=true&amp;view=twocol&amp;name=Guitar1
/// </summary>
public static class StageDisplayHtml
{
    /// <summary>
    /// Returns the complete HTML page for the stage display.
    /// The page reads URL params and sends them to the WebSocket server for personalized data.
    /// </summary>
    /// <param name="port">The WebSocket server port.</param>
    public static string GetHtml(int port)
    {
        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no"">
    <title>Stage Display</title>
    <style>
        * {{
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }}

        body {{
            background: #1a1a2e;
            color: #ffffff;
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            height: 100vh;
            width: 100vw;
            overflow: hidden;
            display: flex;
            flex-direction: column;
        }}

        #header {{
            display: flex;
            justify-content: space-between;
            align-items: center;
            padding: 1.5vh 2vw;
            background: rgba(0, 0, 0, 0.3);
            border-bottom: 1px solid rgba(255, 255, 255, 0.1);
            min-height: 6vh;
            flex-shrink: 0;
        }}

        #header .title {{
            font-size: 2.2vh;
            font-weight: 600;
            color: #ffffff;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
            max-width: 40%;
        }}

        #header .info {{
            font-size: 1.8vh;
            color: rgba(255, 255, 255, 0.7);
            display: flex;
            gap: 2vw;
            align-items: center;
        }}

        #header .info .section-name {{
            color: #f5a623;
            font-weight: 500;
        }}

        #header .info .slide-counter {{
            color: rgba(255, 255, 255, 0.5);
        }}

        #header .info .musician-name {{
            color: #4caf50;
            font-weight: 500;
        }}

        /* One-column layout (default) */
        #main-content {{
            flex: 1;
            display: flex;
            flex-direction: column;
            justify-content: center;
            padding: 2vh 3vw;
            overflow: hidden;
        }}

        #main-content.twocol {{
            flex-direction: row;
        }}

        #current-text {{
            font-size: 4.5vh;
            font-family: 'Consolas', 'Courier New', monospace;
            white-space: pre-wrap;
            word-wrap: break-word;
            text-align: left;
            flex: 1;
            display: flex;
            align-items: center;
            justify-content: center;
            flex-direction: column;
            padding: 2vh 4vw;
        }}

        #current-text .lyric-block {{
            margin-bottom: 1.5vh;
            width: 100%;
        }}

        #current-text .chord-row {{
            font-family: 'Consolas', 'Courier New', monospace;
            white-space: pre;
            line-height: 1.2;
            color: #7ED321;
            font-weight: 700;
            font-size: 0.8em;
            margin-bottom: 0.1vh;
        }}

        #current-text .chord-row.dim {{
            color: #4A4A60;
            font-weight: 400;
            font-size: 0.7em;
        }}

        #current-text .lyric-row {{
            font-family: 'Consolas', 'Courier New', monospace;
            line-height: 1.3;
            color: #ffffff;
            font-size: 1em;
        }}
        }}

        /* Two-column: current section on left */
        .twocol #current-text {{
            text-align: left;
            align-items: flex-start;
            justify-content: flex-start;
            padding-right: 2vw;
            border-right: 1px solid rgba(255, 255, 255, 0.1);
            font-size: 3.8vh;
        }}

        /* Two-column: next section on right */
        .twocol #next-inline {{
            flex: 1;
            display: flex;
            align-items: flex-start;
            justify-content: flex-start;
            padding-left: 2vw;
            font-size: 3vh;
            line-height: 1.5;
            color: rgba(255, 255, 255, 0.5);
            white-space: pre-wrap;
            word-wrap: break-word;
        }}

        .twocol #next-inline .chord {{
            color: rgba(245, 166, 35, 0.5);
            font-weight: 700;
            font-size: 0.75em;
        }}

        .twocol #next-inline .lyric-line {{
            margin-bottom: 0.3vh;
        }}

        .twocol #next-inline .chord-row {{
            font-family: 'Courier New', monospace;
            white-space: pre;
            line-height: 1.1;
            min-height: 2vh;
        }}

        .twocol #next-inline .chord-space {{
            font-family: 'Courier New', monospace;
        }}

        .twocol #next-inline .lyric-row {{
            line-height: 1.3;
        }}

        #current-text .chord {{
            color: #f5a623;
            font-weight: 700;
            font-size: 0.75em;
        }}

        #current-text .lyric-line {{
            margin-bottom: 0.5vh;
        }}

        #current-text .chord-row {{
            font-family: 'Courier New', monospace;
            white-space: pre;
            line-height: 1.2;
            min-height: 2.5vh;
        }}

        #current-text .chord-space {{
            font-family: 'Courier New', monospace;
        }}

        #current-text .lyric-row {{
            line-height: 1.4;
        }}

        /* One-column next preview (bottom panel) */
        #next-preview {{
            padding: 2vh 3vw;
            border-top: 1px solid rgba(255, 255, 255, 0.1);
            background: rgba(0, 0, 0, 0.2);
            min-height: 12vh;
            flex-shrink: 0;
        }}

        #next-preview .label {{
            font-size: 1.5vh;
            color: rgba(255, 255, 255, 0.4);
            text-transform: uppercase;
            letter-spacing: 0.1em;
            margin-bottom: 0.5vh;
        }}

        #next-text {{
            font-size: 2.5vh;
            line-height: 1.4;
            color: rgba(255, 255, 255, 0.5);
            white-space: pre-wrap;
            word-wrap: break-word;
            text-align: center;
        }}

        #next-text .chord {{
            color: rgba(245, 166, 35, 0.5);
            font-weight: 700;
            font-size: 0.75em;
        }}

        #next-text .lyric-line {{
            margin-bottom: 0.3vh;
        }}

        #next-text .chord-row {{
            font-family: 'Courier New', monospace;
            white-space: pre;
            line-height: 1.1;
            min-height: 2vh;
        }}

        #next-text .chord-space {{
            font-family: 'Courier New', monospace;
        }}

        #next-text .lyric-row {{
            line-height: 1.3;
        }}

        /* Hide bottom next-preview in two-column mode */
        body.twocol-mode #next-preview {{
            display: none;
        }}

        /* Show inline next panel only in two-column mode */
        #next-inline {{
            display: none;
        }}

        body.twocol-mode #next-inline {{
            display: flex;
        }}

        #overlay {{
            position: fixed;
            top: 0;
            left: 0;
            width: 100%;
            height: 100%;
            display: none;
            justify-content: center;
            align-items: center;
            z-index: 1000;
            background: #1a1a2e;
        }}

        #overlay.visible {{
            display: flex;
        }}

        #overlay .message {{
            font-size: 5vh;
            font-weight: 700;
            letter-spacing: 0.2em;
            text-transform: uppercase;
        }}

        #overlay.blacked .message {{
            color: #ff4444;
        }}

        #overlay.standby .message {{
            color: #888888;
        }}

        #connection-status {{
            position: fixed;
            top: 1vh;
            right: 1vw;
            font-size: 1.5vh;
            padding: 0.3vh 0.8vw;
            border-radius: 3px;
            z-index: 1001;
            transition: opacity 0.3s;
        }}

        #connection-status.connected {{
            background: rgba(76, 175, 80, 0.3);
            color: #4caf50;
        }}

        #connection-status.disconnected {{
            background: rgba(244, 67, 54, 0.3);
            color: #f44336;
        }}
    </style>
</head>
<body>
    <div id=""connection-status"" class=""disconnected"">DISCONNECTED</div>

    <div id=""overlay"">
        <div class=""message""></div>
    </div>

    <div id=""header"">
        <div class=""title"" id=""song-title""></div>
        <div class=""info"">
            <span class=""musician-name"" id=""musician-name""></span>
            <span class=""section-name"" id=""section-name""></span>
            <span id=""key-info""></span>
            <span id=""tempo-info""></span>
            <span class=""slide-counter"" id=""slide-counter""></span>
        </div>
    </div>

    <div id=""main-content"">
        <div id=""current-text""></div>
        <div id=""next-inline""></div>
    </div>

    <div id=""next-preview"">
        <div class=""label"">Next</div>
        <div id=""next-text""></div>
    </div>

    <script>
        let ws = null;
        let reconnectTimer = null;
        const WS_PORT = {port};

        // Parse URL parameters for musician preferences
        const urlParams = new URLSearchParams(window.location.search);
        const clientConfig = {{
            capo: parseInt(urlParams.get('capo') || '0', 10),
            nashville: urlParams.get('nashville') === 'true',
            view: urlParams.get('view') || 'onecol',
            name: urlParams.get('name') || ''
        }};

        // Apply layout mode
        if (clientConfig.view === 'twocol') {{
            document.body.classList.add('twocol-mode');
            document.getElementById('main-content').classList.add('twocol');
        }}

        // Display musician name in header if provided
        if (clientConfig.name) {{
            document.getElementById('musician-name').textContent = clientConfig.name;
        }}

        function connect() {{
            const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
            const host = window.location.hostname || 'localhost';

            // Pass preferences as query params on the WebSocket URL
            const wsParams = new URLSearchParams();
            if (clientConfig.capo > 0) wsParams.set('capo', clientConfig.capo);
            if (clientConfig.nashville) wsParams.set('nashville', 'true');
            if (clientConfig.view) wsParams.set('view', clientConfig.view);
            if (clientConfig.name) wsParams.set('name', clientConfig.name);

            const paramStr = wsParams.toString();
            const url = protocol + '//' + host + ':' + WS_PORT + '/ws/stage' + (paramStr ? '?' + paramStr : '');

            try {{
                ws = new WebSocket(url);
            }} catch (e) {{
                scheduleReconnect();
                return;
            }}

            ws.onopen = function() {{
                const status = document.getElementById('connection-status');
                status.className = 'connected';
                status.textContent = 'CONNECTED';
                // Fade out after 3 seconds
                setTimeout(() => {{ status.style.opacity = '0'; }}, 3000);

                // Send config message to confirm preferences
                ws.send(JSON.stringify({{
                    type: 'config',
                    capo: clientConfig.capo,
                    nashville: clientConfig.nashville,
                    view: clientConfig.view,
                    name: clientConfig.name
                }}));
            }};

            ws.onclose = function() {{
                showDisconnected();
                scheduleReconnect();
            }};

            ws.onerror = function() {{
                showDisconnected();
                ws.close();
            }};

            ws.onmessage = function(event) {{
                try {{
                    const data = JSON.parse(event.data);
                    updateDisplay(data);
                }} catch (e) {{
                    console.error('Failed to parse message:', e);
                }}
            }};
        }}

        function showDisconnected() {{
            const status = document.getElementById('connection-status');
            status.className = 'disconnected';
            status.textContent = 'DISCONNECTED';
            status.style.opacity = '1';
        }}

        function scheduleReconnect() {{
            if (reconnectTimer) clearTimeout(reconnectTimer);
            reconnectTimer = setTimeout(connect, 5000);
        }}

        function formatTextWithChords(text) {{
            if (!text) return '';
            // Replicate the EXACT same rendering as ChordChartWindow.BuildFromInlineChords()
            // 1. For each line: extract [chord] positions relative to stripped lyric text
            // 2. Build a chord line using spaces to position chords at correct char offsets
            // 3. Render chord line (green, monospace) above lyric line (white, monospace)
            const lines = text.split('\n');
            let html = '';
            let lastChord = '';

            for (const line of lines) {{
                if (!line.trim()) continue; // skip empty lines within a slide

                // Extract chords and build stripped lyric + chord positions
                const chordRegex = /\[([^\]]+)\]/g;
                let match;
                const chords = [];
                while ((match = chordRegex.exec(line)) !== null) {{
                    chords.push({{ index: match.index, chord: match[1], len: match[0].length }});
                }}

                // Build stripped lyric text and calculate chord positions in stripped text
                let lyric = '';
                let lastEnd = 0;
                const chordPositions = [];
                for (const c of chords) {{
                    lyric += line.substring(lastEnd, c.index);
                    chordPositions.push({{ pos: lyric.length, chord: c.chord }});
                    lastEnd = c.index + c.len;
                }}
                lyric += line.substring(lastEnd);

                // Build chord line: fill with spaces, place chords at correct offsets
                // (same as ChordChartWindow.BuildChordLine using char array)
                let chordLineHtml = '';
                if (chordPositions.length > 0) {{
                    const lineLength = Math.max(lyric.length + 10, 80);
                    const chordChars = new Array(lineLength).fill(' ');

                    for (const cp of chordPositions) {{
                        const offset = Math.min(Math.max(cp.pos, 0), lineLength - cp.chord.length - 1);
                        for (let i = 0; i < cp.chord.length && offset + i < lineLength; i++) {{
                            chordChars[offset + i] = cp.chord[i];
                        }}
                        lastChord = cp.chord;
                    }}

                    // Trim trailing spaces and wrap in colored span
                    const chordStr = chordChars.join('').trimEnd();
                    chordLineHtml = '<div class=""chord-row"">' + escapeHtml(chordStr) + '</div>';
                }} else if (lastChord) {{
                    // No chord on this line - show carried-over chord (dim)
                    chordLineHtml = '<div class=""chord-row dim"">(' + escapeHtml(lastChord) + ')</div>';
                }}

                html += '<div class=""lyric-block"">';
                html += chordLineHtml;
                html += '<div class=""lyric-row"">' + escapeHtml(lyric) + '</div>';
                html += '</div>';
            }}
            return html;
        }}

        function escapeHtml(text) {{
            const div = document.createElement('div');
            div.textContent = text;
            return div.innerHTML;
        }}

        function updateDisplay(data) {{
            const overlay = document.getElementById('overlay');

            // Handle blacked state
            if (data.isBlacked) {{
                overlay.className = 'visible blacked';
                overlay.querySelector('.message').textContent = 'BLACKED';
                return;
            }}

            // Handle standby (not live)
            if (!data.isLive) {{
                overlay.className = 'visible standby';
                overlay.querySelector('.message').textContent = 'STANDBY';
                return;
            }}

            // Normal display
            overlay.className = '';

            // Update header
            document.getElementById('song-title').textContent = data.songTitle || '';
            document.getElementById('section-name').textContent = data.sectionName || '';

            let keyText = '';
            if (data.key) {{
                keyText = 'Key: ' + data.key;
                if (data.pitchShift !== 0) {{
                    keyText += ' (' + (data.pitchShift > 0 ? '+' : '') + data.pitchShift + ')';
                }}
                if (data.capo > 0) {{
                    keyText += ' | Capo: ' + data.capo;
                }}
            }}
            document.getElementById('key-info').textContent = keyText;

            let tempoText = '';
            if (data.tempoPercent && data.tempoPercent !== 100) {{
                tempoText = 'Tempo: ' + Math.round(data.tempoPercent) + '%';
            }}
            document.getElementById('tempo-info').textContent = tempoText;

            // Slide counter
            if (data.totalSlides > 0) {{
                document.getElementById('slide-counter').textContent =
                    (data.currentSlideIndex + 1) + ' / ' + data.totalSlides;
            }} else {{
                document.getElementById('slide-counter').textContent = '';
            }}

            // Update current text
            document.getElementById('current-text').innerHTML = formatTextWithChords(data.currentText);

            // Update next text (both locations for one-column and two-column)
            document.getElementById('next-text').innerHTML = formatTextWithChords(data.nextText);
            document.getElementById('next-inline').innerHTML = formatTextWithChords(data.nextText);
        }}

        // Initial connection
        connect();
    </script>
</body>
</html>";
    }
}
