# OWC Presenter Module — Reorganized Feature & Task Plan (v2)

## Status: In Progress (Month 1)
**Branch:** `feature/lyric-presenter`
**Approach:** UX-first organization — grouped by user workflow, not by build order

---

## Architecture Decisions (CONFIRMED)

- [x] Hybrid architecture (Desktop C#/WPF + Cloud SaaS)
- [x] Backtrack is the conductor (timing, sections, loops, pitch)
- [x] Presenter follows backtrack events (display only)
- [x] Lyrics source: `song.json → sections[].text` (inline [chords] + {timestamps})
- [x] Cloud Song Library = master source → downloaded locally for offline Sunday execution
- [x] ServiceOrder = single source of truth for service flow
- [x] Stage display: ALWAYS shows chords (transposed)
- [x] Congregation output: CONFIGURABLE show/hide chords
- [x] Network delivery: port 5055, WebSocket for real-time updates

---

## APPLICATION LAYOUT

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ [OWC Logo]  Services | Songs | Scripture | Slides | Media | Tools   [👤 ⚙️] │
├─────────────┬──────────────────────────────────────────┬─────────────────────┤
│             │ [🔴 Audience Live] [🟢 Stage Live]       │                     │
│  SERVICE    │──────────────────────────────────────────│   BACKTRACK         │
│  ORDER      │                                          │   STATUS            │
│             │        SLIDE GRID / CONTENT AREA         │                     │
│ (left       │        (changes per tab)                 │   Song: The Bless.  │
│  sidebar)   │                                          │   Section: VERSE 1  │
│             │  ┌──────┐ ┌══════┐ ┌──────┐ ┌──────┐   │   Key: F (+0)       │
│ ▼ Worship   │  │Blank │ ║LIVE  ║ │V1b   │ │Chorus│   │   Tempo: 100%       │
│   The Bless.│  │      │ ║Verse1║ │      │ │      │   │   Loop: ∞           │
│   Open Heav.│  └──────┘ ╚══════╝ └──────┘ └──────┘   │                     │
│ ▼ Message   │                                          │   [CHORDS: OFF]     │
│   John 3:16 │  ┌──────┐ ┌──────┐ ┌──────┐            │                     │
│   Sermon    │  │Verse2│ │V2b   │ │Bridge│            │                     │
│ ▼ Response  │  └──────┘ └──────┘ └──────┘            │                     │
│   Goodness  │                                          │                     │
│             │  [◀ PREV] [▶ NEXT] [BLACK] [LOGO]       │                     │
├─────────────┴──────────────────────────────────────────┴─────────────────────┤
│ Backtrack: ▶ Playing | The Blessing | VERSE 1 | 1:24/5:12 | Slide 2/19      │
└──────────────────────────────────────────────────────────────────────────────┘
```

---

## FEATURE GROUPS (Reorganized by UX Domain)

### A. CORE ENGINE (What makes it work — invisible to user)

| # | Feature | Description |
|---|---------|-------------|
| A1 | Section-aware slide generation | Read from `sections[].text`, split by empty lines, apply ChartSlide timing |
| A2 | Backtrack event integration | SectionChanged, SectionLooped, PitchShift, Tempo, SongEnded, PositionChanged |
| A3 | Dual rendering pipeline | Congregation: strip chords (configurable) / Stage: always show chords |
| A4 | Auto-advance from timestamps | `ChartSlide.StartMs/EndMs` drives slide timing |
| A5 | Manual advance mode | Songs without backtrack: keyboard/foot pedal/remote advance |
| A6 | Loop awareness | Section loops → reset slides to section start (don't advance) |
| A7 | Pitch-shift chord transposition | Real-time transpose display when backtrack shifts pitch |
| A8 | ServiceOrder navigation | Next/prev item, auto-load songs in backtrack, handle all item types |

### B. MAIN WORKSPACE (What the operator sees and does)

| # | Feature | Description |
|---|---------|-------------|
| B1 | Top navigation tabs | Services, Songs, Scripture, Slides, Media, Tools |
| B2 | Left sidebar: Service order | Collapsible custom groups, drag-drop, icons, right-click menu |
| B3 | Slide grid view | Thumbnail grid with backgrounds/labels, green=live, click to fire |
| B4 | Zoom slider | Control thumbnail size, auto-reflow grid, remember setting |
| B5 | Live preview boxes | Top area: Audience (left) + Stage (right), click for pop-out |
| B6 | Transport bar (bottom) | Prev/Next slide, Prev/Next item, GO LIVE, BLACK, LOGO, CHORDS toggle |
| B7 | Backtrack status panel | Song/section/position/key/tempo/loop in right panel |
| B8 | Edit mode toggle | Switch between performance mode (click=fire) and edit mode |
| B9 | Keyboard shortcuts | Space/Right=next, Left=prev, B=black, Enter=live, Esc=black, 1-9=jump |
| B10 | Status bar (bottom) | Current playback info, slide counter, connection status |

### C. SERVICE BUILDER (Planning the service before Sunday)

| # | Feature | Description |
|---|---------|-------------|
| C1 | Add service item dialog | Background (None/Media/WebVideo/WebPage/VideoFeed) + Foreground (Lyrics/Slides/Scripture/PPT/Google) |
| C2 | Section headers/groups | Create named collapsible groups (Opening Worship, Message, Response) |
| C3 | Bulk edit | Multi-select items → batch apply background/foreground/transition/auto-advance |
| C4 | Per-item settings | Auto-advance (Go To + After), Transition (fade time), attached audio |
| C5 | Print service order | Printable view with/without lyrics, export PDF |
| C6 | Import from Planning Center | "Import from PCO" migration path |
| C7 | Import from ProPresenter | Migration from ProPresenter setlists/songs |
| C8 | Duplicate/Delete service items | Quick actions on items |

### D. SONG & LYRICS MANAGEMENT

| # | Feature | Description |
|---|---------|-------------|
| D1 | Song library browser | Search/filter by title, artist, key, lyrics; sort A-Z/date |
| D2 | Song slide editor | Full WYSIWYG: lyrics text + background + template per slide |
| D3 | CCLI SongSelect integration | Search online, import lyrics (requires online + API key) |
| D4 | ChordPro import | Import .chopro files with chords |
| D5 | Import from other software | Migration link for ProPresenter, EasyWorship, etc. |
| D6 | Song metadata | Title, artist, key, BPM, CCLI number, tags |
| D7 | Song usage tracking | Auto-log when played → CCLI reporting export |

### E. SCRIPTURE

| # | Feature | Description |
|---|---------|-------------|
| E1 | Scripture browser | Type reference (e.g., "John 3:16-18") → auto-fetch |
| E2 | Bible translations | All major: NIV, KJV, ESV, NLT, NKJV, AMP, MSG, WEB, NASB |
| E3 | Online + Offline | API fetch when connected, SQLite local database as fallback |
| E4 | Scripture templates | Apply formatting templates (Default, Lower Third, Trendy Line) |
| E5 | Reference display options | Show/hide reference, show/hide translation name |
| E6 | Background per scripture | Pick from media library or solid color |
| E7 | Auto-split passages | Long passages auto-split into multiple slides (configurable) |
| E8 | Import Bible (XML) | Import additional Bible versions from XML files |

### F. MEDIA LIBRARY

| # | Feature | Description |
|---|---------|-------------|
| F1 | Sub-tabs | Your Media Library, Free Stills, Motions, Templates |
| F2 | View modes | Grid (large thumbnails), List (metadata), Compact (small icons) |
| F3 | Filter panel | Type (Image/Video/Audio/PPT), Tags, Cloud Status |
| F4 | Import media | From file system (images, videos, audio, PowerPoint) |
| F5 | Cloud sync | Upload to cloud, download status, sync indicators |
| F6 | Motion backgrounds | Looping video backgrounds, seamless loop, opacity control |
| F7 | Templates/Notices | Pre-designed sermon series graphics, announcements |
| F8 | Drag-to-assign | Drag media onto slide thumbnail to set as background |
| F9 | Sort & search | Sort by name/date/type, search by name |

### G. TEMPLATE SYSTEM

| # | Feature | Description |
|---|---------|-------------|
| G1 | Template editor (WYSIWYG) | 3 tabs: Formatting, Advanced, Margins + live preview |
| G2 | Formatting tab | Font, size, weight, color, italic, H-align, V-align, shadow, outline |
| G3 | Advanced tab | Transform, letter spacing, line height, line background |
| G4 | Margins tab | Top/left/right/bottom margins, border |
| G5 | Template management | List, create new, duplicate, delete, rename |
| G6 | Built-in templates | Default, Lower Third, Trendy Line (+ user-created) |
| G7 | Apply templates | Per song, per scripture, per slide; set defaults in settings |

### H. OUTPUT & DISPLAYS

| # | Feature | Description |
|---|---------|-------------|
| H1 | Congregation output window | Full-screen on selected monitor, transitions (cut/fade/dissolve) |
| H2 | Stage display (WPF window) | Chords + lyrics on separate monitor, layout editor |
| H3 | Stage display (web/iPad) | Kestrel server port 5055, WebSocket real-time |
| H4 | Per-musician views | Guitarist (capo), Keys (Nashville), Vocalist (lyrics only) via URL params |
| H5 | Multi-output support | Add unlimited outputs: lobby TV, overflow, livestream, etc. |
| H6 | Window mode | Output as floating window (not just full-screen) |
| H7 | NDI output | Stream to OBS/vMix/video switchers over network |
| H8 | AirPlay/Miracast | Wireless display to Apple TV, smart TVs |
| H9 | Pop-out preview windows | Larger monitor window for congregation/stage preview |
| H10 | GPU hardware acceleration | Toggle for performance |

### I. MUSICIAN FEATURES (Stage Display)

| # | Feature | Description |
|---|---------|-------------|
| I1 | Chord chart display | Chords above lyrics, monospace, green color (matching WPF chart) |
| I2 | Auto-transpose with pitch | Chords update when backtrack shifts pitch |
| I3 | Capo display | Show capo-adjusted chords for guitarists |
| I4 | Nashville numbers | Show 1-4-5-6m notation (per-musician toggle) |
| I5 | One-column / Two-column | Layout options for chart readability |
| I6 | Next section preview | Show upcoming section name + first chord |
| I7 | Current time / countdown | Configurable blocks: clock, countdown, alert |
| I8 | Stage display layout editor | Drag-and-drop blocks (time/countdown/current/next/alert) |

### J. TOOLS (Quick-access utilities)

| # | Feature | Description |
|---|---------|-------------|
| J1 | Alert overlay | Fire text alert on any output (templated, positionable) |
| J2 | Countdown timer | Countdown/count-up/to-time, on audience + stage, configurable font |
| J3 | Global search | Search services, songs, media — grouped results with actions |
| J4 | CCLI song usage report | Track songs played, export CSV/PDF for CCLI submission |

### K. REMOTE CONTROL

| # | Feature | Description |
|---|---------|-------------|
| K1 | Web-based remote | Phone/tablet via browser (`/remote`), no app install |
| K2 | PIN security | 4-6 digit auth, rate limiting, token expiry |
| K3 | Remote features | Next/prev slide/item, go live/black/logo, service order view, timer, notes |
| K4 | Multi-remote | Multiple devices simultaneously, all synced via WebSocket |
| K5 | Read-only mode | Pastor monitoring view (no control) |

### L. SETTINGS (Full settings panel)

| # | Feature | Description |
|---|---------|-------------|
| L1 | Settings structure | Full-screen window, left sidebar nav (Basic + Advanced categories) |
| L2 | Defaults | Fade time, aspect ratio, templates, auto-fire, auto-advance, media defaults |
| L3 | Footer/CCLI | License #, footer location/font/size/color/padding, show on which slides |
| L4 | Interface | Editor background, media load behavior |
| L5 | Bibles | Import XML, manage installed translations, set default |
| L6 | Screen configuration | Multi-output display assignment, Window mode, + Add Screen |
| L7 | Stage display layout | Block arrangement editor, aspect ratio, foreground options |
| L8 | Library & database | Media path, reset, storage info |
| L9 | MIDI mapping | Command → note table (6 commands), network MIDI connections |
| L10 | Updates & cache | Version, auto-update, cache management |

### M. ACCOUNT & CLOUD (Month 2-3)

| # | Feature | Description |
|---|---------|-------------|
| M1 | User account menu | Name, subscription tier, settings, manage account, users, logout |
| M2 | Cloud song library API | Upload/download songs + stems, search, CCLI import |
| M3 | Desktop sync service | "Download for Sunday" workflow, background download, offline cache |
| M4 | Team/user management | Invite team members, roles (Admin/Operator/Viewer) |
| M5 | Community marketplace | Share arrangements, rate/review (future) |
| M6 | Subscription/billing | Per-module activation, Stripe integration |

---

## BUILD PRIORITY (Recommended Order)

### Priority 1: MUST HAVE for First Usable Release
> *What you need to run a service with lyrics on screen*

- **A1-A8** (Core engine — already built ✅)
- **B1-B10** (Main workspace UI — Phase 11 redesign)
- **H1** (Congregation output — already built ✅)
- **H2-H3** (Stage display — already built ✅)
- **I1-I2** (Chord chart display — already built ✅)

### Priority 2: HIGH VALUE for Real-World Use
> *What makes it competitive with WorshipTools/ProPresenter*

- **C1-C5** (Service builder — plan your Sunday)
- **D1-D4** (Song library + editor)
- **E1-E4** (Scripture browser)
- **F1-F4, F6** (Media library basics + motion backgrounds)
- **G1-G7** (Template system)
- **J1-J2** (Alert + Countdown — used every Sunday)
- **K1-K4** (Remote control)
- **L1-L10** (Full settings panel)

### Priority 3: DIFFERENTIATION Features
> *What makes OWC better than the competition*

- **H4** (Per-musician views)
- **I3-I5** (Capo, Nashville, column layout)
- **H7** (NDI output)
- **C6-C7** (Import from Planning Center / ProPresenter)
- **D3** (CCLI SongSelect integration)
- **J4** (CCLI reporting)

### Priority 4: FUTURE / Nice-to-Have
> *Month 2-3 and beyond*

- **M1-M6** (Cloud, accounts, billing)
- **H8** (AirPlay/Miracast)
- **F5, F7** (Cloud sync, community content)
- **C8** (Bulk operations refinement)

---

## TOOLBAR ICONS (Left sidebar header)

| Icon | Action | Description |
|------|--------|-------------|
| **+** | Add Item | Opens service item dialog (background + foreground type selector) |
| **§** (section) | Add Group/Header | Creates a new collapsible section group in the service order |
| **✏️** | Edit Service | Rename service, change date/type |
| **📄** | Print/Export | Print service order (with/without lyrics) |
| **☰** | Bulk Edit | Multi-select items for batch operations |

---

## KEY DESIGN PRINCIPLES

1. **Performance mode first** — Default state is optimized for running a live service (click=fire, no edit clutter)
2. **Edit mode is explicit** — Must toggle Edit to modify content (prevents accidental changes during service)
3. **Two-click rule** — Any action during service should take max 2 clicks/taps
4. **Always visible: what's live** — Live preview boxes + green border on active slide = instant awareness
5. **Offline Sunday** — Everything works without internet during service; sync happens before/after
6. **Progressive disclosure** — Basic features obvious, advanced features discoverable (settings, advanced tabs)
7. **Consistent dark theme** — Optimized for low-light environments (worship services, tech booths)
8. **Keyboard-first during service** — Arrow keys, Space, B, Enter, Esc = hands-free operation
