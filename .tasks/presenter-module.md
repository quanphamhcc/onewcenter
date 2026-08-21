# OWC Lyric Presenter Module — Task List

## Status: In Progress (Month 1)
**Branch:** `feature/lyric-presenter`
**Foundation committed:** Core models + basic engine (PR #2)

---

## Architecture Decisions (CONFIRMED)

- [x] Hybrid architecture (Desktop C#/WPF + Cloud SaaS)
- [x] Option B repo strategy (Presenter in same repo as Backtrack)
- [x] ServiceOrder is shared model in Core (single source of truth for service flow)
- [x] Backtrack is the conductor (timing, sections, loops, pitch)
- [x] Presenter follows backtrack events (display only)
- [x] Lyrics source: `song.json → sections[].text` (with inline [chords] and {timestamps})
- [x] Cloud Song Library is the master source — downloaded locally for offline Sunday execution
- [x] Stage display: ALWAYS shows chords (transposed)
- [x] Congregation output: CONFIGURABLE show/hide chords
- [x] Download options: Lyrics only, Lyrics + Stems, Lyrics only (no chords)
- [x] Song library: Private per-church + shared community marketplace
- [x] Lyrics authoring: Manual, CCLI/SongSelect import, ChordPro import
- [x] Stage display delivery: WPF window (local monitor) + Network/web (iPad/tablet)

### Features Selected (from iSingWorship + WorshipTools analysis)

- [x] Capo position display (guitarists see capo-adjusted chords)
- [x] Motion backgrounds (looping video behind lyrics)
- [x] Video playback in presentation (countdown, welcome, sermon clips)
- [x] Presenter Remote (phone/tablet controls via browser)
- [x] Nashville number charts (1-4-5-6m notation option)
- [x] One column / Two column chart views (musician preference)
- [x] AirPlay / Wireless display (Miracast, NDI, AirPlay)

---

## TASK LIST

### Phase 1: Core Engine (Update from current foundation)

- [ ] **1.1** Rework `PresenterEngine` to be section-aware
  - Read lyrics directly from `Song.Sections[].Text` (not pre-generated slides)
  - Use `ChartSlide.StartMs/EndMs` for auto-advance timing
  - Handle section loops (reset slide index when section loops)
  - Handle section jumps (jump to correct lyrics instantly)
  - Handle queued sections

- [ ] **1.2** Add backtrack event integration
  - `SectionChanged` → jump to section's first slide
  - `SectionLooped` → reset to section start (don't advance to next section)
  - `PitchShiftChanged` → transpose all displayed chords via `ChordProParserService.Transpose()`
  - `TempoChanged` → update display (stage shows current tempo)
  - `SongEnded` → advance to next ServiceItem
  - `SongLoaded` → load new song's lyrics/sections
  - `PositionChanged` → check ChartSlide timing for auto-advance

- [ ] **1.3** Dual rendering pipeline
  - Congregation output: Strip chords (`ChordProParserService.StripChords()`) when setting is OFF
  - Stage display: Always show chords (transposed), section info, key, tempo, next section preview
  - Both read from same source, different rendering

- [ ] **1.4** Handle songs WITHOUT backtrack (manual advance)
  - `ServiceItem.UseBacktrack = false` → no auto-advance, manual only
  - Lyrics still come from `song.json` (downloaded from cloud, just no stems)
  - Keyboard/foot pedal advances slides
  - Foot pedal via MIDI input (already have `MidiInputEnabled` in settings)

### Phase 2: Stage Display

- [ ] **2.1** Stage Display WPF window
  - Shows: current lyrics + chords (transposed), next section preview, current key, tempo
  - Font size configurable (musicians need large text at a distance)
  - Dark theme (stage lighting environment)
  - Place on separate monitor via DisplayManager

- [ ] **2.2** Stage Display network/web version
  - Lightweight web server embedded in desktop app (Kestrel or minimal HTTP)
  - Serves stage display as HTML/CSS/JS page
  - iPads/tablets on music stands connect via browser (`http://192.168.x.x:5050/stage`)
  - Real-time updates via WebSocket or Server-Sent Events (SSE)
  - Shows same content as WPF stage window
  - Configurable per-musician view (e.g., guitarist sees guitar chords, keys player sees Nashville numbers — FUTURE)

### Phase 3: Congregation Output Window

- [ ] **3.1** Update OutputWindow rendering
  - Double-buffer crossfade transitions (already built)
  - Chord display toggle (strip chords when OFF)
  - Text shadow / drop shadow for readability over backgrounds
  - Viewbox auto-scaling for different text lengths
  - Background image/color from theme

- [ ] **3.2** Transitions
  - Cut (instant)
  - Fade (crossfade opacity)
  - Dissolve (pixel-level blend)
  - Configurable duration per slide or per theme

- [ ] **3.3** Theme system
  - Background: solid color, image, gradient
  - Fonts: family, size, weight, color, shadow
  - Layout: alignment, margins, line spacing
  - Logo watermark (configurable position, opacity)
  - CCLI footer
  - Multiple saved themes (worship theme, sermon theme, announcement theme)

### Phase 4: Operator Window

- [ ] **4.1** Service Order panel (left)
  - Lists all ServiceItems with icons (♪ Song, 📖 Scripture, etc.)
  - Click to jump to any item
  - Drag-and-drop reorder (future)
  - Current item highlighted

- [ ] **4.2** Live/Next preview (center)
  - Shows current slide as it appears on output (WYSIWYG preview)
  - Shows next slide preview
  - Slide thumbnails strip (all slides for current item)
  - Click any thumbnail to jump to that slide

- [ ] **4.3** Backtrack status panel (right)
  - Connection status (linked to backtrack)
  - Current song / section / position
  - Auto-advance status (MIDI: ON/OFF, Timestamp: ON/OFF)
  - Loop indicator
  - Current key / tempo display

- [ ] **4.4** Transport controls
  - Next/Prev slide buttons
  - Next/Prev item buttons
  - GO LIVE / BLACK / LOGO buttons
  - Keyboard shortcuts (Right=next, Left=prev, B=black, Enter=live, Esc=black)

### Phase 5: Integration with Backtrack MainWindow

- [ ] **5.1** Add "Presenter" button to MainWindow (like existing Chord Chart, Lighting, MIDI Cues buttons)
- [ ] **5.2** Wire up backtrack events → MidiPresenterSync → PresenterEngine
- [ ] **5.3** Shared song loading (when backtrack loads a song, presenter loads its lyrics)
- [ ] **5.4** ServiceOrder integration (when ServiceOrder advances to a Song item, backtrack loads that song)
- [ ] **5.5** Handle the "conductor" question: ServiceOrder drives the flow, Backtrack handles song audio, Presenter handles all display

### Phase 6: Capo Display & Nashville Numbers

- [ ] **6.1** Capo position setting per song/service item
  - `Song` model: add `capo` field (0 = no capo, 1-9 = fret)
  - `ServiceItem` model: add `capoOverride` for per-service key adjustment
  - Calculate capo-adjusted chords (e.g., song in D with Capo 2 → show C shapes)

- [ ] **6.2** Nashville Number chart display
  - Convert chords to Nashville numbers (C=1, D=2, E=3, F=4, G=5, A=6, B=7 in key of C)
  - Respects current key (if key is G: G=1, A=2, B=3, C=4, D=5, Em=6m)
  - Toggle between: Standard chords / Nashville numbers / Capo-adjusted chords
  - Per-musician preference (stage display setting)

- [ ] **6.3** One-column / Two-column chart views
  - Stage display layout options:
    - One column: full-width lyrics + chords (large text, easy to read at distance)
    - Two column: compact layout (fits more content, less scrolling)
  - Auto-fit text size based on content length and column mode
  - Configurable per-musician in network stage display settings

- [ ] **6.4** Stage display per-musician view (via network)
  - Guitarist: sees capo-adjusted chords
  - Keys player: sees Nashville numbers or standard chords
  - Vocalist: sees lyrics only (no chords)
  - Each connected device can choose its own view mode
  - URL parameter: `http://192.168.x.x:5050/stage?view=guitar&capo=2`

### Phase 7: Motion Backgrounds & Video Playback

- [ ] **7.1** Motion backgrounds (looping video behind lyrics)
  - Support formats: MP4, WebM, MOV (H.264/H.265)
  - Seamless loop (no visible restart glitch)
  - Opacity/dim control (darken video for text readability)
  - Per-theme or per-slide background assignment
  - Library of motion backgrounds (bundled + user-imported)
  - Smooth transition between different motion backgrounds on slide change

- [ ] **7.2** Video playback as service item
  - Play video files during service (countdown timers, welcome videos, sermon clips, announcements)
  - ServiceItemType.Video → full-screen video playback on congregation output
  - Transport controls: play, pause, seek, volume
  - Auto-advance to next service item when video ends
  - Supported formats: MP4, AVI, MOV, WMV
  - Overlay option: show text (title/lower third) on top of video

- [ ] **7.3** Video engine implementation
  - WPF MediaElement or LibVLCSharp for reliable video playback
  - Hardware-accelerated decoding (GPU)
  - Separate from audio engine (video goes to output window, audio goes to system default or configured device)
  - Pre-buffer next video while current item is playing

### Phase 8: Presenter Remote (Phone/Tablet Control)

- [ ] **8.1** Embedded web server for remote control
  - Extend the existing Kestrel server (port 5050) with remote control endpoints
  - Web app served at `http://192.168.x.x:5050/remote`
  - Responsive design (works on phone, tablet, laptop)
  - No app install required — just a browser

- [ ] **8.2** Remote control features
  - View current + next slide (live preview)
  - Next/Prev slide buttons (large, touch-friendly)
  - Next/Prev service item
  - Go Live / Go Black / Show Logo
  - View full service order (tap to jump to any item)
  - Timer display (elapsed time for current item)
  - Notes display (per-item notes from ServiceOrder)

- [ ] **8.3** Remote control security
  - PIN-based authentication (4-6 digit code displayed on operator window)
  - Optional: only allow devices on same subnet
  - Session management (see connected remotes, kick if needed)
  - Read-only mode option (view only, no control — for pastors monitoring)

- [ ] **8.4** Multi-remote support
  - Multiple phones/tablets can connect simultaneously
  - Worship leader controls from stage (phone in pocket)
  - Pastor controls during sermon (advances sermon points)
  - Tech booth as primary operator (full control)
  - All remotes stay in sync (real-time via WebSocket)

### Phase 9: AirPlay / Wireless Display

- [ ] **9.1** Research & choose wireless display protocol
  - Options: AirPlay (Apple TV), Miracast (Windows built-in), Chromecast, DLNA
  - AirPlay: best for Apple TV-connected projectors — need AirPlay sender in C#
  - Miracast: Windows-native, works with many smart TVs/wireless dongles
  - NDI (Network Device Interface): professional AV, low-latency over Ethernet

- [ ] **9.2** Implement wireless output
  - Send congregation output window content to wireless display
  - Maintain a local preview even when outputting wirelessly
  - Handle latency (wireless adds 50-200ms — may need to account for audio sync)
  - Fallback: always support wired HDMI/DisplayPort as primary method

- [ ] **9.3** NDI output (streaming/professional AV)
  - Output congregation display as NDI stream on the network
  - Any NDI-capable device can receive it (OBS, vMix, video switchers, other PCs)
  - Low latency (~1 frame)
  - Useful for: livestream overlays, lobby displays, overflow rooms, recording
  - Library: NDI SDK for .NET (Newtek/Vizrt)

### Phase 10: Cloud Sync (Month 2 — separate from this branch)

- [ ] **10.1** Cloud Song Library API (ASP.NET Core)
  - Upload song (metadata, lyrics, stems)
  - Download song (lyrics only, or lyrics + stems)
  - Search/browse songs
  - CCLI/SongSelect import endpoint

- [ ] **10.2** Desktop sync service
  - "Download for Sunday" workflow
  - Background download of stems (large files)
  - Incremental sync (only download changed songs)
  - Offline cache management

- [ ] **10.3** Community marketplace (future)
  - Share arrangements between churches
  - Rate/review shared songs
  - License-aware sharing (CCLI compliance)

### Phase 11: Operator Window Redesign (WorshipTools-Inspired UI)

- [ ] **11.1** Slide Grid View (main workspace)
  - Visual slide thumbnails in a grid layout (not a text list)
  - Each thumbnail shows: background image + lyrics text preview + slide label (Verse 1, Chorus, etc.)
  - Slide number in bottom-right corner of each thumbnail
  - Active/live slide has GREEN border
  - Click a slide to make it live (send to output)
  - Zoom slider to control thumbnail size:
    - Zoom out: 5-6 slides per row (overview)
    - Zoom in: 2-3 slides per row (detail)
    - Grid auto-reflows to fit screen width
    - Remember last zoom level in settings

- [ ] **11.2** Top Navigation Tabs
  - **Services** tab: Service order builder/editor (current service)
  - **Songs** tab: Song library browser (search, import, add to service)
  - **Scripture** tab: Built-in Bible browser (type reference → auto-fetch text)
  - **Media** tab: Media library (backgrounds, videos, images)
  - **Slides** tab: Quick slide builder (blank, text, image)
  - Tab icons + labels, highlighted when active

- [ ] **11.3** Left Sidebar — Service Order (tree/list)
  - Service items listed vertically
  - Collapsible groups with custom labels (e.g., "Opening Worship", "Message", "Response")
  - User can create/rename/delete groups
  - Drag-and-drop items between groups or reorder within
  - Icons per item type (♪ Song, 📖 Scripture, 📢 Announcement, etc.)
  - Currently active item highlighted (green or accent color)
  - Click item → slide grid shows that item's slides
  - Right-click context menu (edit, delete, move up/down, duplicate)
  - "Add Item" button at bottom

- [ ] **11.4** Live Output Preview Boxes (top of main area)
  - **Left box: Congregation/Audience Live Preview**
    - Real-time mini-monitor showing exactly what's on the projector
    - Updates instantly when slide changes
    - Shows "(BLACK)" overlay when blacked out
    - Click to open larger pop-out preview window
  - **Right box: Stage Display Live Preview**
    - Real-time mini-monitor showing what musicians see (chords + lyrics)
    - Updates with transposed chords, section name, key, tempo
    - Click to open larger pop-out preview window
  - Both boxes always visible at top of workspace
  - Compact size (~200x120px each) so they don't take too much space

- [ ] **11.5** Edit Mode Toggle
  - "Edit" button (top-right, toggleable green/gray)
  - When EDIT mode ON:
    - Slide thumbnails show edit icon (pencil) and delete icon (X) on hover
    - Click pencil → inline lyrics editor opens (edit text directly)
    - Click X → delete slide (with confirmation)
    - Drag-and-drop slides to reorder within the song
    - Can change background per slide (right-click or edit dialog)
    - "Add Slide" button visible (insert blank, lyrics, scripture, image slide)
  - When EDIT mode OFF (performance mode):
    - Clean view, no edit controls
    - Click a slide → sends it live immediately
    - Optimized for fast operation during service

- [ ] **11.6** Built-in Scripture Browser
  - Scripture tab with search box: type "John 3:16" or "Romans 8:28-30"
  - Auto-parses book, chapter, verse range
  - Multiple translations available: NIV, KJV, ESV, NLT, NKJV, AMP, MSG, WEB, NASB
  - Online API (api.bible or similar) for fetching text
  - Local offline Bible database (SQLite with common translations) as fallback
  - Preview the scripture text before inserting
  - "Add to Service" button → creates a Scripture service item with auto-generated slides
  - Auto-splits long passages into multiple slides (configurable lines per slide)
  - Copyright notice auto-added per translation requirements

- [ ] **11.7** Media Library
  - Media tab showing all available media assets
  - **Sub-tabs:** Your Media Library | Free Stills | Notices | Templates
  - **Toolbar:** Import Media (button + dropdown) | View modes (Grid / List / Compact) | Hide/Show Filters | Sort By (dropdown)
  - **Search box** (top-right, searches by name)
  - **Left Filter Panel:**
    - Name: text search field + "Reset Filters" link
    - Type checkboxes: Image, Audio, Video, PowerPoint, Keynote
    - Include community/shared media checkbox
    - Tags/Categories filter: list of tags (blue, christmas, dark, lines, red, ripple, smoke, water, worship, etc.)
    - Cloud Status filter: Any, Download Queue, Downloaded, Download Failed, Upload Queue, Uploading, Upload Failed
  - **Main Content Grid:**
    - Thumbnail previews with colored label bar at bottom (showing name)
    - Grid view: large thumbnails in rows
    - List view: smaller thumbnails with metadata (name, type, size, date)
    - Compact view: text list with small icons
  - Import from file system (images, videos, audio, PowerPoint)
  - Drag media onto a slide thumbnail to set as background
  - Cloud sync status per item (uploaded/downloading/failed)
  - Store in configurable MediaLibrary folder path
  - Preview on hover or click

- [ ] **11.8** Song Library Browser
  - Songs tab showing all songs in the local library
  - Search by title, artist, key, or lyrics content
  - Preview lyrics before adding to service
  - "Add to Service" button → adds song as ServiceItem
  - Import from CCLI SongSelect (with API key — future)
  - Import from ChordPro files
  - Shows: title, artist, key, last used date, CCLI number
  - Sort by: title, recently used, key, artist

- [ ] **11.9** Inline Slide Editor
  - When edit icon is clicked on a slide thumbnail:
    - Opens an editor panel/dialog
    - Edit lyrics text (multi-line text box)
    - Edit slide label (Verse 1, Chorus, etc.)
    - Change background (pick from media library or solid color)
    - Set transition type and duration
    - Preview changes live before saving
    - Apply / Cancel buttons
  - For Scripture slides: edit reference and translation
  - For Announcement slides: edit text and optional image

- [ ] **11.9b** Template Editor (WYSIWYG)
  - Full template editing window with live preview
  - Template name (editable at top)
  - **3 tabs:**
    - **Formatting tab:**
      - Font family (dropdown)
      - Font size (numeric)
      - Font weight (Normal / Bold dropdown)
      - Color (color picker)
      - Italic toggle
      - Horizontal alignment: Left | Center | Right | Justify (button group)
      - Vertical alignment: Top | Middle | Bottom (button group)
      - Shadow (dropdown: None, Light, Medium, Heavy)
      - Outline (dropdown: None, Thin, Medium, Thick)
    - **Advanced tab:**
      - Transform (dropdown: None, Uppercase, Lowercase, Capitalize)
      - Letter Spacing (numeric, px)
      - Line Height (numeric, px or multiplier)
      - Line Background (On/Off — adds colored box behind each line, like "Trendy Line")
    - **Margins tab:**
      - Top margin (numeric, px)
      - Left margin (numeric, px)
      - Right margin (numeric, px)
      - Bottom margin (numeric, px)
      - Manage Border/Fill (dropdown)
      - Border style (dropdown)
  - **Live Preview Area:**
    - Shows 3 lines of placeholder text with formatting applied in real-time
    - "Template Preview" / "This is some placeholder text" / "Use this text to get a feel for the formatting"
    - Updates instantly as settings change
    - Black background (or selected background)
  - **Template Management:**
    - Right panel: list of all templates (click to switch/edit)
    - Duplicate button (green) — clone current template
    - Delete button (red) — remove template (with confirmation)
    - "+ Create New" button — new blank template
    - Save button (green, top-right) — save current changes
  - **Built-in Templates:**
    - Default (centered, large text, normal weight)
    - Lower Third (text at bottom, slightly smaller)
    - Trendy Line (text with white background boxes behind each line)
    - User can create unlimited custom templates

- [ ] **11.10** Pop-out Preview Windows
  - Clicking the Live Preview box (left) → opens a larger resizable window showing congregation output
  - Clicking the Stage Preview box (right) → opens a larger resizable window showing stage display
  - These are read-only monitors (no controls, just the live view)
  - Stay on top (optional)
  - Can be placed on any monitor for monitoring purposes
  - Close to dismiss (main preview boxes remain in operator window)

### Phase 12: Settings Panel (WorshipTools-Inspired, Full Settings UI)

- [ ] **12.1** Settings Window Structure
  - Full-screen settings window (replaces current simple dialogs)
  - Left sidebar navigation with categories:
    - **Basic:** Defaults, Footer, Interface, Bibles
    - **Advanced:** Screen Configuration, Stage Display, Library & Database, Updates, Cache, MIDI
  - Each category shows its settings in the main content area
  - Green "Save" button at bottom of each section
  - Dark theme matching the rest of the app

- [ ] **12.2** Defaults Settings
  - **Service Defaults:**
    - Default Fade Time (seconds, numeric input)
    - Default Background (selector: none, image, color)
    - Inherit background when 'None' is selected (checkbox)
    - Auto Fire: Automatically fire first slide when service item is launched (checkbox)
  - **Song, Scripture & Slide Defaults:**
    - Default Slide Aspect Ratio: 16:9, 4:3, 16:10, 32:6, 9:16 (button group)
    - Default Song Lyrics Template (template selector)
    - Default Bible Template (template selector)
    - Default Bible Translation (dropdown: NIV, KJV, ESV, NLT, NKJV, AMP, MSG, WEB, NASB, etc.)
    - Scripture Reference: Hide / Hide translation (checkboxes)
  - **Blank Slide:** Add blank slide when presenting song lyrics or scripture (checkbox)
  - **Title Slide:** Add title slide when presenting song lyrics (checkbox)
  - **Auto-advance:** Go to next service item when advancing from last slide (checkbox)
  - **Media Defaults:**
    - Background Video/Image Scale: Scale to fit screen (dropdown)
    - Video Loop: Loop background videos by default (checkbox)
    - Upload Media: Upload media to the cloud when added to local library (checkbox)

- [ ] **12.3** Footer Settings
  - **Copyright License:**
    - License # field (CCLI license number)
  - **Footer:**
    - Footer Location: dropdown (Bottom Left, Bottom Center, Bottom Right)
    - Show Footer on: dropdown (All slides, Song slides only, Scripture only, None)
    - Footer Font: font family picker
    - Footer Size: numeric (pt)
    - Footer Color: color picker OR "Use slide color" checkbox
    - Extra Footer Padding: numeric (px)

- [ ] **12.4** Interface Settings
  - **Editor:**
    - Editor Background: color picker with presets (Solid black, dark gray, etc.)
  - **Media Library:**
    - Media on initial load: dropdown (All, None, Last used, Favorites)

- [ ] **12.5** Bibles Settings
  - Import Bible: file picker for XML Bible files
  - Installed Bibles list (expandable by language: English, Spanish, etc.)
  - Download/purchase additional translations
  - Set default translation
  - Remove installed translations

- [ ] **12.6** Screen Configuration Settings
  - **Preview Screen:**
    - Resolution display (e.g., "1920 x 1080")
    - Note: "This preview is shown when a secondary monitor or screen is not connected"
  - **Screen Configuration:**
    - List of configured outputs (Main Audience Output, Stage Display, etc.)
    - Each output has:
      - Label (editable name)
      - Display selector: Off, Display 1, Display 2, Display 3, Display 4 (shows resolution for each)
      - Save button per output
    - "+ Add Screen" button to add additional outputs (e.g., lobby TV, overflow room, livestream)
  - **Advanced:**
    - GPU Hardware Acceleration: Enabled / Disabled toggle
    - Note: "Application must restart for this change to take effect"

- [ ] **12.7** Stage Display Settings
  - **Visual Layout Editor** (drag-and-drop block arrangement):
    - Top row blocks: Current Time, Video Countdown, Countdown, Alert
    - Main area: Current (lyrics/chords currently showing)
    - Bottom area: Next (upcoming lyrics preview)
    - Each block shows "Font size: auto" (configurable)
    - Right-click blocks for options (font size, show/hide, color)
  - **Aspect ratio selector:** 4:3, 16:9, 9:16, Reset
  - **Display options:**
    - Song Lyrics/Scripture Foreground: "Display Just Text" vs "Display Full Slide" (dropdown)
    - Slides Foreground: "Display Full Slide" (dropdown)
    - No Foreground: "Nothing" (dropdown — what to show when no content)

- [ ] **12.8** Library & Database Settings
  - Media Library Location: path display + "Change" button (folder picker)
  - Library Reset: "Reset media" button (with confirmation dialog)
  - Database info (size, song count, etc.)

- [ ] **12.9** MIDI Settings
  - **MIDI Command Mapping Table:**
    - Each row: Command name | MIDI Note number (editable)
    - Commands:
      - Change Service Item: note (default 1)
      - Previous Service Item: note (default 2)
      - Next Service Item: note (default 3)
      - Change Slide: note (default 4)
      - Previous Slide: note (default 5)
      - Next Slide: note (default 6)
    - Note about velocity: "For Change Service Item and Change Slide commands, the velocity determines the index of the service item or slide to select (starting at 0)"
  - **MIDI Connections:**
    - "Connect to MIDI Network" button
    - Connections panel showing active MIDI devices
    - Status: "No connections found" or list of connected devices
  - Save button

- [ ] **12.10** Updates & Cache Settings
  - Check for updates (button)
  - Current version display
  - Auto-update toggle
  - Cache management:
    - Cache location
    - Cache size display
    - Clear cache button

### Phase 13: Tools Menu (Alert, Countdown, Search, CCLI Report)

- [ ] **13.1** Tools Menu (top navigation bar)
  - Rename "More" tab to **"Tools"** in the top navigation
  - Dropdown menu with items:
    - Alert (keyboard shortcut shown)
    - Countdown (keyboard shortcut shown)
    - Search
    - Song Usage Report (CCLI reporting)
  - Each item opens its respective dialog/panel

- [ ] **13.2** Alert Tool
  - Modal dialog with live preview (black preview area showing alert text)
  - Drag text in preview to reposition
  - **Template:** dropdown (-New Template- / saved templates) + Save button + Delete (red X)
  - **Text:** input field (e.g., "Welcome!")
  - **Size:** slider (controls font size)
  - **Font:** font family picker + color picker + Shadow dropdown + Outline dropdown
  - **"Stage Display Alert"** checkbox (also show alert on stage display)
  - **"Show Alert"** button (green) — fires the alert immediately on outputs
  - **"Close"** button — dismisses dialog
  - Alert appears as overlay on congregation output (top or configurable position)
  - Auto-dismiss after configurable seconds, or manual dismiss
  - Use cases: "Welcome!", "Service starting in 5 minutes", "Please silence phones"

- [ ] **13.3** Countdown Tool
  - Modal dialog with live preview (shows countdown number, e.g., "30")
  - Drag text in preview to reposition
  - **Template:** dropdown (-New Template- / saved templates) + Save + Delete
  - **Type:** button group: Countdown | Countdown To Time | Count Up
    - Countdown: counts down from Duration to 0
    - Countdown To Time: counts down to a specific time (e.g., 10:30 AM service start)
    - Count Up: counts up from 0 (elapsed timer)
  - **Duration:** time input (HH:MM:SS) + Reset button
  - **Location:** checkboxes:
    - ☑ Main Audience Output
    - ☑ Stage Display
  - **Size:** slider (font size)
  - **Font:** font family picker (e.g., "Tahoma Normal") + color + Shadow + Outline
  - **"Start"** button (green) — starts the countdown on selected outputs
  - **"Close"** button
  - Countdown displays as overlay on selected outputs
  - When countdown reaches 0: auto-dismiss or fire next service item (configurable)
  - Use cases: pre-service countdown on lobby TVs, sermon timer on stage display

- [ ] **13.4** Search Tool
  - Full-page search view (replaces main content area when opened)
  - **Search box** at top with "Clear" button
  - Results grouped by category:
    - **Services:** matching service orders (name, date)
    - **Songs:** matching songs with:
      - Green icon + song title
      - Artist/author info below title
      - Last Modified date
      - Options: "Edit" button (green) | "Delete" button (red)
  - Could also search: Media, Scripture, Slides
  - Results update in real-time as you type
  - Click a result to: open it, add to current service, or edit
  - Keyboard shortcut to open (Ctrl+F or Ctrl+Shift+F)

- [ ] **13.5** Song Usage Report (CCLI Reporting)
  - View songs used in services over a date range
  - Track: song title, CCLI number, dates used, times used
  - Export as CSV or PDF for CCLI reporting submission
  - Auto-logged when a song is played during a service (backtrack plays it = logged)
  - Future: auto-submit to CCLI via API

---

## Technical Notes

### Lyrics Format (existing, no changes needed)
```
sections[].text uses inline [chord] notation:
  "[B]    The Lord bless you  [E]and keep you"

Slide breaks = empty lines in section text
Timestamps = {M:SS.mmm} at start of slide block
ChordProParserService handles parsing, transposition, stripping
```

### Event Flow (Backtrack → Presenter)
```
AudioEngine.PositionChanged → MidiPresenterSync.OnPlaybackPositionUpdate()
MultiTrackMixer.SectionChanged → MidiPresenterSync.OnBacktrackSectionChanged()
MultiTrackMixer (loop detected) → MidiPresenterSync.OnSectionLooped() [NEW]
AudioEngine.SetPitchShift() → MidiPresenterSync.OnPitchChanged() [NEW]
AudioEngine.SetTempo() → MidiPresenterSync.OnTempoChanged() [NEW]
AudioEngine.PlaybackStopped (song end) → MidiPresenterSync.OnSongEnded()
AudioEngine.LoadSong() → MidiPresenterSync.OnSongLoaded() [NEW]
```

### Stage Display Network Protocol
```
Embedded Kestrel web server on port 5050 (configurable)
WebSocket endpoint: /ws/stage
Messages: JSON { type: "slide", text: "...", chords: [...], section: "...", key: "D", tempo: 95 }
Client: Static HTML/CSS/JS page served at /stage
Auto-reconnect on connection loss
```

---

## Dependencies

- `WorshipPlayer.Core` (shared models — Song, Section, ChordChartData, ServiceOrder)
- `WorshipPlayer.Audio` (AudioEngine events — position, section changes)
- `NAudio` (MIDI input for foot pedal control)
- `Microsoft.AspNetCore` (embedded web server for network stage display + remote control) — ADD to .csproj
- `System.Net.WebSockets` (real-time stage updates + remote sync)
- `LibVLCSharp` or `WPF MediaElement` (video playback — motion backgrounds + video service items)
- `NewTek.NDI` or `NDI.SDK` (NDI output for wireless/streaming display)
- Music theory library or custom (Nashville number conversion, capo transposition)
