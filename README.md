# Worship Backtrack Player

A professional-grade multitrack worship backtrack player for Windows with WASAPI exclusive mode audio, real-time tempo/pitch changes, beat-accurate section looping, and MaestroDMX lighting integration via OSC.

## Features

### Audio Engine
- **WASAPI Exclusive Mode** (~3-5ms latency)
- **8+ simultaneous tracks** with individual volume, solo, mute
- **Real-time tempo change** (50%-150%) via SoundTouch
- **Real-time pitch shift** (up to 6 semitones up/down)
- **Pan-split output** (Left=FOH, Right=IEM/Click)
- **Beat-grid-snapped loop points** derived from click track analysis

### Section Management
- **Millisecond-precision section markers** (loop in/out)
- **Seamless looping** (infinite or 1x)
- **Section jumping** with configurable timing
- **Custom arrangements** (drag-and-drop reorder)
- **Auto-detection** from click track (when available)
- **Input modes**: Milliseconds, Timecode (M:SS.mmm), or Bar number

### Song & Setlist Management
- **Multi-song support** — add unlimited songs
- **JSON-based configuration** (easy to edit)
- **Auto-detect stems** from folder
- **Smart track labeling** (detects Click, Drums, Bass, Guitar, etc.)
- **Setlists** with transitions (Crossfade, Overlap, Auto-Advance)
- **Per-song key and tempo overrides**

### Lighting Control (MaestroDMX)
- **OSC output** (primary — for MaestroDMX via Ethernet)
- **MIDI output** (fallback — for other controllers)
- **Auto-generated lighting presets** per section
- **Per-section palette, intensity, excitement mapping**
- **Manual overrides** (Blackout, Flash, Full Bright)
- **Live BPM sync** to MaestroDMX

### User Interface
- **Dark theme** optimized for live performance
- **Touch-friendly buttons** (large targets)
- **Keyboard shortcuts** for hands-free operation
- **Visual section highlighting** with progress
- **Real-time position display** (timecode, bar/beat)

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `SPACE` | Play / Pause |
| `ESC` | Stop |
| `L` | Toggle Loop (Infinite → 1x → Off) |
| `→` | Next Section |
| `←` | Previous Section |
| `1-9` | Jump to Section 1-9 |
| `B` | Blackout (lights) |

## Getting Started

### Prerequisites
- Windows 10/11 (64-bit)
- .NET 8.0 SDK ([Download](https://dotnet.microsoft.com/download/dotnet/8.0))
- Visual Studio 2022 (recommended) or VS Code with C# extension

### Build & Run

```powershell
# Clone this repository
git clone <your-repo-url>
cd WorshipBacktrackPlayer

# Build
dotnet build

# Run
dotnet run --project WorshipPlayer.UI
```

### Or open in Visual Studio:
1. Open `WorshipBacktrackPlayer.sln`
2. Set `WorshipPlayer.UI` as startup project
3. Press F5 to run

### Publish as standalone .exe:
```powershell
dotnet publish WorshipPlayer.UI -c Release -r win-x64 --self-contained -o ./publish
```

## Adding Songs

### Quick Method:
1. Click **"+ Add Song"** in the app
2. Select a folder containing your audio stems (MP3 or WAV)
3. The app auto-detects tracks and creates a `song.json`
4. Edit sections using the **Section Editor**

### Manual Method:
1. Create a folder in `Songs/` with your song name
2. Copy your audio stems into it
3. Create a `song.json` (see `Songs/Open The Heavens/song.json` as example)
4. Define your sections with precise loop points

### song.json Structure:
```json
{
  "title": "Your Song Title",
  "bpm": 120.0,
  "timeSignatureNumerator": 4,
  "timeSignatureDenominator": 4,
  "beatInterval_ms": 500.0,
  "barDuration_ms": 2000.0,
  "gridAnchor_ms": 0.0,
  "originalKey": "G",
  "tracks": [
    { "file": "Click.mp3", "label": "Click", "output": "IEM" },
    { "file": "Drums.mp3", "label": "Drums", "output": "FOH" }
  ],
  "sections": [
    { "name": "INTRO", "loopIn_ms": 0.0, "loopOut_ms": 8000.0, "startBar": 1, "endBar": 4 }
  ]
}
```

## MaestroDMX Setup

### Connection:
1. Connect an Ethernet cable from your PC to MaestroDMX
2. Set MaestroDMX IP address in Lighting settings
3. Port default: 8000

### How it works:
- Audio feeds into MaestroDMX for real-time music analysis (beats, dynamics)
- Our app sends OSC commands at each section change (palette, intensity, excitement)
- MaestroDMX handles moment-to-moment lighting decisions
- Our app handles section-level mood/color decisions

## Output Routing

### Pan-Split Mode (Default):
- **Left channel** = Full mix (FOH/speakers)
- **Right channel** = Click + cues (IEM)
- Use a Y-splitter cable or audio interface

### Multi-Output Mode (with audio interface):
- Outputs 1-2: Full mix → FOH
- Outputs 3-4: Click → IEM

## Project Structure

```
WorshipBacktrackPlayer/
├── WorshipPlayer.Core/          # Data models, services, business logic
│   ├── Models/                   # Song, Section, Track, Settings, Setlist
│   └── Services/                 # SongManager, Settings, OSC, MIDI, Lighting
├── WorshipPlayer.Audio/         # Audio engine (Windows-specific)
│   ├── Engine/                   # AudioEngine, MultiTrackMixer
│   └── DSP/                      # TrackPlayer (SoundTouch integration)
├── WorshipPlayer.UI/            # WPF User Interface
│   ├── Views/                    # MainWindow, SectionEditor, LightingWindow
│   ├── ViewModels/               # (future: MVVM pattern)
│   └── Controls/                 # (future: custom WPF controls)
├── Songs/                        # Song folders with stems + song.json
└── Setlists/                     # Setlist JSON files
```

## Technology Stack

| Component | Technology |
|-----------|-----------|
| UI Framework | WPF (.NET 8) |
| Audio Output | NAudio + WASAPI |
| Tempo/Pitch DSP | SoundTouch.Net |
| Lighting (primary) | OSC over UDP (Ethernet) |
| Lighting (fallback) | MIDI via NAudio.Midi |
| Data Storage | JSON files (System.Text.Json) |
| Target Platform | Windows 10/11 x64 |

## License

This is a personal project for worship use. Feel free to use and modify.
