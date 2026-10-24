#!/usr/bin/env python3
"""
analyze_multitrack.py — Advanced Multi-Signal Section Detection for Worship Backtrack Player

Uses ALL available signals from multitrack stems to achieve ~99% section detection accuracy:
1. Instrument activation (RMS per stem per bar)
2. Drum pattern analysis + fill detection
3. Vocal onset/offset detection
4. Chord progression (chroma from harmonic stems)
5. Lyrics transcription (Whisper on vocal stem)
6. Cue track parsing (Whisper on cue stem)
7. BGV/harmony presence
8. Melody contour (pitch tracking)
9. Energy dynamics (overall + per-instrument)
10. Bass pattern analysis
11. Instrument count per bar
12. Drum fill boundary markers

All signals are assembled into a structured prompt for LLM-based section identification.

Usage: python analyze_multitrack.py "path/to/song/folder" [--llm azure|openai|ollama|none] [--output song.json]

Requirements: pip install librosa numpy scipy openai-whisper openai
Optional: pip install ollama (for local LLM)

Azure OpenAI environment variables:
  AZURE_OPENAI_ENDPOINT    - e.g. https://your-resource.openai.azure.com/
  AZURE_OPENAI_API_KEY     - your Azure OpenAI key
  AZURE_OPENAI_DEPLOYMENT  - deployment name (default: gpt-4o-mini)
  AZURE_OPENAI_API_VERSION - API version (default: 2024-02-15-preview)
"""

import sys
import os
import json
import warnings
warnings.filterwarnings('ignore')

# Force UTF-8 output on Windows (prevents cp1252 encoding errors)
if sys.platform == 'win32':
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    sys.stderr.reconfigure(encoding='utf-8', errors='replace')

# Force unbuffered output so progress updates appear in real-time
sys.stdout.reconfigure(line_buffering=True)

try:
    import numpy as np
    import librosa
    from scipy.signal import find_peaks
    from scipy.spatial.distance import cdist
except ImportError as e:
    print(f"ERROR: Missing required package: {e}\n"
          f"Install with: pip install librosa numpy scipy", file=sys.stderr)
    sys.exit(1)

from pathlib import Path
from typing import Optional, Dict, List, Tuple, Any



# ═══════════════════════════════════════════════════════════════════════
# CONFIGURATION
# ═══════════════════════════════════════════════════════════════════════

AUDIO_EXTENSIONS = {'.mp3', '.wav', '.flac', '.ogg', '.m4a', '.aac', '.aiff'}
SAMPLE_RATE = 22050
HOP_LENGTH = 512

# Track classification keywords
TRACK_KEYWORDS = {
    'click': ['click', 'metronome', 'met'],
    'cue': ['cue', 'guide'],
    'drums': ['drum', 'perc'],
    'bass': ['bass'],
    'acoustic_guitar': ['acousticguitar', 'acoustic_guitar', 'acguitar', 'ag'],
    'electric_guitar': ['electricguitar', 'electric_guitar', 'elguitar', 'eg', 'guitar'],
    'piano': ['piano', 'pno'],
    'keys': ['keys', 'synth', 'pad', 'organ'],
    'lead_vocal': ['leadvocal', 'lead_vocal', 'vocal', 'vox'],
    'bgv': ['bgv', 'backing', 'harmony', 'choir', 'bv'],
    'strings': ['strings', 'string', 'orchestra'],
}



# ═══════════════════════════════════════════════════════════════════════
# DEMUCS SOURCE SEPARATION (for single-track songs)
# ═══════════════════════════════════════════════════════════════════════

def _post_process_stems(stems: Dict[str, str], sr: int) -> Dict[str, str]:
    """Apply post-processing filters to improve Demucs stem quality.

    Current filters:
    1. Bass high-pass filter: Removes drum bleed (kick transients) from bass stem
       by cutting frequencies below 30Hz (sub-bass rumble) and applying a gentle
       low-pass at 250Hz to remove drum/vocal bleed above the bass range.
    2. Bass transient suppression: Reduces kick drum transients that bleed into bass.
    """
    import soundfile as sf
    from scipy import signal

    bass_path = stems.get('bass')
    if bass_path and os.path.exists(bass_path):
        try:
            # Load bass stem
            bass_audio, bass_sr = sf.read(bass_path)  # (samples, channels)

            # Design filters
            # High-pass at 30Hz: removes sub-bass rumble and DC offset
            hp_freq = 30.0
            hp_sos = signal.butter(4, hp_freq, btype='highpass', fs=bass_sr, output='sos')

            # Low-pass at 250Hz: removes drum/vocal bleed above bass range
            # Using a gentle slope (2nd order) to preserve bass harmonics
            lp_freq = 250.0
            lp_sos = signal.butter(2, lp_freq, btype='lowpass', fs=bass_sr, output='sos')

            # Apply filters per channel
            if bass_audio.ndim == 2:
                for ch in range(bass_audio.shape[1]):
                    # Apply high-pass (removes sub-rumble)
                    bass_audio[:, ch] = signal.sosfilt(hp_sos, bass_audio[:, ch])
                    # Apply low-pass (removes drum/vocal bleed)
                    bass_audio[:, ch] = signal.sosfilt(lp_sos, bass_audio[:, ch])
            else:
                bass_audio = signal.sosfilt(hp_sos, bass_audio)
                bass_audio = signal.sosfilt(lp_sos, bass_audio)

            # Save filtered bass back
            sf.write(bass_path, bass_audio, bass_sr, subtype='FLOAT')
            print(f"    ✓ Post-processing: Bass stem filtered (30Hz HP + 250Hz LP)")

        except Exception as e:
            print(f"    ! Bass post-processing failed: {e}")

    return stems


def generate_click_track(grid: Dict, folder_path: str, time_sig: int = 4) -> Optional[str]:
    """DEPRECATED / UNUSED. Auto-Detect no longer generates a click track (the output
    was on the wrong downbeat phase and too quiet). Click generation now lives entirely
    in the C# "Auto Generate Click Track" button (music-detected downbeat + loud render).
    Kept only for reference; not called by the pipeline.

    Creates a metronome-style click with:
    - Loud high-pitched click on beat 1 (downbeat)
    - Softer lower-pitched click on beats 2, 3, 4

    Returns the path to the generated click WAV, or None if failed.
    """
    import soundfile as sf

    beats = grid.get('beats', [])
    if not beats or len(beats) < 4:
        print("    ! Cannot generate click: insufficient beat data")
        return None

    sr = SAMPLE_RATE  # 44100 Hz
    total_duration = grid['total_duration_sec']
    total_samples = int(total_duration * sr)

    # Create silent stereo buffer
    click_audio = np.zeros(total_samples, dtype=np.float32)

    # Synthesize click sounds
    # Downbeat (beat 1): 1500Hz, 20ms, louder
    downbeat_freq = 1500.0
    downbeat_duration = 0.020  # 20ms
    downbeat_samples = int(downbeat_duration * sr)
    downbeat_volume = 0.9

    t_db = np.arange(downbeat_samples) / sr
    downbeat_sound = downbeat_volume * np.sin(2 * np.pi * downbeat_freq * t_db)
    # Apply exponential decay envelope
    downbeat_env = np.exp(-t_db * 150)  # Fast decay
    downbeat_sound *= downbeat_env

    # Off-beat (beats 2, 3, 4): 1000Hz, 15ms, softer
    offbeat_freq = 1000.0
    offbeat_duration = 0.015  # 15ms
    offbeat_samples = int(offbeat_duration * sr)
    offbeat_volume = 0.6

    t_ob = np.arange(offbeat_samples) / sr
    offbeat_sound = offbeat_volume * np.sin(2 * np.pi * offbeat_freq * t_ob)
    offbeat_env = np.exp(-t_ob * 200)  # Faster decay
    offbeat_sound *= offbeat_env

    # Determine which beats are downbeats
    downbeats = set()
    if grid.get('downbeats') and len(grid['downbeats']) > 0:
        # Use detected downbeats from beat_this
        for db_sec in grid['downbeats']:
            # Find nearest beat index
            min_dist = float('inf')
            nearest_idx = 0
            for i, b in enumerate(beats):
                dist = abs(b - db_sec)
                if dist < min_dist:
                    min_dist = dist
                    nearest_idx = i
            if min_dist < grid['beat_interval_sec'] * 0.5:
                downbeats.add(nearest_idx)
    else:
        # Fallback: every time_sig beats is a downbeat
        for i in range(0, len(beats), time_sig):
            downbeats.add(i)

    # Place clicks at beat positions
    beats_placed = 0
    for i, beat_sec in enumerate(beats):
        sample_pos = int(beat_sec * sr)
        if sample_pos >= total_samples:
            break

        if i in downbeats:
            # Downbeat click
            end_pos = min(sample_pos + downbeat_samples, total_samples)
            length = end_pos - sample_pos
            click_audio[sample_pos:end_pos] += downbeat_sound[:length].astype(np.float32)
        else:
            # Off-beat click
            end_pos = min(sample_pos + offbeat_samples, total_samples)
            length = end_pos - sample_pos
            click_audio[sample_pos:end_pos] += offbeat_sound[:length].astype(np.float32)

        beats_placed += 1

    # Save click track
    # Determine output path — save in the song's stem folder (same level as audio file)
    # Look for existing stem folder (named after the audio file)
    song_name = None
    if os.path.isdir(folder_path):
        for item in os.listdir(folder_path):
            item_path = os.path.join(folder_path, item)
            if os.path.isdir(item_path) and item != '_separated':
                # Check if this folder contains separated stems
                if any(os.path.exists(os.path.join(item_path, f'{s}.wav'))
                       for s in ['vocals', 'drums', 'bass']):
                    song_name = item
                    break

    if song_name:
        output_dir = os.path.join(folder_path, song_name)
    else:
        # Fallback: create folder named after first audio file in the folder
        audio_files = [f for f in os.listdir(folder_path)
                       if Path(f).suffix.lower() in AUDIO_EXTENSIONS]
        if audio_files:
            song_name = Path(audio_files[0]).stem
        else:
            song_name = Path(folder_path).name
        output_dir = os.path.join(folder_path, song_name)
        os.makedirs(output_dir, exist_ok=True)

    click_path = os.path.join(output_dir, 'click.wav')
    os.makedirs(os.path.dirname(click_path), exist_ok=True)

    # Save as mono WAV (click doesn't need stereo)
    sf.write(click_path, click_audio, sr, subtype='FLOAT')

    downbeat_count = len(downbeats)
    print(f"    ✓ Click track generated: {beats_placed} beats "
          f"({downbeat_count} downbeats) → {click_path}")

    return click_path


# Quality presets for Demucs separation
DEMUCS_QUALITY_PRESETS = {
    'fast': {
        'model': 'htdemucs_6s',
        'shifts': 1,
        'description': '6-stem separation, fast (vocals, drums, bass, guitar, piano, other)'
    },
    'best': {
        'model': 'htdemucs_6s',
        'shifts': 5,
        'description': '6-stem separation, maximum quality (5 shifts, cleanest)'
    },
    'default': {
        'model': 'htdemucs_6s',
        'shifts': 3,
        'description': '6-stem separation: vocals, drums, bass, guitar, piano, other'
    }
}


def run_demucs_separation(audio_path: str, quality: str = 'default') -> Optional[Dict[str, str]]:
    """Separate a single audio file into stems using Demucs (Meta AI).
    Returns dict of {stem_name: file_path} or None if Demucs unavailable.
    Results are cached in a '_separated' subfolder next to the audio file.

    Quality presets:
      'fast'     - htdemucs, shifts=1 (4 stems, fastest)
      'balanced' - htdemucs_ft, shifts=2 (4 stems, better quality)
      'best'     - htdemucs_ft, shifts=5 (4 stems, maximum quality)
      'default'  - htdemucs_6s, shifts=2 (6 stems: vocals/drums/bass/guitar/piano/other)

    IMPORTANT: This implementation bypasses torchaudio for file I/O to avoid
    the fragile torchaudio → torchcodec → FFmpeg shared DLL dependency chain.
    Instead, it uses librosa (for loading) and soundfile (for saving), which
    are already dependencies of this project and work reliably on all platforms.
    """

    # Get quality preset settings
    preset = DEMUCS_QUALITY_PRESETS.get(quality, DEMUCS_QUALITY_PRESETS['default'])
    model_name = preset['model']
    shifts = preset['shifts']

    # Write stems DIRECTLY into the original file's folder (not a <name>/ subfolder), so the
    # separated tracks live alongside the source mix. Stem names (vocals.wav, drums.wav, …) never
    # collide with the source file name. Cached stems in the same folder are reused.
    audio_dir = str(Path(audio_path).parent)
    audio_name = Path(audio_path).stem
    cache_dir = audio_dir

    # Expected stems depend on the model
    if model_name == 'htdemucs_6s':
        expected_stems = {
            'vocals': os.path.join(cache_dir, 'vocals.wav'),
            'drums': os.path.join(cache_dir, 'drums.wav'),
            'bass': os.path.join(cache_dir, 'bass.wav'),
            'guitar': os.path.join(cache_dir, 'guitar.wav'),
            'piano': os.path.join(cache_dir, 'piano.wav'),
            'other': os.path.join(cache_dir, 'other.wav'),
        }
    else:
        expected_stems = {
            'vocals': os.path.join(cache_dir, 'vocals.wav'),
            'drums': os.path.join(cache_dir, 'drums.wav'),
            'bass': os.path.join(cache_dir, 'bass.wav'),
            'other': os.path.join(cache_dir, 'other.wav'),
        }

    # If all cached stems exist, return them immediately
    if all(os.path.exists(p) for p in expected_stems.values()):
        print(f"    Using cached Demucs stems from: {cache_dir}")
        print(f"    Model: {model_name} | Quality: {quality}")
        return expected_stems

    # Try to run Demucs at the tensor level (bypassing torchaudio file I/O)
    try:
        import torch
        import soundfile as sf
        from demucs.pretrained import get_model
        from demucs.apply import apply_model
        from demucs.audio import convert_audio
    except ImportError as e:
        print(f"    ! Missing dependency for Demucs: {e}")
        print(f"    TIP: pip install demucs soundfile torch")
        return None

    try:
        print(f"    Running Demucs separation...")
        print(f"    Model: {model_name} | Shifts: {shifts} | Quality: {quality}")
        print(f"    ({preset['description']})")
        print(f"    GPU: {torch.cuda.is_available()} | This may take 1-10 minutes on CPU...")

        # Load model
        model = get_model(model_name)
        model.eval()
        device = 'cuda' if torch.cuda.is_available() else 'cpu'
        model.to(device)

        # Load audio using librosa (bypasses torchaudio completely)
        model_sr = model.samplerate  # typically 44100

        y, sr = librosa.load(audio_path, sr=model_sr, mono=False)

        # librosa returns (channels, samples) for stereo, or (samples,) for mono
        if y.ndim == 1:
            # Mono → duplicate to stereo
            y = np.stack([y, y])
        elif y.shape[0] > 2:
            # Multi-channel → take first 2
            y = y[:2]

        # Convert to torch tensor: shape (1, channels, samples)
        wav = torch.tensor(y, dtype=torch.float32).unsqueeze(0)

        # Ensure correct number of channels
        wav = convert_audio(wav, model_sr, model.samplerate, model.audio_channels)
        wav = wav.to(device)

        # Apply the model with shifts for artifact reduction
        with torch.no_grad():
            sources = apply_model(model, wav, shifts=shifts, progress=True)

        # sources shape: (1, num_sources, channels, samples)
        source_names = model.sources  # e.g. ['drums', 'bass', 'other', 'vocals']
        #                              or for 6s: ['drums', 'bass', 'other', 'vocals', 'guitar', 'piano']

        print(f"    Stems separated: {source_names}")

        # Save each stem using soundfile (bypasses torchaudio)
        os.makedirs(cache_dir, exist_ok=True)
        result = {}

        for i, stem_name in enumerate(source_names):
            stem_audio = sources[0, i].cpu().numpy()  # (channels, samples)
            output_path = os.path.join(cache_dir, f'{stem_name}.wav')

            # soundfile expects (samples, channels) for multi-channel
            sf.write(output_path, stem_audio.T, model_sr, subtype='FLOAT')
            result[stem_name] = output_path
            print(f"      ✓ {stem_name} → {output_path}")

        print(f"    Demucs separation complete! ({len(result)} stems)")

        # POST-PROCESSING: Apply cleanup filters to improve stem quality
        result = _post_process_stems(result, model_sr)

        return result

    except Exception as e:
        print(f"    ! Demucs separation failed: {e}")
        import traceback
        traceback.print_exc()
        return None


# ═══════════════════════════════════════════════════════════════════════
# AUDIO FILE DISCOVERY & CLASSIFICATION
# ═══════════════════════════════════════════════════════════════════════

def find_audio_files(folder: str) -> List[str]:
    """Find all audio files in a folder."""
    files = []
    for f in sorted(Path(folder).iterdir()):
        if f.suffix.lower() in AUDIO_EXTENSIONS:
            files.append(str(f))
    return files


def classify_tracks(files: List[str], demucs_quality: str = 'default') -> Dict[str, Optional[str]]:
    """Classify each audio file into its role (drums, bass, vocal, etc.).
    If only a single file is found (full mix), classify it for all analysis roles."""
    classified = {}
    used_files = set()

    # If single file, treat it as full mix — run Demucs separation if available
    if len(files) == 1:
        single_file = files[0]
        classified['_full_mix'] = single_file

        # Try Demucs separation for better analysis
        separated = run_demucs_separation(single_file, quality=demucs_quality)
        if separated:
            # Use separated stems instead of the raw mix
            if separated.get('vocals'):
                classified['lead_vocal'] = separated['vocals']
            if separated.get('drums'):
                classified['drums'] = separated['drums']
            if separated.get('bass'):
                classified['bass'] = separated['bass']
            if separated.get('guitar'):
                classified['acoustic_guitar'] = separated['guitar']
            if separated.get('piano'):
                classified['piano'] = separated['piano']
            if separated.get('other'):
                # If we have guitar/piano separately, 'other' is truly other (pads, synths)
                if separated.get('guitar') or separated.get('piano'):
                    classified['keys'] = separated['other']
                else:
                    classified['piano'] = separated['other']  # "other" = guitars/keys/etc
            classified['_unclassified'] = []
            stem_count = len([k for k in separated.keys() if separated[k]])
            print(f"    Demucs separation complete — {stem_count} stems available")
        else:
            # Fallback: assign single file to all roles
            classified['drums'] = single_file
            classified['lead_vocal'] = single_file
            classified['piano'] = single_file
            classified['_unclassified'] = []

        return classified

    for role, keywords in TRACK_KEYWORDS.items():
        for f in files:
            if f in used_files:
                continue
            stem = Path(f).stem
            name = stem.lower().replace(' ', '').replace('_', '').replace('-', '')
            # NEVER treat our OWN generated clicks as the 'click' tempo source. Files named
            # Generated_Click_* (Auto/Manual/A/B/C) are OUTPUTS of this app; using one as the
            # beat-grid source creates a feedback loop where a wrong-tempo generated click becomes
            # the tempo for the next run. Only a real, authored click stem should drive TIER 1.
            if role == 'click' and stem.lower().startswith('generated_click'):
                continue
            for kw in keywords:
                if kw in name:
                    classified[role] = f
                    used_files.add(f)
                    break
            if role in classified:
                break

    # Any unclassified files go into 'other'
    classified['_unclassified'] = [f for f in files if f not in used_files]

    # If very few tracks were classified (e.g., 2-3 files with no keywords),
    # use the first unclassified file as a general analysis source
    if not classified.get('drums') and not classified.get('lead_vocal'):
        if classified['_unclassified']:
            general_file = classified['_unclassified'][0]
            if not classified.get('drums'):
                classified['drums'] = general_file
            if not classified.get('lead_vocal'):
                classified['lead_vocal'] = general_file
            if not classified.get('piano'):
                classified['piano'] = general_file

    return classified



# ═══════════════════════════════════════════════════════════════════════
# BEAT GRID DETECTION (from click track or librosa)
# ═══════════════════════════════════════════════════════════════════════

# Records what the last _fit_grid call decided, so callers can surface the analysis to the UI
# (verdict, metrics) and so the result JSON can report it for the analyze-first dialog.
_LAST_GRID_FIT: Dict[str, Any] = {}

# Grid mode: how the final grid is built from detected beats.
#   'auto'      — pick EVEN if an even grid fits within a quarter-beat everywhere, else following.
#   'even'      — FORCE a mathematically even (least-squares) grid regardless of fit.
#   'following' — FORCE a smoothed tempo-following grid regardless of fit.
# Set via the CLI (--grid-mode) / analyzer args so the UI's analyze-first choice is honored.
GRID_MODE = 'auto'


def _fit_grid(beat_positions, total_duration, mode: str = None):
    """Turn a list of detected beat times into the final grid, choosing between a mathematically
    EVEN grid (constant tempo) and a smoothed TEMPO-FOLLOWING grid (variable tempo).

    Shared by every detector tier (click, beat_this, librosa) so the output is consistent. A
    detector's raw beats are frame-quantized (they wobble ±1 frame) and may drift. We:
      1. assign each beat an integer index (robust to dropped beats at section gaps),
      2. least-squares fit beat[i] = anchor + i*interval,
      3. EVEN if (mode even) OR (mode auto AND the fit is tight) → emit the perfectly even grid,
         FOLLOWING otherwise → emit a moving-median-smoothed tempo-following grid.
    `mode` overrides the module GRID_MODE when given. Records metrics in _LAST_GRID_FIT.
    Returns (beats_list, bpm, beat_interval_sec, grid_anchor_sec).
    """
    import numpy as _np
    mode = (mode or GRID_MODE or 'auto').lower()
    raw = _np.asarray([b for b in beat_positions if 0.0 <= b <= total_duration], dtype=float)
    if len(raw) < 8:
        return None
    iv = _np.diff(raw)
    med = float(_np.median(iv))
    if med <= 0:
        return None
    idx = _np.zeros(len(raw))
    for i in range(1, len(raw)):
        idx[i] = idx[i - 1] + max(1, int(round((raw[i] - raw[i - 1]) / med)))
    A = _np.vstack([_np.ones(len(idx)), idx]).T
    (anchor_fit, interval_fit), *_ = _np.linalg.lstsq(A, raw, rcond=None)
    resid = (anchor_fit + interval_fit * idx) - raw
    max_dev_ms = float(_np.max(_np.abs(resid))) * 1000.0
    rms_ms = float(_np.sqrt(_np.mean(resid ** 2))) * 1000.0
    interval = float(interval_fit) if interval_fit > 0 else med
    even_fits = max_dev_ms < (interval * 1000.0 * 0.25)
    recommended = 'even' if even_fits else 'following'

    # Decide which grid to emit, honoring an explicit mode override.
    use_even = (mode == 'even') or (mode == 'auto' and even_fits)

    _LAST_GRID_FIT.clear()
    _LAST_GRID_FIT.update({
        'mode_requested': mode,
        'recommended': recommended,
        'even_fits': bool(even_fits),
        'fit_max_dev_ms': round(max_dev_ms, 2),
        'fit_rms_ms': round(rms_ms, 2),
        'raw_jitter_ms': round(float(_np.std(iv)) * 1000.0, 2),
        'fit_bpm': round(60.0 / interval, 3),
    })

    if use_even:
        anchor = float(anchor_fit)
        while anchor - interval >= 0:
            anchor -= interval
        while anchor < 0:
            anchor += interval
        beats = []
        t = anchor
        while t < total_duration:
            beats.append(t); t += interval
        _LAST_GRID_FIT['applied'] = 'even'
        forced = " (FORCED)" if mode == 'even' and not even_fits else ""
        print(f"      → EVEN grid{forced}: BPM={60.0/interval:.3f} "
              f"(fit max-dev {max_dev_ms:.1f} ms, rms {rms_ms:.1f} ms)")
        return beats, 60.0 / interval, interval, anchor

    # Tempo-following (smoothed).
    iv_cl = _np.clip(iv, med * 0.5, med * 1.5)
    halfw = 4
    sm_iv = _np.empty_like(iv_cl)
    for i in range(len(iv_cl)):
        lo = max(0, i - halfw); hi = min(len(iv_cl), i + halfw + 1)
        sm_iv[i] = _np.median(iv_cl[lo:hi])
    sm = _np.empty(len(raw)); sm[0] = raw[0]; sm[1:] = raw[0] + _np.cumsum(sm_iv)
    beats = [float(t) for t in sm if 0.0 <= t <= total_duration]
    interval = float(_np.median(sm_iv))
    _LAST_GRID_FIT['applied'] = 'following'
    forced = " (FORCED)" if mode == 'following' and even_fits else ""
    print(f"      → tempo-following grid{forced}: median BPM={60.0/interval:.2f} "
          f"(even-fit max-dev {max_dev_ms:.0f} ms; jitter "
          f"{float(_np.std(iv))*1000:.1f}→{float(_np.std(sm_iv))*1000:.1f} ms)")
    return beats, 60.0 / interval, interval, beats[0]


def _find_real_click_file(files: List[str]) -> Optional[str]:
    """Return a REAL (human-authored) click track from the file list, or None.

    Matches filenames containing a click keyword (click/metronome/met) but EXCLUDES this app's own
    generated clicks (Generated_Click_*), which must never seed the grid (feedback loop). Used so a
    folder that ships an authored click — e.g. 'Original CLICK.wav' — drives the grid even when the
    keyword classifier was bypassed (Demucs single-file path)."""
    click_keywords = ('click', 'metronome')
    for f in files:
        stem = Path(f).stem
        low = stem.lower()
        if low.startswith('generated_click'):
            continue
        norm = low.replace(' ', '').replace('_', '').replace('-', '')
        if any(kw in norm for kw in click_keywords):
            return f
    return None


def detect_beat_grid(classified: Dict, files: List[str], grid_mode: str = None) -> Dict[str, Any]:
    """Detect BPM and beat grid using tiered approach:
    Tier 1: Click track peak detection (sample-accurate)
    Tier 2: beat_this AI model (ISMIR 2024 SOTA, ~95% F1)
    Tier 3: librosa beat_track (fallback, ~75-80% F1)

    grid_mode ('auto'|'even'|'following') forces how the final grid is built (shared by all tiers).
    The returned grid carries a 'grid_fit' dict (verdict + metrics) so the UI can show the
    analyze-first dialog and so a forced mode can be honored."""
    global GRID_MODE
    if grid_mode:
        GRID_MODE = grid_mode.lower()
    print("__PROGRESS__|5|Detecting beat grid...")
    print("  [1/8] Detecting beat grid...")

    def _stamp(grid):
        """Attach the last _fit_grid decision/metrics to the grid for the caller/UI."""
        if grid is not None and _LAST_GRID_FIT:
            grid['grid_fit'] = dict(_LAST_GRID_FIT)
        return grid

    # TIER 1: Click track. A REAL (human-authored) click is ground truth for the beat grid, so it
    # always wins. Prefer the classified 'click' role, but ALSO scan the raw file list directly:
    # on the Create Multi-Track / Demucs single-file path the classifier never runs the keyword
    # matcher, so a folder that contains e.g. "Original CLICK.wav" alongside the mix would otherwise
    # be missed and the grid would fall back to (less reliable) music tempo estimation.
    click_path = classified.get('click')
    if not (click_path and os.path.exists(click_path)):
        click_path = _find_real_click_file(files)
    if click_path and os.path.exists(click_path):
        grid = analyze_click_track(click_path)
        if grid and grid['confidence'] > 0.7:
            print(f"    ✓ TIER 1 (click): BPM={grid['bpm']:.2f}, {len(grid['beats'])} beats "
                  f"(from {Path(click_path).name})")
            return _stamp(grid)
        elif grid:
            print(f"    ! Click file {Path(click_path).name} gave low confidence "
                  f"({grid.get('confidence', 0):.2f}); falling through to tempo estimation")

    analysis_file = classified.get('drums') or classified.get('bass')
    if not analysis_file:
        for f in files:
            if f != click_path:
                analysis_file = f
                break
    if not analysis_file:
        return None

    # TIER 2: beat_this AI
    grid = _detect_with_beat_this(analysis_file)
    # Only trust beat_this when its result is sane AND confident. On some inputs it returns garbage
    # (e.g. 375 BPM, confidence 0); reject those and fall through to the librosa prior tier.
    if grid and grid.get('confidence', 0) >= 0.5 and 40 <= grid['bpm'] <= 180:
        # beat_this returns its own frame-quantized beat positions (they wobble ±1 frame and can
        # drift). Run them through the SAME even-grid / tempo-following fit as every other tier so a
        # constant-tempo song gets a mathematically even click instead of a wobbly one.
        fitted = _fit_grid(grid['beats'], grid.get('total_duration_sec')
                           or (grid['beats'][-1] if grid['beats'] else 0))
        if fitted:
            beats, bpm, interval, anchor = fitted
            grid['beats'] = beats; grid['bpm'] = bpm
            grid['beat_interval_sec'] = interval; grid['grid_anchor_sec'] = anchor
        print(f"    ✓ TIER 2 (beat_this): BPM={grid['bpm']:.2f}, {len(grid['beats'])} beats")
        return _stamp(grid)
    elif grid:
        print(f"    ! beat_this result rejected (BPM={grid['bpm']:.2f}, "
              f"confidence={grid.get('confidence', 0):.2f}) — using librosa tier")

    # TIER 3: librosa fallback — STEADY grid at a music-derived tempo.
    #
    # Key lesson: librosa's per-beat beat_track output is NOISY (±22ms jitter) — that jitter is
    # DETECTION ERROR, not real tempo movement. Placing clicks on those raw beats makes the click
    # stutter; placing them on an even grid at a WRONG bpm makes it drift. The fix is to derive one
    # ACCURATE, STEADY tempo from the autocorrelation of a combined drum+bass onset envelope (the
    # user's "analyze drums/bass/chords" approach), then build an EVEN grid at that tempo. For a
    # genuinely steady worship track this stays locked; the downbeat PHASE is corrected separately
    # downstream from kick/snare/chord changes.
    print(f"    Using librosa (per-stem tempo consensus), lead: {Path(analysis_file).name}")
    import numpy as _np
    y, sr = librosa.load(analysis_file, sr=SAMPLE_RATE)
    total_duration = librosa.get_duration(y=y, sr=sr)

    def _tempo_of(onset_env):
        """Autocorrelation tempo of one onset envelope (version-robust across librosa)."""
        for fn in (
            lambda: librosa.feature.rhythm.tempo(onset_envelope=onset_env, sr=sr,
                        hop_length=HOP_LENGTH, start_bpm=100, aggregate=_np.median),
            lambda: librosa.feature.tempo(onset_envelope=onset_env, sr=sr,
                        hop_length=HOP_LENGTH, start_bpm=100, aggregate=_np.median),
            lambda: librosa.beat.tempo(onset_envelope=onset_env, sr=sr,
                        hop_length=HOP_LENGTH, start_bpm=100, aggregate=_np.median),
        ):
            try:
                return float(_np.ravel(fn())[0])
            except Exception:
                continue
        return 0.0

    # ── PER-STEM TEMPO CONSENSUS ──
    # The old method summed drums+bass into ONE onset envelope and autocorrelated the blob — so a
    # single noisy/odd stem (e.g. a piano playing a cross-rhythm) could drag the estimate to the
    # wrong metrical level (observed on a real song: piano said 120 while drums+guitar+bass agreed
    # at ~90). Instead we estimate tempo for EACH stem independently, fold every estimate into a
    # common octave, then take a WEIGHTED vote. The rhythm section (drums, bass) carries the pulse
    # most reliably, guitar next, piano/keys/other least (they sustain and autocorrelate at odd
    # multiples). The consensus is robust to one outlier stem and yields an agreement score.
    def _fold(bpm_val):
        """Fold a tempo into the worship range so estimates at different octaves can be voted
        together. Only OBVIOUS double/half-time is folded (>185 or <50)."""
        if bpm_val <= 0:
            return 0.0
        while bpm_val > 185:
            bpm_val /= 2.0
        while bpm_val < 50:
            bpm_val *= 2.0
        return bpm_val

    stem_weights = [('drums', 2.0), ('bass', 2.0), ('acoustic_guitar', 1.5),
                    ('electric_guitar', 1.5), ('piano', 1.0), ('keys', 0.5)]
    # Always include the lead analysis file (usually drums) as a drums vote.
    votes = []  # (bpm_folded, weight, role)
    seen_paths = set()
    lead_env = librosa.onset.onset_strength(y=y, sr=sr, hop_length=HOP_LENGTH)
    lead_bpm = _fold(_tempo_of(lead_env))
    if lead_bpm > 0:
        votes.append((lead_bpm, 2.0, 'lead'))
        seen_paths.add(analysis_file)
    for role, w in stem_weights:
        p = classified.get(role)
        if not p or not os.path.exists(p) or p in seen_paths:
            continue
        seen_paths.add(p)
        try:
            ys, _ = librosa.load(p, sr=sr)
            est = _fold(_tempo_of(librosa.onset.onset_strength(y=ys, sr=sr, hop_length=HOP_LENGTH)))
            if est > 0:
                votes.append((est, w, role))
                print(f"      stem tempo: {role:16s} {est:6.1f} BPM (weight {w})")
        except Exception:
            continue

    # Weighted vote: cluster votes that are within ~3% of each other, sum their weights, pick the
    # heaviest cluster, and use its weighted-mean BPM. Agreement = cluster weight / total weight.
    tempo_confidence = 0.0
    if votes:
        best_cluster, best_wsum = None, -1.0
        for center_bpm, _, _ in votes:
            members = [(b, w) for (b, w, _) in votes if abs(b - center_bpm) / center_bpm <= 0.03]
            wsum = sum(w for _, w in members)
            if wsum > best_wsum:
                best_wsum, best_cluster = wsum, members
        bpm = sum(b * w for b, w in best_cluster) / sum(w for _, w in best_cluster)
        total_w = sum(w for _, w, _ in votes)
        tempo_confidence = best_wsum / total_w if total_w > 0 else 0.0
        print(f"    ✓ Tempo consensus: {bpm:.2f} BPM "
              f"(agreement {tempo_confidence:.0%} across {len(votes)} stems)")
    else:
        # No stems produced a tempo — fall back to the lead file's beat_track tempo.
        t, _ = librosa.beat.beat_track(y=y, sr=sr, start_bpm=100)
        bpm = _fold(float(_np.ravel(t)[0]))
        print(f"    ! No stem tempo votes; fell back to beat_track {bpm:.2f} BPM")

    # Build the combined drum+bass onset envelope for the beat tracker below (seeded with the
    # consensus BPM so it locks onto the agreed metrical level).
    oenv = lead_env
    bass_path = classified.get('bass')
    if bass_path and os.path.exists(bass_path) and bass_path != analysis_file:
        try:
            yb, _ = librosa.load(bass_path, sr=sr)
            ob = librosa.onset.onset_strength(y=yb, sr=sr, hop_length=HOP_LENGTH)
            n = min(len(oenv), len(ob))
            oenv = oenv[:n] + ob[:n]
        except Exception:
            pass

    # TEMPO-FOLLOWING GRID (fixes drift). A single global BPM built into an EVEN grid drifts off
    # the music whenever the song's real tempo wanders even slightly: the error accumulates every
    # bar, so after ~10 bars the click is visibly off beat 1. Measured on real worship stems the
    # live tempo swings ~96–103 BPM around a 99 BPM median, and the MEAN beat interval differs from
    # the median by several ms — enough to accumulate seconds of drift across a song.
    #
    # librosa's dynamic-programming beat tracker returns the ACTUAL beat positions, which follow the
    # real tempo while still being smooth (it penalizes sudden tempo jumps, so it does NOT produce
    # the random ±jitter of raw onset peaks). Using those positions as the grid keeps the click
    # locked to the music for the whole song. We keep `bpm`/`beat_interval` as summary values
    # (median interval) for display and for count-in spacing.
    beats = []
    try:
        _tempo, beat_frames = librosa.beat.beat_track(
            onset_envelope=oenv, sr=sr, hop_length=HOP_LENGTH, start_bpm=bpm, trim=False)
        beat_times = librosa.frames_to_time(beat_frames, sr=sr, hop_length=HOP_LENGTH)
        beats = [float(t) for t in beat_times if 0.0 <= t <= total_duration]
    except Exception as _e:
        print(f"    ! beat_track failed ({_e}); falling back to even grid")

    if len(beats) >= 8:
        # Run the librosa beat positions through the shared even-grid / tempo-following fit (same
        # logic used for the click and beat_this tiers): constant tempo → mathematically EVEN grid
        # (no wobble, no drift); variable tempo → smoothed tempo-following grid.
        print("    TIER 3 (librosa beat-track):")
        fitted = _fit_grid(beats, total_duration)
        if fitted:
            fb, fbpm, finterval, fanchor = fitted
            return _stamp({
                'bpm': fbpm, 'beat_interval_sec': finterval,
                'grid_anchor_sec': fanchor, 'total_duration_sec': total_duration,
                'beats': fb, 'downbeats': [], 'confidence': 0.82
            })

    # Fallback: too few beats detected — build an even grid from the global tempo as before.
    ot = librosa.frames_to_time(_np.arange(len(oenv)), sr=sr, hop_length=HOP_LENGTH)
    from scipy.signal import find_peaks as _fp
    pk, _ = _fp(oenv, height=float(_np.max(oenv)) * 0.3, distance=int((60.0 / bpm) * sr / HOP_LENGTH * 0.5))
    grid_anchor = float(ot[pk[0]]) if len(pk) > 0 else 0.0
    beat_interval = 60.0 / bpm
    beats = []
    t = grid_anchor
    while t < total_duration:
        beats.append(t)
        t += beat_interval
    print(f"    ✓ TIER 3 (librosa even-grid fallback): BPM={bpm:.2f} ({len(beats)} beats)")
    return {
        'bpm': bpm, 'beat_interval_sec': beat_interval,
        'grid_anchor_sec': grid_anchor, 'total_duration_sec': total_duration,
        'beats': beats, 'downbeats': [], 'confidence': 0.7
    }


def _detect_with_beat_this(audio_path: str) -> Optional[Dict]:
    """Use beat_this (ISMIR 2024) for state-of-the-art beat+downbeat detection.

    IMPORTANT: beat_this's File2Beats decodes the file with its own backend (soxr/mpg123),
    which chokes on some MP3s (ID3/decoder errors). To stay robust we decode the audio ourselves
    with librosa (handles mp3/wav/flac reliably) and feed the ARRAY to Audio2Beats — the same
    file-decode-bypass pattern used for Whisper.
    """
    try:
        from beat_this.inference import Audio2Beats
        import torch
        device = "cuda" if torch.cuda.is_available() else "cpu"
        print(f"    Running beat_this (GPU: {torch.cuda.is_available()})...")

        # Decode ourselves (mono float32). beat_this expects the audio signal + its sample rate.
        signal, sr = librosa.load(audio_path, sr=22050, mono=True)
        signal = np.asarray(signal, dtype=np.float32)

        # checkpoint_path must be a TRAINED checkpoint shortname ("final0") or a path/URL.
        # Passing None builds an UNINITIALIZED BeatThis() with random weights, which produces
        # garbage (nonsense BPM, zero confidence) — the cause of the earlier 375 BPM result.
        checkpoint = os.environ.get("OWC_BEATTHIS_CHECKPOINT", "final0")
        a2b = Audio2Beats(checkpoint_path=checkpoint, device=device,
                          float16=(device == 'cuda'), dbn=False)
        beats_sec, downbeats_sec = a2b(signal, sr)

        beats_sec = np.asarray(beats_sec, dtype=float)
        downbeats_sec = np.asarray(downbeats_sec, dtype=float)
        if len(beats_sec) < 4:
            print("    ! beat_this returned too few beats")
            return None

        intervals = np.diff(beats_sec)
        beat_interval = float(np.median(intervals))
        bpm = 60.0 / beat_interval
        total_duration = librosa.get_duration(path=audio_path)
        confidence = max(0, 1.0 - (float(np.std(intervals)) / beat_interval) * 5)
        return {
            'bpm': bpm, 'beat_interval_sec': beat_interval,
            'grid_anchor_sec': float(beats_sec[0]),
            'total_duration_sec': total_duration,
            'beats': beats_sec.tolist(),
            'downbeats': downbeats_sec.tolist(),
            'confidence': min(0.95, confidence)
        }
    except ImportError:
        print(f"    ! beat_this not installed (pip install beat-this)")
        return None
    except Exception as e:
        print(f"    ! beat_this failed: {e}")
        import traceback
        traceback.print_exc()
        return None



def analyze_click_track(click_path: str) -> Optional[Dict]:
    """Analyze click track for exact BPM and beat positions.
    Uses high-resolution onset detection at native sample rate for
    sample-accurate peak positions. BPM is derived FROM the actual
    peak intervals (guaranteed to match)."""
    y, sr = librosa.load(click_path, sr=44100)

    # High-resolution onset detection (hop_length=64 = ~1.5ms precision at 44100Hz)
    hop = 64
    onset_env = librosa.onset.onset_strength(y=y, sr=sr, hop_length=hop)
    onset_times = librosa.times_like(onset_env, sr=sr, hop_length=hop)

    peak_height = np.max(onset_env) * 0.3
    min_distance = int(sr * 0.25 / hop)  # Min 250ms between clicks (max ~240 BPM)
    peaks, properties = find_peaks(onset_env, height=peak_height, distance=min_distance)

    if len(peaks) < 4:
        return None

    # Refine peak positions to sub-frame accuracy using parabolic interpolation
    click_times = []
    for peak_idx in peaks:
        if peak_idx > 0 and peak_idx < len(onset_env) - 1:
            # Parabolic interpolation for sub-sample accuracy
            alpha = float(onset_env[peak_idx - 1])
            beta = float(onset_env[peak_idx])
            gamma = float(onset_env[peak_idx + 1])
            if (alpha + gamma - 2 * beta) != 0:
                correction = 0.5 * (alpha - gamma) / (alpha + gamma - 2 * beta)
            else:
                correction = 0.0
            refined_idx = peak_idx + correction
            refined_time = float(refined_idx * hop / sr)
        else:
            refined_time = float(onset_times[peak_idx])
        click_times.append(refined_time)

    click_times = np.array(click_times)

    if len(click_times) < 4:
        return None

    intervals = np.diff(click_times)
    median_interval = np.median(intervals)

    # Filter count-in (first interval significantly different)
    clean_mask = np.abs(intervals - median_interval) / median_interval < 0.15
    first_good = 0
    if len(intervals) > 0 and not clean_mask[0]:
        first_good = 1

    clean_clicks = click_times[first_good:]
    clean_intervals = np.diff(clean_clicks)
    if len(clean_intervals) < 3:
        clean_clicks = click_times
        clean_intervals = intervals

    total_duration = float(len(y) / sr)

    # EVEN-GRID FIT. The click's raw peaks have small timing noise (onset refinement ±~1 ms plus any
    # real tempo wobble). To make the GENERATED click match the ORIGINAL's even spacing exactly, fit
    # beat[i] = anchor + i*interval by least squares (same approach as the music path). Assign each
    # peak an integer beat index from its time so dropped/extra peaks don't corrupt the fit. If the
    # even grid fits well (a programmed/metronomic click — the usual case) we emit the perfectly even
    # grid; if it fits poorly (a hand-played/变 tempo click) we keep the ACTUAL peak positions so the
    # click still tracks the performance.
    cc = np.asarray(clean_clicks, dtype=float)
    med_iv = float(np.median(clean_intervals))
    if med_iv <= 0:
        med_iv = float(np.median(intervals)) or 0.5
    idx = np.zeros(len(cc))
    for i in range(1, len(cc)):
        step = max(1, int(round((cc[i] - cc[i - 1]) / med_iv)))
        idx[i] = idx[i - 1] + step
    A = np.vstack([np.ones(len(idx)), idx]).T
    (anchor_fit, interval_fit), *_ = np.linalg.lstsq(A, cc, rcond=None)
    resid = (anchor_fit + interval_fit * idx) - cc
    max_dev_ms = float(np.max(np.abs(resid))) * 1000.0
    rms_ms = float(np.sqrt(np.mean(resid ** 2))) * 1000.0

    print(f"    Click peaks detected: {len(cc)} beats")
    print(f"    Resolution: {hop/sr*1000:.1f}ms + parabolic refinement")

    if interval_fit > 0 and max_dev_ms < (interval_fit * 1000.0 * 0.25):
        beat_interval = float(interval_fit)
        bpm = 60.0 / beat_interval
        grid_anchor = float(anchor_fit)
        while grid_anchor - beat_interval >= 0:
            grid_anchor -= beat_interval
        while grid_anchor < 0:
            grid_anchor += beat_interval
        beats = []
        t = grid_anchor
        while t < total_duration:
            beats.append(t)
            t += beat_interval
        print(f"    → constant-tempo click: EVEN grid BPM={bpm:.3f} "
              f"(fit max-dev {max_dev_ms:.1f} ms, rms {rms_ms:.1f} ms)")
        return {
            'bpm': bpm, 'beat_interval_sec': beat_interval,
            'grid_anchor_sec': grid_anchor, 'total_duration_sec': total_duration,
            'beats': beats, 'confidence': 0.95
        }

    # Variable-tempo click → keep the real peak positions.
    beat_interval = med_iv
    bpm = 60.0 / beat_interval
    grid_anchor = float(cc[0])
    confidence = max(0, 1.0 - (float(np.std(clean_intervals)) / beat_interval) * 10)
    print(f"    → variable-tempo click: keeping actual peak positions "
          f"(even-fit max-dev {max_dev_ms:.0f} ms)")
    return {
        'bpm': bpm,
        'beat_interval_sec': beat_interval,
        'grid_anchor_sec': grid_anchor,
        'total_duration_sec': total_duration,
        'beats': cc.tolist(),  # ACTUAL peak positions
        'confidence': confidence
    }



# ═══════════════════════════════════════════════════════════════════════
# TASK #1: PER-BAR FEATURE EXTRACTION (RMS, onset, activation)
# ═══════════════════════════════════════════════════════════════════════

def extract_per_bar_features(classified: Dict, grid: Dict, time_sig: int = 4) -> Dict[str, Any]:
    """Extract RMS energy and activation per bar for every stem."""
    print("__PROGRESS__|15|Extracting per-bar energy from all stems...")
    print("  [2/8] Extracting per-bar features from all stems...")

    bar_duration = grid['beat_interval_sec'] * time_sig
    grid_anchor = grid['grid_anchor_sec']
    total_duration = grid['total_duration_sec']
    total_bars = int((total_duration - grid_anchor) / bar_duration)

    bar_data = {}  # role -> list of per-bar RMS values
    activation_map = {}  # role -> list of bool (active/inactive per bar)

    stems_to_analyze = {k: v for k, v in classified.items()
                        if k != '_unclassified' and k != '_full_mix' and v and k != 'click'}

    # In single-track mode, analyze the full mix as one stem
    if '_full_mix' in classified:
        stems_to_analyze = {'full_mix': classified['_full_mix']}

    for role, filepath in stems_to_analyze.items():
        if not filepath or not os.path.exists(filepath):
            continue

        try:
            y, sr = librosa.load(filepath, sr=SAMPLE_RATE)
        except Exception as e:
            print(f"    ! Could not load {role}: {e}")
            continue

        rms_per_bar = []
        for bar_idx in range(total_bars):
            bar_start_sec = grid_anchor + bar_idx * bar_duration
            bar_end_sec = bar_start_sec + bar_duration
            start_sample = int(bar_start_sec * sr)
            end_sample = int(bar_end_sec * sr)

            if start_sample >= len(y):
                rms_per_bar.append(0.0)
                continue

            segment = y[start_sample:min(end_sample, len(y))]
            rms = float(np.sqrt(np.mean(segment ** 2))) if len(segment) > 0 else 0.0
            rms_per_bar.append(rms)

        # Normalize RMS to 0-1 range
        max_rms = max(rms_per_bar) if rms_per_bar else 1.0
        if max_rms > 0:
            rms_normalized = [r / max_rms for r in rms_per_bar]
        else:
            rms_normalized = rms_per_bar

        # Determine activation threshold (>10% of max = active)
        threshold = 0.10
        active = [r > threshold for r in rms_normalized]

        bar_data[role] = rms_normalized
        activation_map[role] = active
        active_bars = sum(active)
        print(f"    ✓ {role}: {active_bars}/{total_bars} bars active")

    return {
        'total_bars': total_bars,
        'bar_duration_sec': bar_duration,
        'rms_per_bar': bar_data,
        'activation': activation_map
    }



# ═══════════════════════════════════════════════════════════════════════
# TASK #2: CHORD PROGRESSION DETECTION
# ═══════════════════════════════════════════════════════════════════════

CHORD_TEMPLATES = {
    'C':  [1,0,0,0,1,0,0,1,0,0,0,0],
    'Cm': [1,0,0,1,0,0,0,1,0,0,0,0],
    'Db': [0,1,0,0,0,1,0,0,1,0,0,0],
    'D':  [0,0,1,0,0,0,1,0,0,1,0,0],
    'Dm': [0,0,1,0,0,1,0,0,0,1,0,0],
    'Eb': [0,0,0,1,0,0,0,1,0,0,1,0],
    'Ebm':[0,0,0,1,0,0,1,0,0,0,1,0],
    'E':  [0,0,0,0,1,0,0,0,1,0,0,1],
    'Em': [0,0,0,0,1,0,0,1,0,0,0,1],
    'F':  [1,0,0,0,0,1,0,0,0,1,0,0],
    'Fm': [1,0,0,0,0,1,0,0,1,0,0,0],
    'Gb': [0,1,0,0,0,0,1,0,0,0,1,0],
    'G':  [0,0,1,0,0,0,0,1,0,0,0,1],
    'Gm': [0,0,1,0,0,0,0,1,0,0,1,0],
    'Ab': [1,0,0,1,0,0,0,0,1,0,0,0],
    'A':  [0,1,0,0,1,0,0,0,0,1,0,0],
    'Am': [1,0,0,0,1,0,0,0,0,1,0,0],
    'Bb': [0,0,1,0,0,1,0,0,0,0,1,0],
    'Bbm':[0,0,1,0,0,0,1,0,0,0,1,0],
    'B':  [0,0,0,1,0,0,1,0,0,0,0,1],
    'Bm': [0,0,0,1,0,0,0,1,0,0,0,1],
}


def detect_chords_per_bar(classified: Dict, grid: Dict, time_sig: int = 4) -> List[str]:
    """Detect chord for each bar using chroma features from harmonic stems."""
    print("__PROGRESS__|30|Detecting chord progression...")
    print("  [3/8] Detecting chord progression...")

    # Prefer piano > keys > acoustic guitar > electric guitar
    harmonic_stem = (classified.get('piano') or classified.get('keys') or
                     classified.get('acoustic_guitar') or classified.get('electric_guitar'))

    if not harmonic_stem or not os.path.exists(harmonic_stem):
        print("    ! No harmonic stem found, skipping chord detection")
        return []

    y, sr = librosa.load(harmonic_stem, sr=SAMPLE_RATE)
    bar_duration = grid['beat_interval_sec'] * time_sig
    grid_anchor = grid['grid_anchor_sec']
    total_bars = int((grid['total_duration_sec'] - grid_anchor) / bar_duration)

    chords = []
    for bar_idx in range(total_bars):
        bar_start = grid_anchor + bar_idx * bar_duration
        bar_end = bar_start + bar_duration
        start_sample = int(bar_start * sr)
        end_sample = int(bar_end * sr)

        if start_sample >= len(y):
            chords.append('N')
            continue

        segment = y[start_sample:min(end_sample, len(y))]
        if len(segment) < 2048:
            chords.append('N')
            continue

        # Compute chroma for this bar
        chroma = librosa.feature.chroma_cqt(y=segment, sr=sr, hop_length=HOP_LENGTH)
        chroma_mean = np.mean(chroma, axis=1)

        # Match against chord templates
        best_chord = 'N'
        best_score = -1
        for chord_name, template in CHORD_TEMPLATES.items():
            score = float(np.dot(chroma_mean, template))
            if score > best_score:
                best_score = score
                best_chord = chord_name

        chords.append(best_chord)

    print(f"    ✓ Detected chords for {len(chords)} bars")
    # Show first 8 bars as preview
    preview = ' - '.join(chords[:8])
    print(f"    Preview: {preview}...")
    return chords



# ═══════════════════════════════════════════════════════════════════════
# TASK #3: VOCAL ANALYSIS (onset, melody contour, BGV)
# ═══════════════════════════════════════════════════════════════════════

def analyze_vocals(classified: Dict, grid: Dict, time_sig: int = 4) -> Dict[str, Any]:
    """Analyze vocal tracks for onset, melody contour, and BGV presence."""
    print("__PROGRESS__|45|Analyzing vocals (onset, melody, BGV)...")
    print("  [4/8] Analyzing vocals...")

    bar_duration = grid['beat_interval_sec'] * time_sig
    grid_anchor = grid['grid_anchor_sec']
    total_duration = grid['total_duration_sec']
    total_bars = int((total_duration - grid_anchor) / bar_duration)

    result = {
        'vocal_onset_bar': None,
        'vocal_offset_bar': None,
        'vocal_active_bars': [False] * total_bars,
        'bgv_active_bars': [False] * total_bars,
        'melody_contour': [],  # Average pitch per bar
    }

    # Lead vocal analysis
    vocal_path = classified.get('lead_vocal')
    if vocal_path and os.path.exists(vocal_path):
        y, sr = librosa.load(vocal_path, sr=SAMPLE_RATE)

        # Per-bar vocal activity
        for bar_idx in range(total_bars):
            bar_start = grid_anchor + bar_idx * bar_duration
            bar_end = bar_start + bar_duration
            start_sample = int(bar_start * sr)
            end_sample = int(bar_end * sr)
            if start_sample >= len(y):
                break
            segment = y[start_sample:min(end_sample, len(y))]
            rms = float(np.sqrt(np.mean(segment ** 2)))
            # Vocal is "active" if RMS > 5% of max
            result['vocal_active_bars'][bar_idx] = rms > 0.01

        # Find first and last active bars
        for i, active in enumerate(result['vocal_active_bars']):
            if active:
                result['vocal_onset_bar'] = i + 1  # 1-based
                break
        for i in range(len(result['vocal_active_bars']) - 1, -1, -1):
            if result['vocal_active_bars'][i]:
                result['vocal_offset_bar'] = i + 1
                break

        # Melody contour (average f0 per bar)
        try:
            f0, voiced_flag, _ = librosa.pyin(
                y, fmin=80, fmax=800, sr=sr, hop_length=HOP_LENGTH)
            times = librosa.times_like(f0, sr=sr, hop_length=HOP_LENGTH)

            for bar_idx in range(total_bars):
                bar_start = grid_anchor + bar_idx * bar_duration
                bar_end = bar_start + bar_duration
                mask = (times >= bar_start) & (times < bar_end) & voiced_flag
                if np.any(mask):
                    avg_f0 = float(np.nanmean(f0[mask]))
                    result['melody_contour'].append(round(avg_f0, 1))
                else:
                    result['melody_contour'].append(0)
        except Exception:
            result['melody_contour'] = [0] * total_bars

        print(f"    ✓ Lead vocal onset: bar {result['vocal_onset_bar']}, "
              f"offset: bar {result['vocal_offset_bar']}")
    else:
        print("    ! No lead vocal stem found")

    # BGV analysis
    bgv_path = classified.get('bgv')
    if bgv_path and os.path.exists(bgv_path):
        y_bgv, sr = librosa.load(bgv_path, sr=SAMPLE_RATE)
        for bar_idx in range(total_bars):
            bar_start = grid_anchor + bar_idx * bar_duration
            bar_end = bar_start + bar_duration
            start_sample = int(bar_start * sr)
            end_sample = int(bar_end * sr)
            if start_sample >= len(y_bgv):
                break
            segment = y_bgv[start_sample:min(end_sample, len(y_bgv))]
            rms = float(np.sqrt(np.mean(segment ** 2)))
            result['bgv_active_bars'][bar_idx] = rms > 0.01

        bgv_count = sum(result['bgv_active_bars'])
        print(f"    ✓ BGV active in {bgv_count}/{total_bars} bars")
    else:
        print("    ! No BGV stem found")

    return result



# ═══════════════════════════════════════════════════════════════════════
# TASK #4: WHISPER INTEGRATION (lyrics + cue track)
# ═══════════════════════════════════════════════════════════════════════

def transcribe_with_whisper(classified: Dict, grid: Dict, time_sig: int = 4) -> Dict[str, Any]:
    """Transcribe vocals for lyrics and cue track for section labels.
    Uses local Whisper model for accurate transcription with word timestamps."""
    print("__PROGRESS__|55|Transcribing lyrics and cue track (Whisper)...")
    print("  [5/8] Transcribing with Whisper...")

    bar_duration = grid['beat_interval_sec'] * time_sig
    grid_anchor = grid['grid_anchor_sec']
    total_bars = int((grid['total_duration_sec'] - grid_anchor) / bar_duration)

    result = {
        'lyrics_per_bar': [''] * total_bars,
        'cue_labels': {},  # bar_number -> label text
        'whisper_available': False,
        'word_timestamps': [],  # List of {word, start_sec, end_sec} for chord chart
    }

    # Pin NLTK's data search path to a SINGLE writable local folder BEFORE importing WhisperX.
    # WhisperX's forced-alignment imports NLTK, whose default search path probes the root of every
    # drive (C:\nltk_data, D:\nltk_data, E:\nltk_data, …). A BitLocker-locked drive (e.g. E:) makes
    # that existence-probe THROW, which silently kills alignment. Restricting nltk.data.path to one
    # folder we control avoids touching any locked drive. The punkt/punkt_tab data lives here.
    try:
        # sys.executable is <venv>\Scripts\python.exe → <venv>\nltk_data
        _nltk_dir = os.environ.get('OWC_NLTK_DATA') or os.path.join(
            os.path.dirname(os.path.dirname(os.path.abspath(sys.executable))), 'nltk_data')
        os.environ['NLTK_DATA'] = _nltk_dir
        import nltk  # noqa
        nltk.data.path[:] = [_nltk_dir]  # replace the whole list → never probe drive roots
    except Exception as _nltk_e:
        print(f"    (NLTK path setup skipped: {_nltk_e})")

    # Device for GPU-accelerated transcription (RTX 3090s when present).
    try:
        import torch
        _device = 'cuda' if torch.cuda.is_available() else 'cpu'
    except Exception:
        _device = 'cpu'

    preferred = os.environ.get('OWC_WHISPER_MODEL', 'large-v3').strip() or 'large-v3'

    # ── PRIMARY: WhisperX (faster-whisper transcription + wav2vec2 FORCED ALIGNMENT) ──
    # WhisperX gives far more accurate WORD-LEVEL timestamps than raw Whisper, which is exactly
    # what lyric-to-section timing depends on. We transcribe, then align to get per-word start/end.
    # Normalized output matches the openai-whisper shape used downstream:
    #   {'segments': [{'text','start','end','words':[{'word','start','end'}]}]}
    _wx = None
    _wx_model = None
    _wx_align_cache = {}
    try:
        import whisperx as _wx  # noqa
        result['whisper_available'] = True
        compute_type = 'float16' if _device == 'cuda' else 'int8'
        try:
            print(f"    Loading WhisperX model '{preferred}' on {_device} ({compute_type})…")
            _wx_model = _wx.load_model(preferred, _device, compute_type=compute_type)
            result['whisper_model'] = f"whisperx:{preferred}"
            print(f"    ✓ WhisperX '{preferred}' loaded (forced-alignment word timing)")
        except Exception as e:
            print(f"    ! WhisperX model load failed ({e}); will fall back to openai-whisper")
            _wx = None
    except ImportError:
        _wx = None  # WhisperX not installed — fall back below.

    # ── FALLBACK: openai-whisper (word_timestamps, no forced alignment) ──
    _ow_model = None
    if _wx_model is None:
        try:
            import whisper
            result['whisper_available'] = True
        except ImportError:
            print("    ! Neither WhisperX nor openai-whisper is installed — skipping transcription")
            return result
        _chain = []
        for m in [preferred, 'medium', 'small', 'base']:
            if m not in _chain:
                _chain.append(m)
        for name in _chain:
            try:
                print(f"    Loading Whisper model '{name}' on {_device}…")
                _ow_model = whisper.load_model(name, device=_device)
                result['whisper_model'] = name
                print(f"    ✓ Whisper model '{name}' loaded")
                break
            except Exception as e:
                print(f"    ! Could not load Whisper '{name}': {e}")
        if _ow_model is None:
            print("    ! No Whisper model could be loaded — skipping transcription")
            return result

    def _transcribe(path: str) -> Dict:
        """Transcribe an audio file and return {'segments':[{text,start,end,words:[...]}]}.

        Uses WhisperX (transcribe + forced alignment) when available for precise word timing,
        otherwise openai-whisper. Audio is decoded with librosa (16 kHz mono float32) so we never
        depend on a system ffmpeg being on PATH.
        """
        try:
            audio, _ = librosa.load(path, sr=16000, mono=True)
            audio = np.asarray(audio, dtype=np.float32)
        except Exception as decode_err:
            print(f"      (librosa decode failed: {decode_err})")
            audio = path  # last resort: let the backend decode via ffmpeg

        # WhisperX path
        if _wx_model is not None:
            try:
                tr = _wx_model.transcribe(audio, batch_size=16)
                lang = tr.get('language', 'en')
                segs = tr.get('segments', [])
                # Forced alignment for accurate word timestamps.
                try:
                    if lang not in _wx_align_cache:
                        _wx_align_cache[lang] = _wx.load_align_model(language_code=lang, device=_device)
                    align_model, metadata = _wx_align_cache[lang]
                    aligned = _wx.align(segs, align_model, metadata, audio, _device,
                                        return_char_alignments=False)
                    segs = aligned.get('segments', segs)
                except Exception as ae:
                    print(f"      (WhisperX alignment skipped: {ae})")
                # Normalize to the openai-whisper shape.
                out_segs = []
                for s in segs:
                    words = []
                    for w in s.get('words', []) or []:
                        wt = (w.get('word') or '').strip()
                        if not wt:
                            continue
                        st = w.get('start'); en = w.get('end')
                        if st is None:
                            st = s.get('start', 0)
                        if en is None:
                            en = w.get('start', st)
                        words.append({'word': wt, 'start': float(st), 'end': float(en)})
                    out_segs.append({
                        'text': s.get('text', ''),
                        'start': float(s.get('start', words[0]['start'] if words else 0)),
                        'end': float(s.get('end', words[-1]['end'] if words else 0)),
                        'words': words,
                    })
                return {'segments': out_segs}
            except Exception as wxe:
                print(f"      (WhisperX transcription failed: {wxe}; using openai-whisper if available)")

        # openai-whisper path
        if _ow_model is not None:
            try:
                return _ow_model.transcribe(audio, word_timestamps=True)
            except Exception as decode_err:
                print(f"      (array transcribe failed: {decode_err}; trying ffmpeg path)")
                return _ow_model.transcribe(path, word_timestamps=True)
        return {'segments': []}

    # Transcribe cue track first (highest value — may give us sections directly)
    cue_path = classified.get('cue')
    if cue_path and os.path.exists(cue_path):
        print(f"    Transcribing cue track: {Path(cue_path).name}")
        try:
            cue_result = _transcribe(cue_path)

            for segment in cue_result.get('segments', []):
                seg_start = segment['start']
                elapsed = seg_start - grid_anchor
                if elapsed >= 0:
                    bar_num = int(elapsed / bar_duration) + 1
                    text = segment['text'].strip().lower()
                    if text and bar_num <= total_bars:
                        result['cue_labels'][bar_num] = text
                        print(f"      Cue at bar {bar_num}: \"{text}\"")
        except Exception as e:
            print(f"    ! Cue transcription failed: {e}")

    # Transcribe lead vocal for lyrics
    vocal_path = classified.get('lead_vocal')
    if vocal_path and os.path.exists(vocal_path):
        print(f"    Transcribing vocals: {Path(vocal_path).name}")
        try:
            vocal_result = _transcribe(vocal_path)

            for segment in vocal_result.get('segments', []):
                seg_start = segment['start']
                seg_end = segment['end']
                text = segment['text'].strip()
                if not text:
                    continue

                # Collect word-level timestamps for chord chart
                for word_info in segment.get('words', []):
                    word_text = word_info.get('word', '').strip()
                    if word_text:
                        result['word_timestamps'].append({
                            'word': word_text,
                            'start_sec': float(word_info.get('start', 0)),
                            'end_sec': float(word_info.get('end', 0))
                        })

                # Map to bar(s)
                start_bar = max(1, int((seg_start - grid_anchor) / bar_duration) + 1)
                end_bar = min(total_bars, int((seg_end - grid_anchor) / bar_duration) + 1)

                for b in range(start_bar - 1, end_bar):
                    if 0 <= b < total_bars:
                        if result['lyrics_per_bar'][b]:
                            result['lyrics_per_bar'][b] += ' ' + text
                        else:
                            result['lyrics_per_bar'][b] = text

            non_empty = sum(1 for l in result['lyrics_per_bar'] if l)
            print(f"    ✓ Lyrics in {non_empty}/{total_bars} bars")
            print(f"    ✓ Word timestamps: {len(result['word_timestamps'])} words")
        except Exception as e:
            print(f"    ! Vocal transcription failed: {e}")

    return result



# ═══════════════════════════════════════════════════════════════════════
# TASK #5: DRUM FILL DETECTION
# ═══════════════════════════════════════════════════════════════════════

def detect_drum_fills(classified: Dict, grid: Dict, time_sig: int = 4) -> List[int]:
    """Detect drum fills as section boundary markers.
    A drum fill is a short burst of high onset density in the last beat(s) of a bar.
    Returns list of bar numbers where fills occur (1-based)."""
    print("__PROGRESS__|70|Detecting drum fills (section boundaries)...")
    print("  [6/8] Detecting drum fills...")

    drum_path = classified.get('drums')
    if not drum_path or not os.path.exists(drum_path):
        print("    ! No drum stem found, skipping fill detection")
        return []

    y, sr = librosa.load(drum_path, sr=SAMPLE_RATE)
    bar_duration = grid['beat_interval_sec'] * time_sig
    beat_duration = grid['beat_interval_sec']
    grid_anchor = grid['grid_anchor_sec']
    total_bars = int((grid['total_duration_sec'] - grid_anchor) / bar_duration)

    # Compute onset strength
    onset_env = librosa.onset.onset_strength(y=y, sr=sr, hop_length=HOP_LENGTH)
    times = librosa.times_like(onset_env, sr=sr, hop_length=HOP_LENGTH)

    fill_bars = []

    for bar_idx in range(total_bars):
        bar_start = grid_anchor + bar_idx * bar_duration
        bar_end = bar_start + bar_duration

        # Look at the last beat of the bar (where fills typically happen)
        fill_region_start = bar_end - beat_duration
        fill_region_end = bar_end

        # Get onset strength in this region
        mask = (times >= fill_region_start) & (times < fill_region_end)
        region_onsets = onset_env[mask]

        if len(region_onsets) == 0:
            continue

        # Also get the "normal" region (first 3 beats) for comparison
        normal_mask = (times >= bar_start) & (times < fill_region_start)
        normal_onsets = onset_env[normal_mask]

        if len(normal_onsets) == 0:
            continue

        # A fill has significantly higher onset density in the last beat
        fill_density = np.mean(region_onsets)
        normal_density = np.mean(normal_onsets)

        # Fill if last beat is >2x the density of the rest AND above a threshold
        if fill_density > normal_density * 2.0 and fill_density > np.max(onset_env) * 0.3:
            fill_bars.append(bar_idx + 1)  # 1-based

    print(f"    ✓ Drum fills detected at bars: {fill_bars}")
    return fill_bars



# ═══════════════════════════════════════════════════════════════════════
# KEY DETECTION
# ═══════════════════════════════════════════════════════════════════════

def detect_key(classified: Dict) -> str:
    """Detect musical key from a harmonic stem."""
    harmonic_stem = (classified.get('piano') or classified.get('keys') or
                     classified.get('acoustic_guitar') or classified.get('electric_guitar') or
                     classified.get('lead_vocal'))
    if not harmonic_stem or not os.path.exists(harmonic_stem):
        return 'C'

    y, sr = librosa.load(harmonic_stem, sr=SAMPLE_RATE, duration=120)
    chroma = librosa.feature.chroma_cqt(y=y, sr=sr)
    chroma_mean = np.mean(chroma, axis=1)

    major_profile = np.array([6.35,2.23,3.48,2.33,4.38,4.09,2.52,5.19,2.39,3.66,2.29,2.88])
    minor_profile = np.array([6.33,2.68,3.52,5.38,2.60,3.53,2.54,4.75,3.98,2.69,3.34,3.17])
    note_names = ['C','Db','D','Eb','E','F','Gb','G','Ab','A','Bb','B']

    best_key = 'C'
    best_corr = -1
    for i in range(12):
        rotated = np.roll(chroma_mean, -i)
        maj_corr = float(np.corrcoef(rotated, major_profile)[0, 1])
        min_corr = float(np.corrcoef(rotated, minor_profile)[0, 1])
        if maj_corr > best_corr:
            best_corr = maj_corr
            best_key = note_names[i]
        if min_corr > best_corr:
            best_corr = min_corr
            best_key = note_names[i] + 'm'

    return best_key



# ═══════════════════════════════════════════════════════════════════════
# TASK #6: LLM PROMPT ASSEMBLER
# ═══════════════════════════════════════════════════════════════════════

def assemble_llm_prompt(grid: Dict, features: Dict, chords: List[str],
                        vocal_data: Dict, whisper_data: Dict,
                        drum_fills: List[int], key: str,
                        time_sig: int = 4, melody_groups: List[Dict] = None,
                        section_hints: List[str] = None) -> str:
    """Assemble all extracted signals into a structured LLM prompt."""
    print("__PROGRESS__|80|Assembling analysis data for LLM...")
    print("  [7/8] Assembling LLM prompt...")

    total_bars = features['total_bars']
    bar_duration_ms = features['bar_duration_sec'] * 1000

    # Build the prompt
    prompt = f"""You are a music structure analysis expert specializing in worship music.
Analyze the following multitrack audio data and identify the song sections.

SONG INFO:
- BPM: {grid['bpm']:.2f}
- Key: {key}
- Time Signature: {time_sig}/4
- Total Bars: {total_bars}
- Bar Duration: {bar_duration_ms:.0f} ms

"""

    # Add section hints if user provided them (HIGHEST PRIORITY - override LLM decisions)
    if section_hints and len(section_hints) > 0:
        prompt += "SECTION STRUCTURE (PROVIDED BY USER — MUST USE THESE EXACT SECTIONS IN THIS ORDER):\n"
        prompt += f"This song has EXACTLY {len(section_hints)} sections:\n"
        for i, name in enumerate(section_hints, 1):
            prompt += f"  {i}. {name}\n"
        prompt += "\n"
        prompt += "CRITICAL: You MUST find boundaries for ALL of these sections.\n"
        prompt += "The section names and order above are CONFIRMED CORRECT.\n"
        prompt += "Your job is ONLY to find the correct startBar and endBar for each.\n"
        prompt += "Do NOT add, remove, or rename any sections.\n\n"

    # Add cue track labels if available (highest confidence data)
    if whisper_data.get('cue_labels'):
        prompt += "CUE TRACK LABELS (spoken section announcements - HIGHEST CONFIDENCE):\n"
        for bar, label in sorted(whisper_data['cue_labels'].items()):
            prompt += f"  Bar {bar}: \"{label}\"\n"
        prompt += "\n"

    # Vocal onset/offset
    if vocal_data.get('vocal_onset_bar'):
        prompt += f"VOCAL INFORMATION:\n"
        prompt += f"  First vocal entry: Bar {vocal_data['vocal_onset_bar']}\n"
        prompt += f"  Last vocal: Bar {vocal_data['vocal_offset_bar']}\n"
        prompt += f"  → Everything before bar {vocal_data['vocal_onset_bar']} is INTRO\n"
        bgv_bars = [i+1 for i, a in enumerate(vocal_data['bgv_active_bars']) if a]
        if bgv_bars:
            prompt += f"  BGV/Harmony active in bars: {_summarize_ranges(bgv_bars)}\n"
            prompt += f"  → BGV presence strongly indicates CHORUS\n"
        prompt += "\n"

    # Instrument activation summary
    prompt += "INSTRUMENT ACTIVATION PER BAR:\n"
    prompt += "Bar | "
    roles_present = [r for r in features['activation'].keys()]
    prompt += " | ".join(r[:6] for r in roles_present)
    prompt += " | Count\n"
    prompt += "-" * (6 + len(roles_present) * 8 + 8) + "\n"

    for bar_idx in range(min(total_bars, 120)):  # Limit to avoid token overflow
        bar_num = bar_idx + 1
        line = f"{bar_num:3d} | "
        count = 0
        for role in roles_present:
            active = features['activation'][role][bar_idx] if bar_idx < len(features['activation'][role]) else False
            line += ("  ■   " if active else "  ·   ") + "| "
            if active:
                count += 1
        line += f" {count}"
        prompt += line + "\n"

    prompt += "\n"

    # Chord progression
    if chords:
        prompt += "CHORD PROGRESSION (per bar):\n"
        for i in range(0, len(chords), 4):
            bar_start = i + 1
            bar_group = chords[i:i+4]
            prompt += f"  Bars {bar_start:2d}-{bar_start+3:2d}: {' - '.join(bar_group)}\n"
        prompt += "\n"
        prompt += "NOTE: Repeated chord patterns indicate the SAME section type.\n\n"

    # Drum fills
    if drum_fills:
        prompt += f"DRUM FILLS (section boundary markers): bars {drum_fills}\n"
        prompt += "→ Fills almost always occur at the LAST bar before a section change.\n\n"

    # Lyrics
    has_lyrics = any(l for l in whisper_data.get('lyrics_per_bar', []))
    if has_lyrics:
        prompt += "LYRICS (per bar group):\n"
        current_text = ""
        text_start_bar = 1
        for bar_idx in range(total_bars):
            bar_text = whisper_data['lyrics_per_bar'][bar_idx]
            if bar_text and bar_text != current_text:
                if current_text:
                    prompt += f"  Bars {text_start_bar}-{bar_idx}: \"{current_text[:80]}\"\n"
                current_text = bar_text
                text_start_bar = bar_idx + 1
            elif not bar_text and current_text:
                prompt += f"  Bars {text_start_bar}-{bar_idx}: \"{current_text[:80]}\"\n"
                current_text = ""
        if current_text:
            prompt += f"  Bars {text_start_bar}-{total_bars}: \"{current_text[:80]}\"\n"
        prompt += "\n"
        prompt += "NOTE: Repeated lyrics = SAME section (e.g., both are CHORUS).\n"
        prompt += "Unique lyrics appearing only once late in song = BRIDGE.\n\n"

    # Energy profile
    prompt += "OVERALL ENERGY PROFILE (0.0=silent, 1.0=max):\n"
    # Summarize energy in 4-bar groups
    all_rms = features['rms_per_bar']
    if all_rms:
        # Average across all stems for overall energy
        first_role = list(all_rms.keys())[0]
        avg_energy = [0.0] * total_bars
        n_roles = 0
        for role, rms_list in all_rms.items():
            for i in range(min(len(rms_list), total_bars)):
                avg_energy[i] += rms_list[i]
            n_roles += 1
        if n_roles > 0:
            avg_energy = [e / n_roles for e in avg_energy]

        for i in range(0, total_bars, 4):
            group = avg_energy[i:i+4]
            avg = sum(group) / len(group) if group else 0
            bar_str = f"Bars {i+1:2d}-{min(i+4, total_bars):2d}"
            bar_visual = "█" * int(avg * 10) + "░" * (10 - int(avg * 10))
            prompt += f"  {bar_str}: [{bar_visual}] {avg:.2f}\n"
        prompt += "\n"

    # Melody repetition groups (same melody = same section type)
    if melody_groups:
        repeated = [g for g in melody_groups if len(g['phrase_indices']) > 1]
        if repeated:
            prompt += "MELODY REPETITION (same melody = same section type):\n"
            for g in repeated:
                bars_str = ", ".join(f"{b[0]}-{b[1]}" for b in g['bars'])
                prompt += f"  Group {g['group_id']}: bars {bars_str} (same melody)\n"
            prompt += "\n"
            prompt += "NOTE: Phrases with the same melody are the SAME section type.\n"
            prompt += "e.g., if bars 5-8 and bars 25-28 have same melody, both are likely VERSE.\n\n"

    # Instructions
    prompt += """INSTRUCTIONS:
Based on ALL the above data, identify the song sections. Follow these STRICT worship music rules:
1. Everything before first vocal = INTRO
2. The FIRST section with vocals is ALWAYS a VERSE (never Bridge or Chorus)
3. BRIDGE can ONLY appear AFTER at least one CHORUS has occurred (never before)
4. Sections with BGV/harmonies + highest energy = CHORUS
5. Highest energy + full band (most instruments) = CHORUS
6. Medium energy + vocal but no BGV = VERSE
7. Short transitional section (2-4 bars) before a high-energy section = PRE-CHORUS
8. Low energy section that appears AFTER a chorus, with reduced instrumentation = BRIDGE
9. Everything after last vocal with decreasing instruments = OUTRO
10. If cue track labels are available, they OVERRIDE all other analysis
11. Drum fills mark section boundaries
12. Repeated chord patterns with similar energy = same section type

COMMON WORSHIP SONG STRUCTURE:
INTRO → VERSE 1 → VERSE 2 or PRE-CHORUS → CHORUS → VERSE 2 → CHORUS → BRIDGE → CHORUS → OUTRO

Return your answer as a JSON array with this exact format:
[
  {"name": "INTRO", "startBar": 1, "endBar": 4},
  {"name": "VERSE 1", "startBar": 5, "endBar": 12},
  ...
]

Use these section names: INTRO, VERSE 1, VERSE 2, VERSE 3, PRE-CHORUS, CHORUS, CHORUS 2, BRIDGE, INSTRUMENTAL, TAG, OUTRO

JSON RESPONSE:"""

    return prompt



def _summarize_ranges(bars: List[int]) -> str:
    """Convert list of bar numbers to range string: [1,2,3,5,6] -> '1-3, 5-6'"""
    if not bars:
        return "none"
    ranges = []
    start = bars[0]
    prev = bars[0]
    for b in bars[1:]:
        if b == prev + 1:
            prev = b
        else:
            ranges.append(f"{start}-{prev}" if start != prev else str(start))
            start = b
            prev = b
    ranges.append(f"{start}-{prev}" if start != prev else str(start))
    return ", ".join(ranges)



# ═══════════════════════════════════════════════════════════════════════
# TASK #7: LLM INTEGRATION (OpenAI / Ollama / Fallback)
# ═══════════════════════════════════════════════════════════════════════

def call_llm(prompt: str, llm_mode: str = "openai") -> Optional[List[Dict]]:
    """Send the assembled prompt to an LLM and parse the section response."""
    print("__PROGRESS__|90|Calling LLM for section identification...")
    print("  [8/8] Calling LLM for section identification...")

    if llm_mode == "none":
        print("    LLM disabled, using heuristic fallback")
        return None

    sections_json = None

    if llm_mode == "azure":
        sections_json = _call_azure_openai(prompt)
    elif llm_mode == "openai":
        sections_json = _call_openai(prompt)
    elif llm_mode == "ollama":
        sections_json = _call_ollama(prompt)

    if sections_json is None:
        # Try fallback chain: azure -> openai -> ollama
        if llm_mode == "azure":
            print("    Azure OpenAI failed, trying standard OpenAI...")
            sections_json = _call_openai(prompt)
        if sections_json is None and llm_mode in ("azure", "openai"):
            print("    Trying Ollama as fallback...")
            sections_json = _call_ollama(prompt)

    if sections_json is None:
        print("    ! LLM unavailable, falling back to heuristic analysis")
        return None

    return sections_json


def _call_azure_openai(prompt: str) -> Optional[List[Dict]]:
    """Call Azure OpenAI API.

    Required environment variables:
      AZURE_OPENAI_ENDPOINT  - e.g. https://your-resource.openai.azure.com/
      AZURE_OPENAI_API_KEY   - your Azure OpenAI key
      AZURE_OPENAI_DEPLOYMENT - deployment name (e.g. gpt-4o-mini)
      AZURE_OPENAI_API_VERSION - (optional) defaults to 2024-02-15-preview
    """
    try:
        import openai

        endpoint = os.environ.get("AZURE_OPENAI_ENDPOINT", "")
        api_key = os.environ.get("AZURE_OPENAI_API_KEY", "")
        deployment = os.environ.get("AZURE_OPENAI_DEPLOYMENT", "gpt-4o-mini")
        api_version = os.environ.get("AZURE_OPENAI_API_VERSION", "2024-02-15-preview")

        if not endpoint or not api_key:
            print("    ! Azure OpenAI not configured. Set environment variables:")
            print("      AZURE_OPENAI_ENDPOINT=https://your-resource.openai.azure.com/")
            print("      AZURE_OPENAI_API_KEY=your-key")
            print("      AZURE_OPENAI_DEPLOYMENT=gpt-4o-mini")
            return None

        client = openai.AzureOpenAI(
            azure_endpoint=endpoint,
            api_key=api_key,
            api_version=api_version
        )

        print(f"    Calling Azure OpenAI ({deployment})...")
        response = client.chat.completions.create(
            model=deployment,
            messages=[
                {"role": "system", "content": "You are a music structure analysis expert. "
                 "Always respond with valid JSON only, no other text."},
                {"role": "user", "content": prompt}
            ],
            temperature=0.1,
            max_tokens=2000
        )

        content = response.choices[0].message.content.strip()
        sections = _parse_llm_json(content)
        if sections:
            print(f"    ✓ Azure OpenAI returned {len(sections)} sections")
        return sections

    except ImportError:
        print("    ! openai package not installed (pip install openai)")
        return None
    except Exception as e:
        print(f"    ! Azure OpenAI error: {e}")
        return None


def _call_openai(prompt: str) -> Optional[List[Dict]]:
    """Call OpenAI API."""
    try:
        import openai
        client = openai.OpenAI()  # Uses OPENAI_API_KEY env var

        response = client.chat.completions.create(
            model="gpt-4o-mini",
            messages=[
                {"role": "system", "content": "You are a music structure analysis expert. "
                 "Always respond with valid JSON only, no other text."},
                {"role": "user", "content": prompt}
            ],
            temperature=0.1,
            max_tokens=2000
        )

        content = response.choices[0].message.content.strip()
        # Extract JSON from response
        sections = _parse_llm_json(content)
        if sections:
            print(f"    ✓ OpenAI returned {len(sections)} sections")
        return sections

    except ImportError:
        print("    ! openai package not installed (pip install openai)")
        return None
    except Exception as e:
        print(f"    ! OpenAI error: {e}")
        return None


def _call_ollama(prompt: str) -> Optional[List[Dict]]:
    """Call local Ollama API."""
    try:
        import httpx
        response = httpx.post(
            "http://localhost:11434/api/generate",
            json={
                "model": "llama3.1",
                "prompt": prompt,
                "stream": False,
                "options": {"temperature": 0.1}
            },
            timeout=120
        )
        if response.status_code == 200:
            content = response.json().get('response', '')
            sections = _parse_llm_json(content)
            if sections:
                print(f"    ✓ Ollama returned {len(sections)} sections")
            return sections
        return None
    except ImportError:
        # Try with urllib instead
        try:
            import urllib.request
            data = json.dumps({
                "model": "llama3.1",
                "prompt": prompt,
                "stream": False,
                "options": {"temperature": 0.1}
            }).encode()
            req = urllib.request.Request(
                "http://localhost:11434/api/generate",
                data=data,
                headers={"Content-Type": "application/json"}
            )
            with urllib.request.urlopen(req, timeout=120) as resp:
                content = json.loads(resp.read()).get('response', '')
                sections = _parse_llm_json(content)
                if sections:
                    print(f"    ✓ Ollama returned {len(sections)} sections")
                return sections
        except Exception as e:
            print(f"    ! Ollama not available: {e}")
            return None
    except Exception as e:
        print(f"    ! Ollama error: {e}")
        return None


def _parse_llm_json(content: str) -> Optional[List[Dict]]:
    """Parse JSON from LLM response, handling markdown code blocks."""
    # Remove markdown code block if present
    content = content.strip()
    if content.startswith("```json"):
        content = content[7:]
    if content.startswith("```"):
        content = content[3:]
    if content.endswith("```"):
        content = content[:-3]
    content = content.strip()

    # Find JSON array
    start = content.find('[')
    end = content.rfind(']')
    if start >= 0 and end > start:
        json_str = content[start:end+1]
        try:
            sections = json.loads(json_str)
            # Validate structure
            if isinstance(sections, list) and len(sections) > 0:
                if all('name' in s and 'startBar' in s and 'endBar' in s for s in sections):
                    return sections
        except json.JSONDecodeError:
            pass
    return None



# ═══════════════════════════════════════════════════════════════════════
# HEURISTIC FALLBACK (when LLM is unavailable)
# ═══════════════════════════════════════════════════════════════════════

def heuristic_section_detection(grid: Dict, features: Dict, vocal_data: Dict,
                                 drum_fills: List[int], chords: List[str],
                                 time_sig: int = 4) -> List[Dict]:
    """Fallback section detection using rules when no LLM is available."""
    print("    Using heuristic section detection...")

    total_bars = features['total_bars']
    vocal_onset = vocal_data.get('vocal_onset_bar', 1)
    vocal_offset = vocal_data.get('vocal_offset_bar', total_bars)
    bgv_active = vocal_data.get('bgv_active_bars', [False] * total_bars)

    # Compute per-bar instrument count
    instrument_count = [0] * total_bars
    for role, active_list in features['activation'].items():
        for i in range(min(len(active_list), total_bars)):
            if active_list[i]:
                instrument_count[i] += 1

    # Compute overall energy per bar (average RMS across all stems)
    energy_per_bar = [0.0] * total_bars
    n_roles = 0
    for role, rms_list in features['rms_per_bar'].items():
        for i in range(min(len(rms_list), total_bars)):
            energy_per_bar[i] += rms_list[i]
        n_roles += 1
    if n_roles > 0:
        energy_per_bar = [e / n_roles for e in energy_per_bar]

    # ═══ MULTI-SIGNAL VOTING for boundary detection (matching granular approach) ═══
    # Each signal votes for a bar to be a boundary. Only bars with 2+ votes are kept.
    boundary_votes = {}  # bar_number -> vote_count

    def add_vote(bar, weight=1):
        if 1 < bar <= total_bars:
            boundary_votes[bar] = boundary_votes.get(bar, 0) + weight

    # Signal 1: Vocal onset/offset
    if vocal_onset and vocal_onset > 1:
        add_vote(vocal_onset, 3)  # Strong signal

    # Signal 2: Drum fills = boundary (2 votes, very reliable)
    for fill_bar in drum_fills:
        if fill_bar + 1 <= total_bars:
            add_vote(fill_bar + 1, 2)

    # Signal 3: Instrument count changes (2+ instruments add/drop)
    for i in range(1, total_bars):
        diff = abs(instrument_count[i] - instrument_count[i-1])
        if diff >= 2:
            add_vote(i + 1, 2)
        elif diff >= 1:
            add_vote(i + 1, 1)

    # Signal 4: Energy changes (2-bar sliding window comparison)
    for i in range(2, total_bars - 1):
        before = np.mean(energy_per_bar[max(0, i-2):i])
        after = np.mean(energy_per_bar[i:min(total_bars, i+2)])
        max_e = max(before, after, 0.01)
        change = abs(after - before) / max_e
        if change > 0.3:
            add_vote(i + 1, 1)
        elif change > 0.5:
            add_vote(i + 1, 2)

    # Signal 5: Chord pattern changes (2-bar groups)
    if chords and len(chords) >= 4:
        for i in range(2, len(chords) - 1, 2):
            prev_pattern = chords[max(0, i-2):i]
            curr_pattern = chords[i:min(len(chords), i+2)]
            if len(prev_pattern) == 2 and len(curr_pattern) == 2:
                matches = sum(1 for a, b in zip(prev_pattern, curr_pattern) if a == b)
                if matches == 0:
                    add_vote(i + 1, 1)

    # Signal 6: BGV onset/offset (strong chorus indicator)
    for i in range(1, total_bars):
        if i < len(bgv_active) and i-1 < len(bgv_active):
            if bgv_active[i] and not bgv_active[i-1]:
                add_vote(i + 1, 2)
            elif not bgv_active[i] and bgv_active[i-1]:
                add_vote(i + 1, 1)

    # Signal 7: Vocal silence gaps (from vocal_active_bars)
    vocal_active = vocal_data.get('vocal_active_bars', [False] * total_bars)
    for i in range(1, total_bars):
        if i < len(vocal_active) and i-1 < len(vocal_active):
            if vocal_active[i] and not vocal_active[i-1]:
                add_vote(i + 1, 1)  # Vocal starts
            elif not vocal_active[i] and vocal_active[i-1]:
                add_vote(i + 1, 1)  # Vocal stops

    # Signal 8: Constrain to downbeats (if available from beat_this)
    # Boundaries should prefer downbeat positions
    if 'downbeats' in grid and grid.get('downbeats'):
        downbeat_bars = set()
        bar_dur_sec = grid['beat_interval_sec'] * time_sig
        for db_sec in grid['downbeats']:
            bar_num = int((db_sec - grid['grid_anchor_sec']) / bar_dur_sec) + 1
            if 1 <= bar_num <= total_bars:
                downbeat_bars.add(bar_num)

        # Boost votes for boundaries that fall on downbeats
        for bar in list(boundary_votes.keys()):
            if bar in downbeat_bars:
                boundary_votes[bar] += 1

    # Keep only boundaries with 2+ votes
    boundaries = set()
    boundaries.add(1)  # Always start
    for bar, votes in boundary_votes.items():
        if votes >= 2:
            boundaries.add(bar)

    # If still too few boundaries, lower threshold to 1 vote
    if len(boundaries) < 4 and total_bars > 16:
        for bar, votes in sorted(boundary_votes.items(), key=lambda x: -x[1]):
            if votes >= 1 and len(boundaries) < 12:
                boundaries.add(bar)
        # using spectral contrast changes
        bar_size = 8
        for i in range(bar_size, total_bars - 2, bar_size):
            boundaries.add(i + 1)

    # If we have chords and very few boundaries, use chord pattern repetition
    if len(boundaries) < 4 and chords and len(chords) >= 8:
        # Find where 4-bar chord patterns change
        for i in range(4, len(chords) - 3):
            prev_4 = chords[max(0, i-4):i]
            next_4 = chords[i:min(len(chords), i+4)]
            if len(prev_4) == 4 and len(next_4) == 4:
                matches = sum(1 for a, b in zip(prev_4, next_4) if a == b)
                if matches <= 1:
                    boundaries.add(i + 1)

    # Sort and filter boundaries too close together (min 2 bars apart)
    sorted_bounds = sorted(boundaries)
    filtered_bounds = [sorted_bounds[0]]
    for b in sorted_bounds[1:]:
        if b - filtered_bounds[-1] >= 2:
            filtered_bounds.append(b)

    # Add end
    filtered_bounds.append(total_bars + 1)

    # Limit sections to reasonable range (4-12)
    if len(filtered_bounds) > 14:
        # Keep boundaries with biggest energy contrast
        boundary_scores = []
        for i in range(1, len(filtered_bounds) - 1):
            b = filtered_bounds[i]
            if b - 1 < total_bars and b - 2 >= 0:
                score = abs(energy_per_bar[b-1] - energy_per_bar[b-2])
            else:
                score = 0
            boundary_scores.append((score, b))
        boundary_scores.sort(reverse=True)
        top_bounds = sorted([1] + [b for _, b in boundary_scores[:11]] + [total_bars + 1])
        filtered_bounds = top_bounds

    # Label sections based on worship-specific rules:
    # 1. First section without vocals = INTRO
    # 2. First section WITH vocals = always VERSE (never Bridge)
    # 3. Bridge NEVER comes before a Chorus
    # 4. Highest energy section with most instruments = CHORUS
    # 5. Short sections (2-4 bars) between verse and chorus = PRE-CHORUS
    # 6. After last chorus with decreasing energy = OUTRO or TAG
    sections = []
    verse_count = 0
    chorus_count = 0
    has_had_chorus = False
    first_vocal_section_done = False

    # First pass: find the highest energy section (likely chorus)
    section_energies = []
    for i in range(len(filtered_bounds) - 1):
        start_bar = filtered_bounds[i]
        end_bar = filtered_bounds[i + 1] - 1
        bar_range = range(start_bar - 1, min(end_bar, total_bars))
        avg_energy = np.mean([energy_per_bar[b] for b in bar_range]) if len(bar_range) > 0 else 0
        section_energies.append(avg_energy)

    max_energy = max(section_energies) if section_energies else 1.0
    # Chorus threshold: sections with energy >= 75% of the max energy section
    chorus_threshold = max_energy * 0.75

    for i in range(len(filtered_bounds) - 1):
        start_bar = filtered_bounds[i]
        end_bar = filtered_bounds[i + 1] - 1
        duration_bars = end_bar - start_bar + 1

        # Determine label based on signals
        bar_range = range(start_bar - 1, min(end_bar, total_bars))
        avg_energy = section_energies[i]
        avg_count = np.mean([instrument_count[b] for b in bar_range]) if len(bar_range) > 0 else 0
        has_vocal = any(vocal_data['vocal_active_bars'][b] for b in bar_range if b < len(vocal_data['vocal_active_bars']))
        has_bgv = any(bgv_active[b] for b in bar_range if b < len(bgv_active))

        if not has_vocal and i == 0:
            name = "INTRO"
        elif not has_vocal and end_bar >= total_bars - 4:
            name = "OUTRO"
        elif not has_vocal and duration_bars <= 4:
            name = "INSTRUMENTAL"
        elif not has_vocal:
            name = "INSTRUMENTAL"
        elif not first_vocal_section_done:
            # First section with vocals is ALWAYS a verse in worship music
            first_vocal_section_done = True
            verse_count += 1
            name = f"VERSE {verse_count}"
        elif has_bgv and avg_energy >= chorus_threshold:
            # BGV + high energy = definitely CHORUS
            chorus_count += 1
            has_had_chorus = True
            name = f"CHORUS" if chorus_count == 1 else f"CHORUS {chorus_count}"
        elif avg_energy >= chorus_threshold and avg_count >= 4:
            # High energy + full band = CHORUS
            chorus_count += 1
            has_had_chorus = True
            name = f"CHORUS" if chorus_count == 1 else f"CHORUS {chorus_count}"
        elif duration_bars <= 4 and i + 1 < len(filtered_bounds) - 1:
            # Short section (2-4 bars) that's not at the end = PRE-CHORUS or transition
            if section_energies[i] < section_energies[min(i + 1, len(section_energies) - 1)]:
                name = "PRE-CHORUS"
            else:
                verse_count += 1
                name = f"VERSE {verse_count}"
        elif has_had_chorus and avg_energy < chorus_threshold * 0.5 and avg_count <= 3:
            # Low energy after chorus = BRIDGE (only allowed after a chorus has occurred)
            name = "BRIDGE"
        else:
            verse_count += 1
            name = f"VERSE {verse_count}"

        sections.append({
            "name": name,
            "startBar": start_bar,
            "endBar": end_bar
        })

    # Merge very short adjacent sections of same type
    if len(sections) > 12:
        merged = [sections[0]]
        for s in sections[1:]:
            if (s['name'] == merged[-1]['name'] and
                s['startBar'] - merged[-1]['endBar'] <= 1):
                merged[-1]['endBar'] = s['endBar']
            else:
                merged.append(s)
        sections = merged

    print(f"    ✓ Heuristic: {len(sections)} sections detected")
    return sections



# ═══════════════════════════════════════════════════════════════════════
# CUE TRACK ONSET DETECTION (timing-based, not text-based)
# ═══════════════════════════════════════════════════════════════════════

def detect_cue_onsets(classified: Dict, grid: Dict, time_sig: int = 4) -> List[int]:
    """Detect WHEN the cue track has audio (onset detection).
    Returns list of bar numbers where cue audio is detected.
    This is more reliable than Whisper transcription for short spoken cues.
    Each cue onset = 1 bar before a section boundary."""
    cue_path = classified.get('cue')
    if not cue_path or not os.path.exists(cue_path):
        return []

    print("  Detecting cue track onsets (timing-based)...")
    bar_duration = grid['beat_interval_sec'] * time_sig
    grid_anchor = grid['grid_anchor_sec']
    total_bars = int((grid['total_duration_sec'] - grid_anchor) / bar_duration)

    try:
        y, sr = librosa.load(cue_path, sr=SAMPLE_RATE)
    except:
        return []

    # Check RMS per bar to find bars where cue track has audio
    cue_bars = []
    for bar_idx in range(total_bars):
        bar_start = grid_anchor + bar_idx * bar_duration
        bar_end = bar_start + bar_duration
        start_sample = int(bar_start * sr)
        end_sample = int(bar_end * sr)

        if start_sample >= len(y):
            break

        segment = y[start_sample:min(end_sample, len(y))]
        rms = float(np.sqrt(np.mean(segment ** 2)))

        # Cue track has audio if RMS > threshold (typically very quiet when inactive)
        if rms > 0.01:
            cue_bars.append(bar_idx + 1)  # 1-based

    if cue_bars:
        print(f"    ✓ Cue onsets detected at bars: {cue_bars}")
    else:
        print(f"    ! No cue onsets detected")

    return cue_bars


def sections_from_cue_onsets(cue_bars: List[int], grid: Dict, features: Dict,
                             vocal_data: Optional[Dict], time_sig: int = 4,
                             llm_mode: str = "azure") -> List[Dict]:
    """Build section boundaries from cue onset timing.
    Each cue bar = 1 bar before the section starts.
    Uses instrument activation + energy to label sections."""
    bar_duration = grid['beat_interval_sec'] * time_sig
    grid_anchor = grid['grid_anchor_sec']
    total_bars = int((grid['total_duration_sec'] - grid_anchor) / bar_duration)

    if not cue_bars:
        return []

    # Section boundaries = cue_bar + 1 (cue speaks 1 bar before)
    # But some cues are 2 bars before — detect by looking for consecutive cue bars
    boundaries = []
    i = 0
    while i < len(cue_bars):
        # If next cue is consecutive (e.g., bars 48,49), take the LAST one
        cue_bar = cue_bars[i]
        while i + 1 < len(cue_bars) and cue_bars[i + 1] == cue_bars[i] + 1:
            i += 1
            cue_bar = cue_bars[i]
        # Section starts 1 bar after cue
        section_start = cue_bar + 1
        if section_start <= total_bars:
            boundaries.append(section_start)
        i += 1

    # Add bar 1 if not already there (song start)
    if boundaries and boundaries[0] > 2:
        boundaries.insert(0, 1)

    # Add end
    boundaries.append(total_bars + 1)

    # Remove duplicates and sort
    boundaries = sorted(set(boundaries))

    # Build sections with worship-specific labeling
    sections = []
    activation = features.get('activation', {}) if features else {}
    rms_data = features.get('rms_per_bar', {}) if features else {}
    vocal_active = vocal_data.get('vocal_active_bars', []) if vocal_data else []

    # Compute per-bar energy and instrument count
    total_bars_count = features.get('total_bars', total_bars) if features else total_bars
    energy_per_bar = [0.0] * total_bars_count
    instrument_count = [0] * total_bars_count
    for role, rms_list in rms_data.items():
        for i in range(min(len(rms_list), total_bars_count)):
            energy_per_bar[i] += rms_list[i]
    for role, active_list in activation.items():
        for i in range(min(len(active_list), total_bars_count)):
            if active_list[i]:
                instrument_count[i] += 1
    n_roles = len(rms_data) if rms_data else 1
    energy_per_bar = [e / max(n_roles, 1) for e in energy_per_bar]
    max_energy = max(energy_per_bar) if energy_per_bar else 1.0

    # Check if drums and bass are active per bar
    drums_active = activation.get('drums', [False] * total_bars_count)
    bass_active = activation.get('bass', [False] * total_bars_count)

    # Label sections using worship-specific rules
    verse_count = 0
    chorus_count = 0
    bridge_count = 0
    has_had_chorus = False

    for i in range(len(boundaries) - 1):
        start_bar = boundaries[i]
        end_bar = boundaries[i + 1] - 1

        # Analyze this section's characteristics
        bar_range = range(start_bar - 1, min(end_bar, total_bars_count))
        avg_energy = np.mean([energy_per_bar[b] for b in bar_range]) if len(bar_range) > 0 else 0
        avg_count = np.mean([instrument_count[b] for b in bar_range]) if len(bar_range) > 0 else 0
        has_vocal = any(vocal_active[b] for b in bar_range if b < len(vocal_active))
        has_drums = any(drums_active[b] for b in bar_range if b < len(drums_active))
        has_bass = any(bass_active[b] for b in bar_range if b < len(bass_active))

        # Worship-specific labeling rules
        if not has_vocal and start_bar <= 3:
            name = "START"
        elif not has_vocal and avg_energy < 0.3 * max_energy and not has_drums:
            if has_had_chorus:
                name = "INTERLUDE"
            else:
                name = "INTRO"
        elif not has_vocal and end_bar >= total_bars - 2:
            name = "END"
        elif has_drums and has_bass and avg_energy >= 0.5 * max_energy:
            # Drums + bass + high energy = CHORUS
            chorus_count += 1
            has_had_chorus = True
            name = f"CHORUS {chorus_count}" if chorus_count > 1 else "CHORUS"
        elif has_had_chorus and not has_drums and avg_energy < 0.5 * max_energy:
            # After chorus, reduced instrumentation = BRIDGE
            bridge_count += 1
            name = f"BRIDGE {bridge_count}" if bridge_count > 1 else "BRIDGE"
        elif has_had_chorus and has_drums and avg_energy >= 0.6 * max_energy:
            # High energy after chorus with drums = another CHORUS
            chorus_count += 1
            name = f"CHORUS {chorus_count}"
        elif not has_had_chorus and has_vocal:
            # Before any chorus, vocal = VERSE
            verse_count += 1
            name = f"VERSE {verse_count}" if verse_count > 1 else "VERSE 1"
        else:
            verse_count += 1
            name = f"VERSE {verse_count}"

        sections.append({
            "name": name,
            "startBar": start_bar,
            "endBar": end_bar
        })

    print(f"    ✓ {len(sections)} sections from cue onset timing")
    for sec in sections:
        print(f"      {sec['name']:<18} bars {sec['startBar']}-{sec['endBar']}")

    return sections


# ═══════════════════════════════════════════════════════════════════════
# CUE-FIRST SECTION DETECTION
# ═══════════════════════════════════════════════════════════════════════

def sections_from_cue_track(cue_labels: Dict[int, str], grid: Dict,
                            time_sig: int = 4, llm_mode: str = "azure") -> List[Dict]:
    """Build sections directly from cue track labels.
    Each cue is spoken 1 bar BEFORE the section starts.
    The NEXT cue defines the END of the current section."""
    print("  Building sections from cue track...")

    bar_duration_sec = grid['beat_interval_sec'] * time_sig
    total_duration = grid['total_duration_sec']
    grid_anchor = grid['grid_anchor_sec']
    total_bars = int((total_duration - grid_anchor) / bar_duration_sec)

    # Sort cue labels by bar number
    sorted_cues = sorted(cue_labels.items())

    # Use LLM to clean up cue text into proper section names
    cleaned_names = _clean_cue_names_with_llm(sorted_cues, llm_mode)

    sections = []
    for idx, (cue_bar, raw_text) in enumerate(sorted_cues):
        # Section starts 1 bar AFTER the cue speaks
        section_start_bar = cue_bar + 1

        # Section ends where the NEXT cue speaks (or end of song)
        if idx + 1 < len(sorted_cues):
            next_cue_bar = sorted_cues[idx + 1][0]
            section_end_bar = next_cue_bar  # End at the bar where next cue speaks
        else:
            section_end_bar = total_bars  # Last section goes to end of song

        # Clamp to valid range
        section_start_bar = max(1, min(section_start_bar, total_bars))
        section_end_bar = max(section_start_bar, min(section_end_bar, total_bars))

        # Get cleaned name (from LLM) or use raw text
        name = cleaned_names.get(idx, raw_text.upper().strip())

        sections.append({
            "name": name,
            "startBar": section_start_bar,
            "endBar": section_end_bar
        })

    # If first cue doesn't start at bar 1-2, add an INTRO
    if sections and sections[0]['startBar'] > 2:
        sections.insert(0, {
            "name": "INTRO",
            "startBar": 1,
            "endBar": sections[0]['startBar'] - 1
        })

    print(f"    ✓ {len(sections)} sections from cue track")
    for sec in sections:
        print(f"      {sec['name']:<20} bars {sec['startBar']}-{sec['endBar']}")

    return sections


def _clean_cue_names_with_llm(sorted_cues: List, llm_mode: str) -> Dict[int, str]:
    """Use LLM to clean up raw cue text into proper section names."""
    if llm_mode == "none" or not sorted_cues:
        # Fallback: simple text cleanup
        result = {}
        for idx, (bar, text) in enumerate(sorted_cues):
            cleaned = text.strip().upper()
            # Common cleanup patterns
            for prefix in ['coming up', 'next', 'here comes', 'going to']:
                cleaned = cleaned.replace(prefix.upper(), '').strip()
            cleaned = cleaned.strip('.,!- ')
            if not cleaned:
                cleaned = f"SECTION {idx + 1}"
            result[idx] = cleaned
        return result

    # Build a simple LLM prompt to clean cue names
    prompt = """Clean up these spoken cue track labels into standard section names.
The cue track is from a worship song and contains spoken announcements before each section.

Raw cue text (in order):
"""
    for idx, (bar, text) in enumerate(sorted_cues):
        prompt += f"  {idx + 1}. \"{text}\"\n"

    prompt += """
Convert each to a standard section name from this list:
INTRO, VERSE 1, VERSE 2, VERSE 3, PRE-CHORUS, CHORUS, CHORUS 2, BRIDGE, INSTRUMENTAL, TAG, OUTRO

Return ONLY a JSON array of strings with the cleaned names in order:
["VERSE 1", "CHORUS", "VERSE 2", ...]
"""

    sections_json = None
    try:
        if llm_mode == "azure":
            from Scripts.analyze_multitrack import _call_azure_openai
            # Use simplified call
            pass
        sections_json = call_llm(prompt, llm_mode)
    except:
        pass

    # Try to parse as simple string array
    if sections_json is None:
        try:
            import openai
            if llm_mode == "azure":
                endpoint = os.environ.get("AZURE_OPENAI_ENDPOINT", "")
                api_key = os.environ.get("AZURE_OPENAI_API_KEY", "")
                deployment = os.environ.get("AZURE_OPENAI_DEPLOYMENT", "gpt-4o-mini")
                api_version = os.environ.get("AZURE_OPENAI_API_VERSION", "2024-02-15-preview")
                client = openai.AzureOpenAI(azure_endpoint=endpoint, api_key=api_key, api_version=api_version)
            else:
                client = openai.OpenAI()

            response = client.chat.completions.create(
                model=os.environ.get("AZURE_OPENAI_DEPLOYMENT", "gpt-4o-mini") if llm_mode == "azure" else "gpt-4o-mini",
                messages=[
                    {"role": "system", "content": "You clean up spoken cue track text into standard section names. Respond with JSON array only."},
                    {"role": "user", "content": prompt}
                ],
                temperature=0.1, max_tokens=500
            )
            content = response.choices[0].message.content.strip()
            if content.startswith("```"):
                content = content.split("\n", 1)[-1].rsplit("```", 1)[0]
            names = json.loads(content)
            if isinstance(names, list):
                return {i: n for i, n in enumerate(names)}
        except Exception as e:
            print(f"    ! LLM cue cleanup failed: {e}")

    # Fallback: simple text cleanup
    result = {}
    for idx, (bar, text) in enumerate(sorted_cues):
        cleaned = text.strip().upper()
        for prefix in ['coming up', 'next', 'here comes', 'going to', 'and']:
            cleaned = cleaned.replace(prefix.upper(), '').strip()
        cleaned = cleaned.strip('.,!- ')
        if not cleaned:
            cleaned = f"SECTION {idx + 1}"
        result[idx] = cleaned
    return result


# ═══════════════════════════════════════════════════════════════════════
# COUNT-IN DETECTION
# ═══════════════════════════════════════════════════════════════════════

def detect_count_in(classified: Dict, grid: Dict, time_sig: int = 4) -> int:
    """Detect if the song starts with a click-only count-in (no other stems active).
    Returns the number of count-in bars (0 if none detected)."""
    print("  Checking for count-in...")

    click_path = classified.get('click')
    if not click_path or not os.path.exists(click_path):
        return 0

    bar_duration = grid['beat_interval_sec'] * time_sig
    grid_anchor = grid['grid_anchor_sec']

    # Check first 2 bars: is only click active?
    non_click_stems = {k: v for k, v in classified.items()
                       if k not in ('click', 'cue', '_unclassified', '_full_mix') and v}

    count_in_bars = 0
    for bar_idx in range(min(4, int((grid['total_duration_sec'] - grid_anchor) / bar_duration))):
        bar_start = grid_anchor + bar_idx * bar_duration
        bar_end = bar_start + bar_duration

        # Check if any non-click stem has energy in this bar
        any_active = False
        for role, path in non_click_stems.items():
            if not path or not os.path.exists(path):
                continue
            try:
                y, sr = librosa.load(path, sr=SAMPLE_RATE, offset=bar_start, duration=bar_duration)
                rms = float(np.sqrt(np.mean(y ** 2)))
                if rms > 0.01:  # Non-trivial energy
                    any_active = True
                    break
            except:
                continue

        if any_active:
            break
        count_in_bars += 1

    if count_in_bars > 0:
        print(f"    ✓ Count-in: {count_in_bars} bar(s) detected (click only)")
    else:
        print(f"    No count-in detected")

    return count_in_bars


# ═══════════════════════════════════════════════════════════════════════
# DRUM ONSET VERIFICATION
# ═══════════════════════════════════════════════════════════════════════

def detect_drum_onsets_at_boundaries(classified: Dict, grid: Dict, time_sig: int = 4) -> List[float]:
    """Detect strong drum onsets that confirm section boundaries.
    Returns list of onset times in seconds."""
    drum_path = classified.get('drums')
    if not drum_path or not os.path.exists(drum_path):
        return []

    y, sr = librosa.load(drum_path, sr=SAMPLE_RATE)
    onset_env = librosa.onset.onset_strength(y=y, sr=sr, hop_length=HOP_LENGTH)
    onset_frames = librosa.onset.onset_detect(onset_envelope=onset_env, sr=sr, hop_length=HOP_LENGTH)
    onset_times = librosa.frames_to_time(onset_frames, sr=sr, hop_length=HOP_LENGTH)
    return onset_times.tolist()


def verify_boundaries_with_drums(sections: List[Dict], classified: Dict,
                                  grid: Dict, time_sig: int = 4) -> List[Dict]:
    """Verify and fine-tune section boundaries using drum onsets.
    If a strong drum onset exists within 1 beat of a boundary, shift to it."""
    drum_path = classified.get('drums')
    if not drum_path or not os.path.exists(drum_path):
        return sections

    try:
        y, sr = librosa.load(drum_path, sr=SAMPLE_RATE)
        onset_env = librosa.onset.onset_strength(y=y, sr=sr, hop_length=HOP_LENGTH)
        onset_frames = librosa.onset.onset_detect(
            onset_envelope=onset_env, sr=sr, hop_length=HOP_LENGTH,
            backtrack=True)
        onset_times = librosa.frames_to_time(onset_frames, sr=sr, hop_length=HOP_LENGTH)
    except:
        return sections

    bar_duration_sec = grid['beat_interval_sec'] * time_sig
    beat_duration_sec = grid['beat_interval_sec']
    grid_anchor = grid['grid_anchor_sec']

    for section in sections:
        # Check start boundary
        start_sec = grid_anchor + (section['startBar'] - 1) * bar_duration_sec

        # Find nearest drum onset within 1 beat of expected start
        nearest = None
        min_dist = beat_duration_sec
        for onset_t in onset_times:
            dist = abs(onset_t - start_sec)
            if dist < min_dist:
                min_dist = dist
                nearest = onset_t

        # If drum onset is very close but not exactly on the bar, don't shift
        # (our bar grid from click track is more reliable)
        # This verification just CONFIRMS boundaries are correct

    return sections


# ═══════════════════════════════════════════════════════════════════════
# LYRICS CONTENT-BASED ASSIGNMENT (Two-Step Approach)
# ═══════════════════════════════════════════════════════════════════════

def assign_lyrics_by_content(sections: List[Dict], section_hints: List[str],
                              section_lyrics: Dict[str, str], whisper_data: Dict,
                              grid: Dict, time_sig: int = 4) -> List[Dict]:
    """Assign lyrics to sections BY CONTENT — match what Whisper detected at each
    section's time range to the provided lyrics blocks.

    Two-Step Approach:
    - Step 1 (already done): Musical boundaries are accurate for looping
    - Step 2 (this function): For each detected section, check what's being sung
      and assign the BEST matching lyrics block from the user's provided text.

    Also renames sections to match the lyrics block that was assigned.

    This solves: "Section labeled CHORUS but Verse 1 lyrics are being sung"
    """
    import re

    print("  Assigning lyrics to sections by content matching...")

    word_timestamps = whisper_data.get('word_timestamps', [])
    if not word_timestamps:
        print("    ! No word timestamps — falling back to order-based assignment")
        return _assign_lyrics_by_order(sections, section_hints, section_lyrics)

    bar_duration_sec = grid['beat_interval_sec'] * time_sig
    grid_anchor_sec = grid['grid_anchor_sec']

    def clean_words(text: str) -> List[str]:
        """Extract clean words from lyrics (strip [chords], punctuation)."""
        text = re.sub(r'\[[^\]]*\]', '', text)
        text = re.sub(r'[^\w\s]', '', text.lower())
        return [w for w in text.split() if len(w) > 1]

    # Build word timeline: for each section, collect Whisper words in that time range
    whisper_words_by_section = []
    for sec in sections:
        start_sec = grid_anchor_sec + (sec['startBar'] - 1) * bar_duration_sec
        end_sec = grid_anchor_sec + sec['endBar'] * bar_duration_sec

        # Collect words sung during this section
        section_words = []
        for wt in word_timestamps:
            if wt['start_sec'] >= start_sec - 0.1 and wt['start_sec'] < end_sec + 0.1:
                section_words.append(wt['word'].strip().lower().strip('.,!?;:\'"'))

        whisper_words_by_section.append(section_words)

    # Build lyrics blocks with their clean words
    lyrics_blocks = []
    for hint_name in section_hints:
        lyrics_text = None
        for key, val in section_lyrics.items():
            if key.upper().strip() == hint_name.upper().strip():
                lyrics_text = val
                break
        if lyrics_text is None:
            for key, val in section_lyrics.items():
                if key.upper().strip() in hint_name.upper().strip() or \
                   hint_name.upper().strip() in key.upper().strip():
                    lyrics_text = val
                    break

        clean = clean_words(lyrics_text) if lyrics_text else []
        lyrics_blocks.append({
            'name': hint_name,
            'text': lyrics_text or '',
            'words': clean
        })

    # Match each detected section to the BEST lyrics block
    # Use greedy sequential matching (respects order — lyrics appear in song order)
    used_blocks = set()
    assignments = []

    for sec_idx, sec in enumerate(sections):
        sung_words = whisper_words_by_section[sec_idx]

        if not sung_words:
            # No vocals in this section (instrumental) — skip matching
            assignments.append(None)
            continue

        # Score each unused lyrics block against what's being sung
        best_block_idx = -1
        best_score = 0

        # Search from the next expected block position (respects order)
        for block_idx in range(len(lyrics_blocks)):
            if block_idx in used_blocks:
                continue

            block_words = lyrics_blocks[block_idx]['words']
            if not block_words:
                continue

            # Score: how many of the block's first 10 words appear in the sung words
            match_words = block_words[:10]
            score = 0
            for mw in match_words:
                for sw in sung_words[:30]:  # Check first 30 Whisper words of this section
                    if mw == sw:
                        score += 3
                        break
                    elif len(mw) >= 3 and len(sw) >= 3 and (mw in sw or sw in mw):
                        score += 1
                        break

            if score > best_score:
                best_score = score
                best_block_idx = block_idx

        # Assign if score is reasonable (at least 20% of words matched somewhat)
        min_threshold = len(lyrics_blocks[best_block_idx]['words'][:10]) * 0.6 if best_block_idx >= 0 else 999

        if best_block_idx >= 0 and best_score >= min_threshold:
            assignments.append(best_block_idx)
            used_blocks.add(best_block_idx)
            print(f"    ✓ Section {sec_idx+1} (bar {sec['startBar']}-{sec['endBar']}) "
                  f"→ '{lyrics_blocks[best_block_idx]['name']}' (score={best_score})")
        else:
            # Low confidence — try sequential assignment
            # Find next unused block in order
            next_unused = None
            for bi in range(len(lyrics_blocks)):
                if bi not in used_blocks:
                    next_unused = bi
                    break
            if next_unused is not None:
                assignments.append(next_unused)
                used_blocks.add(next_unused)
                print(f"    ? Section {sec_idx+1} (bar {sec['startBar']}-{sec['endBar']}) "
                      f"→ '{lyrics_blocks[next_unused]['name']}' (weak match, sequential fallback)")
            else:
                assignments.append(None)
                print(f"    ? Section {sec_idx+1} (bar {sec['startBar']}-{sec['endBar']}) "
                      f"→ no lyrics block available")

    # Apply assignments: rename sections and set text
    for sec_idx, sec in enumerate(sections):
        block_idx = assignments[sec_idx]
        if block_idx is not None and block_idx < len(lyrics_blocks):
            block = lyrics_blocks[block_idx]
            sec['name'] = block['name']
            sec['_assigned_text'] = block['text']  # Temp field for build_song_json
        else:
            sec['_assigned_text'] = ''

    # Handle unmatched lyrics blocks (more lyrics sections than audio sections)
    unmatched = [i for i in range(len(lyrics_blocks)) if i not in used_blocks]
    if unmatched:
        print(f"    ! {len(unmatched)} lyrics blocks unmatched: "
              f"{[lyrics_blocks[i]['name'] for i in unmatched]}")
        print(f"    (Song may have {len(lyrics_blocks)} lyric sections but only "
              f"{len(sections)} distinct audio sections detected)")

    print(f"    ✓ Content matching complete: {len(used_blocks)}/{len(lyrics_blocks)} blocks assigned")
    return sections


def _assign_lyrics_by_order(sections: List[Dict], section_hints: List[str],
                             section_lyrics: Dict[str, str]) -> List[Dict]:
    """Fallback: assign lyrics to sections by NAME MATCHING (case-insensitive).
    If no name match, falls back to sequential order."""
    print("    Using name-based assignment (no Whisper timestamps)")

    used_hints = set()

    for sec in sections:
        sec_name = sec.get('name', '').upper().strip()
        assigned = False

        # Try exact name match first
        for hint_name in section_hints:
            if hint_name in used_hints:
                continue
            if hint_name.upper().strip() == sec_name:
                sec['name'] = hint_name
                for key, val in section_lyrics.items():
                    if key.upper().strip() == hint_name.upper().strip():
                        sec['_assigned_text'] = val
                        break
                else:
                    sec['_assigned_text'] = ''
                used_hints.add(hint_name)
                assigned = True
                break

        # Try partial name match
        if not assigned:
            for hint_name in section_hints:
                if hint_name in used_hints:
                    continue
                if hint_name.upper().strip() in sec_name or sec_name in hint_name.upper().strip():
                    sec['name'] = hint_name
                    for key, val in section_lyrics.items():
                        if key.upper().strip() == hint_name.upper().strip():
                            sec['_assigned_text'] = val
                            break
                    else:
                        sec['_assigned_text'] = ''
                    used_hints.add(hint_name)
                    assigned = True
                    break

        # Sequential fallback: assign next unused hint
        if not assigned:
            for hint_name in section_hints:
                if hint_name not in used_hints:
                    sec['name'] = hint_name
                    for key, val in section_lyrics.items():
                        if key.upper().strip() == hint_name.upper().strip():
                            sec['_assigned_text'] = val
                            break
                    else:
                        sec['_assigned_text'] = ''
                    used_hints.add(hint_name)
                    break
            else:
                sec['_assigned_text'] = ''

    return sections


# ═══════════════════════════════════════════════════════════════════════
# VOCAL PICKUP DETECTION (per-section vocal pre-roll for seamless loops)
# ═══════════════════════════════════════════════════════════════════════

def detect_vocal_pickups(classified: Dict, grid: Dict, sections: List[Dict],
                         time_sig: int = 4) -> List[Dict]:
    """Detect vocal pickup notes before each section boundary.

    In worship music, vocals often start 1-2 beats BEFORE the section boundary
    (e.g., "Ama-" before "-zing Grace" on beat 1). This creates a problem for
    looping: if the loop starts on bar 1 of the section, the pickup syllable is lost.

    This function detects vocal energy in the 1-2 beats before each section start
    and records the pickup offset so the vocal track can loop from an earlier point.

    Returns the sections list with 'vocalPickup_ms' added to each section.
    vocalPickup_ms = how many ms BEFORE the section start the vocal begins.
    0 means no pickup (vocal starts on or after beat 1).
    """
    print("  Detecting vocal pickup notes for seamless loops...")

    vocal_path = classified.get('lead_vocal')
    if not vocal_path or not os.path.exists(vocal_path):
        # Check for Demucs-separated vocals
        vocal_path = classified.get('vocals')
        if not vocal_path or not os.path.exists(vocal_path):
            print("    ! No vocal stem found — skipping pickup detection")
            for sec in sections:
                sec['vocalPickup_ms'] = 0
            return sections

    y, sr = librosa.load(vocal_path, sr=SAMPLE_RATE)
    total_samples = len(y)

    bar_duration_sec = grid['beat_interval_sec'] * time_sig
    beat_duration_sec = grid['beat_interval_sec']
    grid_anchor_sec = grid['grid_anchor_sec']

    # Analysis parameters
    # Look back up to 2 beats before section start for pickup notes
    max_lookback_beats = 2
    max_lookback_sec = max_lookback_beats * beat_duration_sec

    # RMS threshold: vocal pickup must be at least this loud relative to section's vocal RMS
    # Using absolute threshold first, then validate with relative check
    ABSOLUTE_THRESHOLD = 0.008  # Minimum RMS to consider as vocal activity
    RELATIVE_THRESHOLD = 0.15   # Pickup must be >= 15% of section's average vocal RMS

    # Onset detection for precise pickup start
    # Use short-frame RMS for fine-grained temporal resolution (~11ms per frame)
    frame_length = 512  # ~11ms at 44100Hz
    hop_length = 256    # ~5.8ms resolution

    for sec in sections:
        start_bar = sec['startBar']
        section_start_sec = grid_anchor_sec + (start_bar - 1) * bar_duration_sec

        # Skip first section (no pickup possible before song start)
        if start_bar <= 1:
            sec['vocalPickup_ms'] = 0
            continue

        # Define the lookback region (up to 2 beats before section start)
        lookback_start_sec = max(0, section_start_sec - max_lookback_sec)
        lookback_end_sec = section_start_sec

        # Also get the first 2 beats of the section for relative comparison
        section_check_end_sec = min(
            section_start_sec + 2 * beat_duration_sec,
            grid_anchor_sec + sec['endBar'] * bar_duration_sec
        )

        # Convert to samples
        lookback_start_sample = int(lookback_start_sec * sr)
        lookback_end_sample = int(lookback_end_sec * sr)
        section_start_sample = int(section_start_sec * sr)
        section_check_end_sample = int(section_check_end_sec * sr)

        if lookback_start_sample >= total_samples or lookback_end_sample <= lookback_start_sample:
            sec['vocalPickup_ms'] = 0
            continue

        # Extract the lookback region
        lookback_audio = y[lookback_start_sample:min(lookback_end_sample, total_samples)]

        # Compute fine-grained RMS in the lookback region
        if len(lookback_audio) < frame_length:
            sec['vocalPickup_ms'] = 0
            continue

        rms_frames = librosa.feature.rms(
            y=lookback_audio, frame_length=frame_length, hop_length=hop_length
        )[0]

        # Check if there's any vocal activity in the lookback region
        if not np.any(rms_frames > ABSOLUTE_THRESHOLD):
            sec['vocalPickup_ms'] = 0
            continue

        # Compute section's vocal RMS for relative validation
        section_audio = y[section_start_sample:min(section_check_end_sample, total_samples)]
        if len(section_audio) > frame_length:
            section_rms = float(np.sqrt(np.mean(section_audio ** 2)))
        else:
            section_rms = 0.0

        # Validate: pickup must be meaningful relative to section's vocal level
        pickup_max_rms = float(np.max(rms_frames))
        if section_rms > 0 and pickup_max_rms < section_rms * RELATIVE_THRESHOLD:
            # Pickup is too quiet relative to section — likely bleed, not actual singing
            sec['vocalPickup_ms'] = 0
            continue

        # Find the exact onset of the pickup (first frame exceeding threshold)
        # Search from the END of lookback backward to find where vocal starts
        # (we want the earliest frame that's part of this pickup phrase)
        onset_threshold = max(ABSOLUTE_THRESHOLD, pickup_max_rms * 0.2)

        # Find first frame above threshold (searching forward)
        onset_frame = None
        for i in range(len(rms_frames)):
            if rms_frames[i] > onset_threshold:
                onset_frame = i
                break

        if onset_frame is None:
            sec['vocalPickup_ms'] = 0
            continue

        # Convert frame index to time offset from lookback start
        onset_offset_sec = onset_frame * hop_length / sr

        # Calculate pickup duration (how far before section start the vocal begins)
        pickup_start_sec = lookback_start_sec + onset_offset_sec
        pickup_offset_sec = section_start_sec - pickup_start_sec

        # Quantize to nearest half-beat for musical consistency
        half_beat_sec = beat_duration_sec / 2.0
        quantized_beats = round(pickup_offset_sec / half_beat_sec) * half_beat_sec

        # Clamp to max 2 beats
        quantized_beats = min(quantized_beats, max_lookback_sec)
        quantized_beats = max(quantized_beats, 0)

        if quantized_beats <= 0:
            sec['vocalPickup_ms'] = 0
        else:
            pickup_ms = round(quantized_beats * 1000, 3)
            sec['vocalPickup_ms'] = pickup_ms
            print(f"    Section '{sec['name']}' (bar {start_bar}): "
                  f"vocal pickup {pickup_ms:.0f}ms ({quantized_beats/beat_duration_sec:.1f} beats) before start")

    # Summary
    pickup_sections = [s for s in sections if s.get('vocalPickup_ms', 0) > 0]
    if pickup_sections:
        print(f"    ✓ Vocal pickups detected in {len(pickup_sections)}/{len(sections)} sections")
    else:
        print(f"    No vocal pickups detected (vocals start on or after beat 1)")

    return sections


# ═══════════════════════════════════════════════════════════════════════
# VOCAL MELODY REPETITION DETECTION
# ═══════════════════════════════════════════════════════════════════════

def detect_melody_repetition(classified: Dict, grid: Dict, time_sig: int = 4) -> List[Dict]:
    """Detect repeated vocal melody patterns to group similar sections.
    Returns list of {group_id, bars} where same group_id = same melody."""
    print("  Detecting melody repetition patterns...")

    vocal_path = classified.get('lead_vocal')
    if not vocal_path or not os.path.exists(vocal_path):
        print("    ! No vocal stem for melody analysis")
        return []

    bar_duration = grid['beat_interval_sec'] * time_sig
    grid_anchor = grid['grid_anchor_sec']
    total_bars = int((grid['total_duration_sec'] - grid_anchor) / bar_duration)

    try:
        y, sr = librosa.load(vocal_path, sr=SAMPLE_RATE)
    except:
        return []

    # Extract pitch contour per 4-bar phrase
    phrase_length = 4  # Compare in 4-bar chunks
    num_phrases = total_bars // phrase_length
    phrase_contours = []

    for i in range(num_phrases):
        start_sec = grid_anchor + i * phrase_length * bar_duration
        end_sec = start_sec + phrase_length * bar_duration
        start_sample = int(start_sec * sr)
        end_sample = int(end_sec * sr)

        if start_sample >= len(y):
            phrase_contours.append(np.zeros(32))
            continue

        segment = y[start_sample:min(end_sample, len(y))]
        if len(segment) < 2048:
            phrase_contours.append(np.zeros(32))
            continue

        # Extract pitch contour using chroma (more robust than pyin for comparison)
        chroma = librosa.feature.chroma_cqt(y=segment, sr=sr, hop_length=HOP_LENGTH)
        # Downsample to fixed 32 frames for comparison
        if chroma.shape[1] > 32:
            indices = np.linspace(0, chroma.shape[1] - 1, 32, dtype=int)
            contour = chroma[:, indices].flatten()
        else:
            contour = np.pad(chroma.flatten(), (0, max(0, 32 * 12 - chroma.size)))[:32 * 12]

        phrase_contours.append(contour)

    if not phrase_contours:
        return []

    # Compute similarity matrix between all phrase pairs
    from scipy.spatial.distance import cdist
    contour_array = np.array(phrase_contours)
    if contour_array.ndim == 1:
        return []

    sim_matrix = 1 - cdist(contour_array, contour_array, metric='cosine')

    # Group similar phrases (correlation > 0.8 = same melody)
    groups = []
    assigned = set()
    group_id = 0

    for i in range(num_phrases):
        if i in assigned:
            continue
        group = [i]
        assigned.add(i)
        for j in range(i + 1, num_phrases):
            if j in assigned:
                continue
            if sim_matrix[i, j] > 0.8:
                group.append(j)
                assigned.add(j)
        groups.append({
            'group_id': group_id,
            'phrase_indices': group,
            'bars': [(idx * phrase_length + 1, (idx + 1) * phrase_length) for idx in group]
        })
        group_id += 1

    melody_count = len([g for g in groups if len(g['phrase_indices']) > 1])
    print(f"    ✓ Found {melody_count} repeated melody patterns")
    return groups


# ═══════════════════════════════════════════════════════════════════════
# AI VERIFICATION — Silent Auto-Fix for Loop Points
# ═══════════════════════════════════════════════════════════════════════

def verify_and_fix_sections(sections: List[Dict], grid: Dict, classified: Dict,
                            whisper_data: Dict, time_sig: int = 4,
                            folder_path: str = "", vocal_data: Optional[Dict] = None,
                            chords: Optional[List[str]] = None,
                            llm_mode: str = "none") -> List[Dict]:
    """AI Verification: Uses LLM + audio analysis to validate each section.
    Phase 1: LLM validates section completeness using vocal/chord/energy context
    Phase 2: Audio-based scoring (downbeat alignment + phrase + splice)
    Phase 3: Auto-adjusts boundaries based on combined assessment."""
    print("  AI Verification: LLM + audio splice analysis...")

    beat_interval = grid['beat_interval_sec']
    bar_duration = beat_interval * time_sig
    grid_anchor = grid['grid_anchor_sec']
    beats = grid.get('beats', [])
    word_timestamps = whisper_data.get('word_timestamps', [])

    # Find the best audio file for analysis (prefer drums for transient clarity)
    mix_path = None
    for key in ['drums', 'bass', 'lead_vocal', 'acoustic_guitar', 'electric_guitar', 'piano']:
        p = classified.get(key)
        if p and os.path.exists(p):
            mix_path = p
            break
    if not mix_path:
        mix_path = next((v for k, v in classified.items()
                        if k not in ('_unclassified', '_full_mix', 'click', 'cue') and v and os.path.exists(v)), None)

    if not mix_path:
        print("    ! No audio file for verification — skipping")
        return sections

    # Load full audio once for efficiency
    try:
        y_full, sr = librosa.load(mix_path, sr=SAMPLE_RATE)
    except Exception as e:
        print(f"    ! Could not load audio for verification: {e}")
        return sections

    verified_count = 0
    adjusted_count = 0
    report_rows = []

    # ═══ PHASE 1: LLM VALIDATION ═══
    # Ask LLM to validate sections using vocal/chord/energy context
    llm_corrections = _llm_validate_sections(sections, grid, whisper_data,
                                              vocal_data, chords, time_sig, llm_mode)

    # Apply LLM corrections if any
    if llm_corrections:
        for correction in llm_corrections:
            idx = correction.get('index', -1)
            if 0 <= idx < len(sections):
                if 'startBar' in correction and correction['startBar'] > 0:
                    sections[idx]['startBar'] = correction['startBar']
                if 'endBar' in correction and correction['endBar'] > 0:
                    sections[idx]['endBar'] = correction['endBar']
        print(f"    LLM suggested {len(llm_corrections)} corrections")

    # ═══ PHASE 2: AUDIO-BASED SCORING + ADJUSTMENT ═══
    for sec_idx, sec in enumerate(sections):
        start_bar = sec['startBar']
        end_bar = sec['endBar']
        sec_original_start = start_bar
        sec_original_end = end_bar

        # Convert bars to seconds using ACTUAL beat positions when available
        if beats and len(beats) > 0:
            start_beat_idx = (start_bar - 1) * time_sig
            end_beat_idx = end_bar * time_sig
            start_sec = beats[min(start_beat_idx, len(beats)-1)] if start_beat_idx < len(beats) else grid_anchor + (start_bar-1) * bar_duration
            end_sec = beats[min(end_beat_idx, len(beats)-1)] if end_beat_idx < len(beats) else grid_anchor + end_bar * bar_duration
        else:
            start_sec = grid_anchor + (start_bar - 1) * bar_duration
            end_sec = grid_anchor + end_bar * bar_duration

        # Score the current loop point
        original_score = _score_splice_quality(y_full, sr, start_sec, end_sec, beats, beat_interval, word_timestamps)

        best_score = original_score
        best_start_sec = start_sec
        best_end_sec = end_sec
        best_start_bar = start_bar
        best_end_bar = end_bar

        # Iterate until score >= 99% or max 15 rounds
        TARGET_SCORE = 99
        stuck_at_round = 0
        for round_num in range(15):
            if best_score >= TARGET_SCORE:
                stuck_at_round = round_num
                break

            candidates = []
            prev_best = best_score

            # Get downbeat positions for targeted shifting
            downbeats = [beats[i] for i in range(0, len(beats), time_sig)] if beats and len(beats) >= time_sig else []

            # Strategy 1: Snap END to nearby downbeats directly
            if downbeats:
                for db in downbeats:
                    if db > best_start_sec + bar_duration and abs(db - best_end_sec) < bar_duration * 4:
                        trial_score = _score_splice_quality(y_full, sr, best_start_sec, db, beats, beat_interval, word_timestamps)
                        if trial_score > best_score:
                            candidates.append((trial_score, best_start_sec, db))

            # Strategy 2: Snap START to nearby downbeats directly
            if downbeats:
                for db in downbeats:
                    if db < best_end_sec - bar_duration and abs(db - best_start_sec) < bar_duration * 4:
                        trial_score = _score_splice_quality(y_full, sr, db, best_end_sec, beats, beat_interval, word_timestamps)
                        if trial_score > best_score:
                            candidates.append((trial_score, db, best_end_sec))

            # Strategy 3: Snap BOTH to nearby downbeats
            if downbeats:
                start_candidates = [db for db in downbeats if abs(db - best_start_sec) < bar_duration * 3]
                end_candidates = [db for db in downbeats if abs(db - best_end_sec) < bar_duration * 3]
                for sc in start_candidates[:6]:
                    for ec in end_candidates[:6]:
                        if ec > sc + bar_duration:
                            trial_score = _score_splice_quality(y_full, sr, sc, ec, beats, beat_interval, word_timestamps)
                            if trial_score > best_score:
                                candidates.append((trial_score, sc, ec))

            # Strategy 4: Shift by bar increments (±1 to ±4 bars)
            for bar_shift in [-4, -3, -2, -1, 1, 2, 3, 4]:
                trial_end = best_end_sec + bar_shift * bar_duration
                if trial_end > best_start_sec + bar_duration:
                    trial_score = _score_splice_quality(y_full, sr, best_start_sec, trial_end, beats, beat_interval, word_timestamps)
                    if trial_score > best_score:
                        candidates.append((trial_score, best_start_sec, trial_end))
                trial_start = best_start_sec + bar_shift * bar_duration
                if trial_start >= 0 and best_end_sec - trial_start > bar_duration:
                    trial_score = _score_splice_quality(y_full, sr, trial_start, best_end_sec, beats, beat_interval, word_timestamps)
                    if trial_score > best_score:
                        candidates.append((trial_score, trial_start, best_end_sec))

            if not candidates:
                stuck_at_round = round_num + 1
                break

            # Pick the best candidate
            candidates.sort(reverse=True, key=lambda x: x[0])
            winner = candidates[0]
            if winner[0] <= best_score:
                stuck_at_round = round_num + 1
                break
            best_score = winner[0]
            best_start_sec = winner[1]
            best_end_sec = winner[2]
            best_start_bar = max(1, int((best_start_sec - grid_anchor) / bar_duration) + 1)
            best_end_bar = max(best_start_bar, int((best_end_sec - grid_anchor) / bar_duration))
            stuck_at_round = round_num + 1

        # Apply best result
        if best_start_bar != sec['startBar'] or best_end_bar != sec['endBar']:
            adjusted_count += 1

        sec['startBar'] = best_start_bar
        sec['endBar'] = best_end_bar
        verified_count += 1

        # Collect report data
        status = "✓ PASS" if best_score >= TARGET_SCORE else "⚠ BELOW TARGET"
        improvement = best_score - original_score

        # Calculate score breakdown for logging
        section_bars = (best_end_sec - best_start_sec) / bar_duration
        stop_reason = "Target reached" if best_score >= TARGET_SCORE else "No better candidate found"

        report_rows.append({
            'section': sec['name'],
            'score': best_score,
            'original_score': original_score,
            'improvement': improvement,
            'stuck_at': stuck_at_round,
            'status': status,
            'original_start_bar': sec_original_start,
            'original_end_bar': sec_original_end,
            'final_start_bar': best_start_bar,
            'final_end_bar': best_end_bar,
            'section_bars': round(section_bars, 1),
            'is_4bar_multiple': abs(section_bars % 4) < 0.2,
            'stop_reason': stop_reason
        })

        # Print per-section result
        imp_str = f" (+{improvement:.0f})" if improvement > 0 else ""
        print(f"    {'✓' if best_score >= TARGET_SCORE else '⚠'} {sec['name']:<18} Score: {best_score:.0f}%{imp_str} [round {stuck_at_round}]")

    print(f"    ─── Verified {verified_count} sections, auto-adjusted {adjusted_count} ───")

    # Generate HTML report
    _generate_verification_report(report_rows, folder_path)

    return sections


def _generate_verification_report(report_rows: List[Dict], folder_path: str):
    """Generate an HTML report of the AI verification results."""
    if not folder_path:
        return

    html = """<!DOCTYPE html>
<html>
<head>
<title>AI Section Verification Report</title>
<style>
body { font-family: 'Segoe UI', Arial, sans-serif; background: #1E1E2E; color: white; padding: 20px; }
h1 { color: #4A90D9; }
table { border-collapse: collapse; width: 100%; margin-top: 16px; }
th { background: #4A90D9; color: white; padding: 10px 14px; text-align: left; font-size: 13px; }
td { padding: 8px 14px; border-bottom: 1px solid #3A3A50; font-size: 13px; }
tr:nth-child(even) { background: #2B2B3D; }
tr:nth-child(odd) { background: #1E1E2E; }
.pass { color: #7ED321; font-weight: bold; }
.fail { color: #F5A623; font-weight: bold; }
.score-high { color: #7ED321; }
.score-mid { color: #F5A623; }
.score-low { color: #E74C3C; }
.detail { color: #6C6C80; font-size: 11px; }
.timestamp { color: #6C6C80; font-size: 12px; margin-top: 8px; }
.yes { color: #7ED321; }
.no { color: #E74C3C; }
</style>
</head>
<body>
<h1>AI Section Verification Report</h1>
<p class="timestamp">Generated: """ + str(json.dumps(str(os.path.basename(folder_path)))) + """</p>
<table>
<tr>
  <th>Section</th>
  <th>Score</th>
  <th>Original→Final Bars</th>
  <th>Duration (bars)</th>
  <th>4-bar phrase?</th>
  <th>Improvement</th>
  <th>Iterations</th>
  <th>Stop Reason</th>
  <th>Status</th>
</tr>
"""
    for row in report_rows:
        score_class = "score-high" if row['score'] >= 99 else ("score-mid" if row['score'] >= 80 else "score-low")
        status_class = "pass" if "PASS" in row['status'] else "fail"
        phrase_class = "yes" if row.get('is_4bar_multiple') else "no"
        phrase_text = "✓ Yes" if row.get('is_4bar_multiple') else "✗ No"
        bars_changed = "" if row['original_start_bar'] == row['final_start_bar'] and row['original_end_bar'] == row['final_end_bar'] else " ←changed"
        html += f"""<tr>
  <td>{row['section']}</td>
  <td class="{score_class}">{row['score']:.0f}% <span class="detail">(was {row.get('original_score', 0):.0f}%)</span></td>
  <td>{row['original_start_bar']}-{row['original_end_bar']} → {row['final_start_bar']}-{row['final_end_bar']}{bars_changed}</td>
  <td>{row.get('section_bars', 0)}</td>
  <td class="{phrase_class}">{phrase_text}</td>
  <td>+{row['improvement']:.0f}</td>
  <td>{row['stuck_at']}</td>
  <td class="detail">{row.get('stop_reason', '')}</td>
  <td class="{status_class}">{row['status']}</td>
</tr>
"""
    html += """</table>
</body>
</html>"""

    report_path = os.path.join(folder_path, '_verification_report.html')
    try:
        with open(report_path, 'w', encoding='utf-8') as f:
            f.write(html)
        print(f"    Report saved: {report_path}")
    except Exception as e:
        print(f"    ! Could not save report: {e}")


def _generate_score_report(sections: List[Dict], grid: Dict, classified: Dict,
                           whisper_data: Dict, time_sig: int, folder_path: str):
    """Generate a verification report WITHOUT modifying section boundaries."""
    bar_duration = grid['beat_interval_sec'] * time_sig
    report_rows = []

    for sec in sections:
        bars = sec['endBar'] - sec['startBar'] + 1
        is_4x = bars % 4 == 0
        report_rows.append({
            'section': sec['name'],
            'score': 100 if is_4x else 75,
            'original_score': 100 if is_4x else 75,
            'improvement': 0,
            'stuck_at': 0,
            'status': "✓ PASS" if is_4x else "⚠ CHECK",
            'original_start_bar': sec['startBar'],
            'original_end_bar': sec['endBar'],
            'final_start_bar': sec['startBar'],
            'final_end_bar': sec['endBar'],
            'section_bars': bars,
            'is_4bar_multiple': is_4x,
            'stop_reason': 'Report only (no auto-fix)'
        })

    _generate_verification_report(report_rows, folder_path)


def _score_splice_quality(y: np.ndarray, sr: int, start_sec: float, end_sec: float,
                          beats: List[float], beat_interval: float,
                          word_timestamps: List[Dict]) -> float:
    """Score loop splice quality for seamless looping.
    Returns 0-100 based on:
    - 30%: Loop IN on exact downbeat (beat 1 of bar)
    - 30%: Loop OUT on exact downbeat (beat 1 of next bar)
    - 25%: Phrase completeness (section is multiple of 4 bars, natural phrase length)
    - 15%: Clean audio at splice point (near zero-crossing, no click)"""
    score = 0.0
    time_sig = 4  # Assumed 4/4
    bar_duration = beat_interval * time_sig

    # Convert to samples
    start_sample = int(start_sec * sr)
    end_sample = int(end_sec * sr)
    if start_sample >= len(y) or end_sample >= len(y) or end_sample <= start_sample:
        return 50.0

    # 1. LOOP IN ON DOWNBEAT (30% weight)
    if beats and len(beats) >= time_sig:
        downbeats = [beats[i] for i in range(0, len(beats), time_sig)]
        if downbeats:
            start_dist = min(abs(db - start_sec) for db in downbeats)
            tolerance = 0.01  # 10ms
            if start_dist <= tolerance:
                score += 100 * 0.3
            elif start_dist <= beat_interval * 0.1:
                score += 90 * 0.3
            elif start_dist <= beat_interval * 0.25:
                score += 70 * 0.3
            else:
                score += max(0, (1 - start_dist / beat_interval) * 50) * 0.3
    else:
        score += 20

    # 2. LOOP OUT ON DOWNBEAT (30% weight)
    if beats and len(beats) >= time_sig:
        downbeats = [beats[i] for i in range(0, len(beats), time_sig)]
        if downbeats:
            end_dist = min(abs(db - end_sec) for db in downbeats)
            tolerance = 0.01
            if end_dist <= tolerance:
                score += 100 * 0.3
            elif end_dist <= beat_interval * 0.1:
                score += 90 * 0.3
            elif end_dist <= beat_interval * 0.25:
                score += 70 * 0.3
            else:
                score += max(0, (1 - end_dist / beat_interval) * 50) * 0.3
    else:
        score += 20

    # 3. PHRASE COMPLETENESS (25% weight)
    # A musically complete section should be a multiple of 4 bars (common phrase length)
    # Also checks: does the section end at a natural energy resolution point?
    section_duration = end_sec - start_sec
    section_bars = section_duration / bar_duration

    # Check if section is a clean multiple of 4 bars
    remainder = section_bars % 4
    if abs(remainder) < 0.1 or abs(remainder - 4) < 0.1:
        phrase_score = 100  # Perfect — exactly N×4 bars
    elif abs(section_bars % 2) < 0.1:
        phrase_score = 80   # Multiple of 2 bars (acceptable)
    elif abs(section_bars % 1) < 0.1:
        phrase_score = 60   # Whole number of bars but not 4-bar phrase
    else:
        phrase_score = 30   # Fractional bars = definitely wrong

    # Also check energy at the OUT point — is it at a natural resolution?
    # Compare last beat energy vs average section energy
    # If last beat is much louder than average = mid-phrase (bad)
    # If last beat is similar or lower = natural ending (good)
    last_beat_start = max(0, end_sample - int(beat_interval * sr))
    if last_beat_start > start_sample:
        last_beat_rms = np.sqrt(np.mean(y[last_beat_start:end_sample] ** 2))
        section_avg_rms = np.sqrt(np.mean(y[start_sample:end_sample] ** 2))
        if section_avg_rms > 0.001:
            energy_ratio = last_beat_rms / section_avg_rms
            if energy_ratio <= 1.1:
                pass  # Natural ending, keep phrase_score
            elif energy_ratio <= 1.3:
                phrase_score = min(phrase_score, 70)  # Slightly mid-phrase
            else:
                phrase_score = min(phrase_score, 50)  # Clearly mid-phrase (building)

    score += phrase_score * 0.25

    # 4. CLEAN AUDIO AT SPLICE (15% weight)
    window = int(0.002 * sr)  # 2ms window
    if start_sample + window < len(y) and end_sample - window >= 0:
        start_rms = np.sqrt(np.mean(y[start_sample:start_sample + window] ** 2))
        end_rms = np.sqrt(np.mean(y[max(0, end_sample - window):end_sample] ** 2))
        section_max = np.max(np.abs(y[start_sample:end_sample])) + 1e-10
        start_clean = max(0, 1 - (start_rms / section_max) * 5)
        end_clean = max(0, 1 - (end_rms / section_max) * 5)
        splice_score = (start_clean + end_clean) / 2
        score += splice_score * 100 * 0.15
    else:
        score += 8

    return score


def _get_rms_at(audio_path: str, position_sec: float, duration_sec: float) -> float:
    """Get RMS energy at a specific position in an audio file."""
    try:
        y, sr = librosa.load(audio_path, sr=SAMPLE_RATE,
                            offset=max(0, position_sec), duration=duration_sec)
        if len(y) > 0:
            return float(np.sqrt(np.mean(y ** 2)))
    except:
        pass
    return 0.0


# ═══════════════════════════════════════════════════════════════════════
# SNAP SECTIONS TO GRID
# ═══════════════════════════════════════════════════════════════════════

def sections_from_lyrics_timing(section_hints: List[str], section_lyrics: Dict[str, str],
                                whisper_data: Dict, grid: Dict,
                                vocal_data: Dict = None, time_sig: int = 4) -> Optional[List[Dict]]:
    """Build section boundaries DIRECTLY from where the lyrics are actually sung.

    This is the preferred path when the user provides BOTH the exact section list
    (section_hints, in song order) AND the lyrics for each section, and Whisper has
    produced word-level timestamps.

    Instead of detecting sections independently and then trying to reconcile a
    differently-ordered lyric list onto them (which misaligns lyrics vs. vocals),
    we anchor each lyric block to the point in the vocal transcript where its
    opening words are actually sung. The result:
      - section count / names / order EXACTLY match the user's input
      - each section's time range starts where that section's words are sung
      - lyrics are aligned to the vocals by construction

    Returns a list of {name, startBar, endBar, _assigned_text} sections, or None if
    this path cannot be used (missing data) so the caller can fall back.
    """
    import re

    word_timestamps = whisper_data.get('word_timestamps', [])
    if not section_hints or not section_lyrics or not word_timestamps:
        return None

    print("  Building sections from lyric timing (vocal-aligned)...")

    bar_duration_sec = grid['beat_interval_sec'] * time_sig
    grid_anchor_sec = grid['grid_anchor_sec']
    total_bars = max(1, int((grid['total_duration_sec'] - grid_anchor_sec) / bar_duration_sec))

    def clean_words(text: str) -> List[str]:
        """Extract clean lowercase words from lyrics (strip [chords], punctuation,
        and non-lyric markers like 'Page 1/2')."""
        text = re.sub(r'\[[^\]]*\]', '', text)
        text = re.sub(r'(?i)\bpage\s*\d+\s*/\s*\d+\b', '', text)
        text = re.sub(r'[^\w\s]', '', text.lower())
        return [w for w in text.split() if len(w) > 1]

    # Normalized Whisper word stream: [{word, start_sec}]
    sung = []
    for wt in word_timestamps:
        w = re.sub(r'[^\w\s]', '', wt.get('word', '').lower().strip())
        if len(w) > 1:
            sung.append({'word': w, 'start_sec': float(wt.get('start_sec', 0))})
    if not sung:
        return None

    # Each sung word also has an end time; keep it so we can find where a
    # section's LAST word finishes (so a boundary never clips a held final word).
    sung_end = []
    for wt in word_timestamps:
        w = re.sub(r'[^\w\s]', '', wt.get('word', '').lower().strip())
        if len(w) > 1:
            sung_end.append(float(wt.get('end_sec', wt.get('start_sec', 0))))

    def find_block_onset(block_words, search_from_idx):
        """Find where a lyric block starts being sung, searching the Whisper word
        stream forward from search_from_idx. Uses a short signature of the block's
        leading words and finds the sung position with the best local word overlap.

        Returns (onset_sec, sung_index_at_match) or (None, search_from_idx) if no
        confident match.
        """
        signature = block_words[:6]
        if not signature:
            return None, search_from_idx

        # Common filler words are poor anchors (they appear everywhere); a section onset
        # should be pinned to a DISTINCTIVE signature word, not "is/my/for/me".
        STOP = {'the', 'is', 'a', 'an', 'of', 'to', 'in', 'on', 'my', 'me', 'for', 'and',
                'that', 'this', 'it', 'be', 'i', 'you', 'we', 'he', 'so', 'no', 'all',
                'now', 'oh', 'will', 'can', 'are', 'was', 'he', 'she'}

        def word_match(sig, sw):
            if sig == sw:
                return 2
            if len(sig) >= 3 and len(sw) >= 3 and (sig in sw or sw in sig):
                return 1
            return 0

        best_score = 0
        best_i = -1
        window = len(signature) + 4  # allow for mis-transcribed / dropped words
        for i in range(search_from_idx, len(sung)):
            sw_window = [sung[j]['word'] for j in range(i, min(i + window, len(sung)))]
            score = 0
            for sig in signature:
                for sw in sw_window:
                    m = word_match(sig, sw)
                    if m:
                        score += m
                        break
            if score > best_score:
                best_score = score
                best_i = i
                if score >= len(signature) * 2 * 0.67:
                    break

        if best_i < 0 or best_score < max(2, len(signature)):
            return None, search_from_idx

        # Anchor the ONSET to where the FIRST signature word actually appears inside the
        # matched window — NOT the window's start index. The window start can be the tail of
        # the previous section (e.g. "...set free" before a CHORUS), which otherwise places the
        # onset too early and makes the next section swallow the previous section's last line.
        # Prefer a DISTINCTIVE (non-stopword) signature word as the anchor; fall back to the
        # earliest signature hit of any kind.
        win_end = min(best_i + window, len(sung))
        anchor_idx = None
        # Pass 1: first distinctive signature word (exact or substring) in the window.
        for j in range(best_i, win_end):
            sw = sung[j]['word']
            for sig in signature:
                if sig in STOP:
                    continue
                if word_match(sig, sw) >= 1:
                    anchor_idx = j
                    break
            if anchor_idx is not None:
                break
        # Pass 2: if the block's leading words are all stopwords, use the first signature hit.
        if anchor_idx is None:
            for j in range(best_i, win_end):
                sw = sung[j]['word']
                if any(word_match(sig, sw) >= 1 for sig in signature):
                    anchor_idx = j
                    break
        if anchor_idx is None:
            anchor_idx = best_i

        return sung[anchor_idx]['start_sec'], anchor_idx + 1

    # Build ordered blocks from the hint list
    blocks = []
    for name in section_hints:
        text = None
        for key, val in section_lyrics.items():
            if key.upper().strip() == name.upper().strip():
                text = val
                break
        if text is None:
            for key, val in section_lyrics.items():
                if key.upper().strip() in name.upper().strip() or \
                   name.upper().strip() in key.upper().strip():
                    text = val
                    break
        blocks.append({'name': name, 'text': text or '', 'words': clean_words(text or '')})

    # Vocal onset (bar) as a floor for the first SUNG section
    vocal_onset_bar = (vocal_data or {}).get('vocal_onset_bar') or 1

    # Resolve an onset time for each block in order. Track the matched word index
    # so we can also find where each section's LAST word ends.
    search_idx = 0
    onsets = []        # onset_sec per block (None = instrumental / unresolved)
    match_idx = []     # sung-stream index where each block's words begin (or None)
    for b in blocks:
        if not b['words']:
            onsets.append(None)  # instrumental section (e.g. INTRO with only chords)
            match_idx.append(None)
            continue
        prev_search = search_idx
        onset_sec, search_idx = find_block_onset(b['words'], search_idx)
        onsets.append(onset_sec)
        # search_idx now points just past the matched onset word
        match_idx.append((search_idx - 1) if onset_sec is not None else None)

    # The last sung word that belongs to block i ends just before block (i+1)'s
    # first word begins. Use that to compute a vocal "gap" end time per section.
    def block_vocal_end(i: int):
        """End time (sec) of the last sung word belonging to block i, or None."""
        start = match_idx[i]
        if start is None:
            return None
        # Find the next block that actually matched a sung word
        nxt_start = None
        for j in range(i + 1, len(blocks)):
            if match_idx[j] is not None:
                nxt_start = match_idx[j]
                break
        last_word = (nxt_start - 1) if nxt_start is not None else (len(sung_end) - 1)
        last_word = max(start, min(last_word, len(sung_end) - 1))
        return sung_end[last_word] if last_word >= 0 else None

    # Convert a time to the nearest bar boundary.
    def sec_to_bar(sec: float) -> int:
        return max(1, min(total_bars, int(round((sec - grid_anchor_sec) / bar_duration_sec)) + 1))

    start_bars: List[Optional[int]] = [None] * len(blocks)
    for i, onset in enumerate(onsets):
        if onset is not None:
            start_bars[i] = sec_to_bar(onset)

    # First section always starts at bar 1 (covers the lead-in / intro)
    start_bars[0] = 1

    # If the first SUNG block resolved before the measured vocal onset, clamp it.
    for i, b in enumerate(blocks):
        if b['words'] and start_bars[i] is not None:
            if i > 0 and start_bars[i] < vocal_onset_bar:
                start_bars[i] = vocal_onset_bar
            break

    # Fill unresolved (None) start bars by interpolating between known neighbours.
    def next_known(idx: int) -> Optional[int]:
        for j in range(idx + 1, len(start_bars)):
            if start_bars[j] is not None:
                return j
        return None

    i = 1
    while i < len(start_bars):
        if start_bars[i] is None:
            prev_bar = start_bars[i - 1] or 1
            nk = next_known(i)
            if nk is None:
                # No further anchor: evenly space remaining sections to the end
                remaining = len(start_bars) - i
                span = max(1, (total_bars + 1) - prev_bar)
                step = max(1, span // (remaining + 1))
                start_bars[i] = min(total_bars, prev_bar + step)
            else:
                gap_sections = nk - (i - 1)
                span = max(1, start_bars[nk] - prev_bar)
                step = max(1, span // gap_sections)
                start_bars[i] = min(start_bars[nk] - 1, prev_bar + step) if start_bars[nk] > prev_bar + 1 else prev_bar + 1
        i += 1

    # Enforce strictly increasing, in-range start bars
    for i in range(1, len(start_bars)):
        if start_bars[i] <= start_bars[i - 1]:
            start_bars[i] = min(total_bars, start_bars[i - 1] + 1)

    # Build sections. The boundary between section i and i+1 must sit AFTER
    # section i's last sung word finishes, so a held final line (e.g. the tail of
    # "I've been set free") is never clipped. Each boundary is snapped to the bar
    # line nearest that section's last-word end (plus a release buffer), stepping
    # to the next bar rather than clipping a word that is still ringing.
    boundary_start = list(start_bars)  # start bar for each section (may be nudged)

    # A held final word (e.g. the tail of "I've been set free") must not be cut.
    # Whisper's word-end time is slightly short of the actual sung release, so add
    # a small release buffer before deciding which bar the boundary belongs to.
    RELEASE_BUFFER_SEC = 0.35

    def bar_of_sec(sec: float) -> float:
        """Fractional bar position of a time (1-based: bar 1 starts at anchor)."""
        return (sec - grid_anchor_sec) / bar_duration_sec + 1.0

    for i in range(len(blocks) - 1):
        next_start = start_bars[i + 1]
        v_end = block_vocal_end(i)
        next_onset = onsets[i + 1]
        if v_end is None:
            continue

        # Which bar line should the boundary sit on?
        #  - It must be at or after the bar that still contains section i's last
        #    sung word (plus a release buffer), so the word is never clipped.
        #  - Prefer the bar line nearest the word-end so we don't over-extend.
        word_end_bar_frac = bar_of_sec(v_end + RELEASE_BUFFER_SEC)
        # Round to nearest bar line, but if that bar line falls BEFORE the buffered
        # word end, step to the next bar (never clip the held word).
        nearest_bar = int(round(word_end_bar_frac))
        if nearest_bar < word_end_bar_frac - 1e-6:
            nearest_bar += 1
        needed_start = max(1, min(total_bars, nearest_bar))

        # Keep boundaries in order and strictly increasing.
        new_start = max(needed_start, boundary_start[i] + 1)
        new_start = min(new_start, total_bars)
        boundary_start[i + 1] = new_start

    # Re-enforce strictly increasing after adjustment
    for i in range(1, len(boundary_start)):
        if boundary_start[i] <= boundary_start[i - 1]:
            boundary_start[i] = min(total_bars, boundary_start[i - 1] + 1)

    sections = []
    for i, b in enumerate(blocks):
        start_bar = boundary_start[i]
        end_bar = (boundary_start[i + 1] - 1) if i + 1 < len(blocks) else total_bars
        end_bar = max(start_bar, end_bar)
        sections.append({
            'name': b['name'],
            'startBar': start_bar,
            'endBar': end_bar,
            '_assigned_text': b['text'],
        })

    print(f"    ✓ Built {len(sections)} vocal-aligned sections from lyrics")
    for s in sections:
        print(f"      {s['name']}: bar {s['startBar']}-{s['endBar']}")
    return sections


def snap_sections_to_grid(sections: List[Dict], grid: Dict, time_sig: int = 4) -> List[Dict]:
    """Snap all section boundaries to exact bar downbeats using the click track grid."""
    bar_duration_sec = grid['beat_interval_sec'] * time_sig
    grid_anchor = grid['grid_anchor_sec']
    total_bars = int((grid['total_duration_sec'] - grid_anchor) / bar_duration_sec)

    for section in sections:
        # Clamp to valid range
        section['startBar'] = max(1, min(section['startBar'], total_bars))
        section['endBar'] = max(section['startBar'], min(section['endBar'], total_bars))

    # Remove any zero-length sections
    sections = [s for s in sections if s['endBar'] >= s['startBar']]

    return sections


# ═══════════════════════════════════════════════════════════════════════
# CHORD CHART BUILDER
# ═══════════════════════════════════════════════════════════════════════

def build_chord_chart(grid: Dict, sections: List[Dict], chords: List[str],
                      whisper_data: Dict, time_sig: int = 4,
                      lines_per_slide: int = 4, slide_lead_ms: int = 500) -> Dict:
    """Build chord chart data and populate section text with inline [chord] format.

    NEW UNIFIED SYSTEM:
    - Lyrics + chords stored as inline [chord] format in sections[].text
      e.g., "[B]    The Lord bless you  [E]and keep you"
    - ChordChart slides only store timing (when to advance)
    - Text is read from sections[].text at display time

    Returns dict with 'slides' (timing) and 'section_texts' (inline [chord] per section).
    """
    bar_duration_sec = grid['beat_interval_sec'] * time_sig
    grid_anchor_sec = grid['grid_anchor_sec']
    word_timestamps = whisper_data.get('word_timestamps', [])

    slides = []
    section_texts = {}  # section_name -> inline [chord] text

    for sec_idx, section in enumerate(sections):
        start_bar = section['startBar']
        end_bar = section['endBar']
        section_name = section['name']
        section_start_sec = grid_anchor_sec + (start_bar - 1) * bar_duration_sec
        section_end_sec = grid_anchor_sec + end_bar * bar_duration_sec

        # Get words in this section's time range
        section_words = [w for w in word_timestamps
                         if w['start_sec'] >= section_start_sec - 0.1
                         and w['start_sec'] < section_end_sec + 0.1]

        # Get chords in this section (one per bar)
        section_chords = []
        for bar_idx in range(start_bar - 1, min(end_bar, len(chords))):
            chord = chords[bar_idx] if bar_idx < len(chords) else 'N'
            if chord and chord != 'N':
                bar_start_sec = grid_anchor_sec + bar_idx * bar_duration_sec
                section_chords.append({
                    'chord': chord,
                    'time_sec': bar_start_sec
                })

        # Next section info for preview
        next_section_name = ""
        next_section_chord = ""
        if sec_idx + 1 < len(sections):
            next_section_name = sections[sec_idx + 1]['name']
            next_bar = sections[sec_idx + 1]['startBar'] - 1
            if next_bar < len(chords) and chords[next_bar] != 'N':
                next_section_chord = chords[next_bar]

        # If no words (instrumental section), create chord-only text
        if not section_words:
            chord_text = " - ".join(f"[{c['chord']}]" for c in section_chords[:8])
            if not chord_text:
                chord_text = "[N.C.](Instrumental)"
            section_texts[section_name] = chord_text

            slides.append({
                'sectionName': section_name,
                'slideIndex': 0,
                'startMs': section_start_sec * 1000,
                'endMs': section_end_sec * 1000,
                'nextSectionName': next_section_name,
                'nextSectionChord': next_section_chord,
                'manualTiming': False
            })
            continue

        # Break words into phrases (by punctuation or pauses > 1 second)
        phrases = []
        current_phrase = []
        for i, word in enumerate(section_words):
            current_phrase.append(word)

            is_phrase_end = False
            word_text = word['word']

            if any(word_text.rstrip().endswith(p) for p in ['.', ',', '!', '?', ';', ':']):
                is_phrase_end = True

            if i + 1 < len(section_words):
                gap = section_words[i + 1]['start_sec'] - word['end_sec']
                if gap > 0.8:
                    is_phrase_end = True

            phrase_text = ' '.join(w['word'] for w in current_phrase)
            if len(phrase_text) > 60:
                is_phrase_end = True

            if is_phrase_end or i == len(section_words) - 1:
                if current_phrase:
                    phrases.append(current_phrase)
                    current_phrase = []

        # Build inline [chord] text for this section
        inline_lines = []
        for phrase in phrases:
            line_text = ' '.join(w['word'] for w in phrase)
            line_start = phrase[0]['start_sec']
            line_end = phrase[-1]['end_sec']

            # Find chords during this phrase and insert as [chord]
            inline_line = ""
            char_pos = 0
            last_chord_inserted = False

            for w_idx, w in enumerate(phrase):
                # Check if any chord falls at or near this word's start
                chord_prefix = ""
                for chord_info in section_chords:
                    ct = chord_info['time_sec']
                    if ct >= w['start_sec'] - 0.1 and ct < w['start_sec'] + 0.3:
                        chord_prefix = f"[{chord_info['chord']}]"
                        # Remove this chord so it's not used again
                        section_chords = [c for c in section_chords if c is not chord_info]
                        break

                if w_idx > 0:
                    inline_line += " "
                inline_line += chord_prefix + w['word']

            inline_lines.append(inline_line)

        # Join lines and store as section text (no empty lines = one slide)
        section_text = '\n'.join(inline_lines)
        section_texts[section_name] = section_text

        # Build slide timing entries (group lines into slides of ~4 lines)
        # Group phrases into slides of the configured size (clamped 2-4). The slide START is the
        # first word's start minus the read-ahead lead (so the slide appears slightly before the
        # word is sung), clamped so it never precedes the section start.
        lps = max(2, min(4, lines_per_slide))
        slide_index = 0
        for i in range(0, len(phrases), lps):
            slide_phrases = phrases[i:i + lps]
            if not slide_phrases:
                continue

            raw_start = slide_phrases[0][0]['start_sec'] * 1000
            slide_start = max(section_start_sec * 1000, raw_start - slide_lead_ms)
            slide_end = slide_phrases[-1][-1]['end_sec'] * 1000

            slides.append({
                'sectionName': section_name,
                'slideIndex': slide_index,
                'startMs': slide_start,
                'endMs': slide_end,
                'nextSectionName': next_section_name if i + lps >= len(phrases) else "",
                'nextSectionChord': next_section_chord if i + lps >= len(phrases) else "",
                'manualTiming': False
            })
            slide_index += 1

    return {'slides': slides, 'section_texts': section_texts}


# ═══════════════════════════════════════════════════════════════════════
# BUILD SONG JSON OUTPUT
# ═══════════════════════════════════════════════════════════════════════

SECTION_COLORS = {
    'INTRO': '#4A90D9',
    'VERSE': '#7ED321',
    'PRE-CHORUS': '#F5A623',
    'CHORUS': '#E74C3C',
    'BRIDGE': '#9B59B6',
    'INSTRUMENTAL': '#50E3C2',
    'TAG': '#F5A623',
    'OUTRO': '#4A90D9',
}


def build_song_json(grid: Dict, sections: List[Dict], classified: Dict,
                    key: str, files: List[str], folder_path: str,
                    time_sig: int = 4, chord_chart: Optional[Dict] = None) -> Dict:
    """Build the final song.json output."""

    bar_duration_sec = grid['beat_interval_sec'] * time_sig
    grid_anchor_ms = grid['grid_anchor_sec'] * 1000

    # Extract section_texts from chord_chart result (if available)
    section_texts = chord_chart.get('section_texts', {}) if chord_chart else {}

    # Beat positions: use actual detected positions (from click track or onsets)
    beat_positions_ms = []
    if 'beats' in grid and grid['beats']:
        beat_positions_ms = [round(b * 1000, 3) for b in grid['beats']]
    else:
        # Fallback: generate mathematical positions
        t = grid['grid_anchor_sec']
        while t < grid['total_duration_sec']:
            beat_positions_ms.append(round(t * 1000, 3))
            t += grid['beat_interval_sec']

    # Convert section bars to milliseconds using ACTUAL beat positions when available
    output_sections = []
    beatsPerBar = time_sig

    for sec in sections:
        start_bar = sec['startBar']
        end_bar = sec['endBar']

        # Use actual beat positions if available (sample-accurate)
        if beat_positions_ms and len(beat_positions_ms) > 0:
            start_beat_idx = (start_bar - 1) * beatsPerBar
            end_beat_idx = end_bar * beatsPerBar
            loop_in_ms = beat_positions_ms[min(start_beat_idx, len(beat_positions_ms) - 1)] if start_beat_idx < len(beat_positions_ms) else grid_anchor_ms + (start_bar - 1) * bar_duration_sec * 1000
            loop_out_ms = beat_positions_ms[min(end_beat_idx, len(beat_positions_ms) - 1)] if end_beat_idx < len(beat_positions_ms) else grid_anchor_ms + end_bar * bar_duration_sec * 1000
        else:
            # Fallback: mathematical conversion
            loop_in_ms = grid_anchor_ms + (start_bar - 1) * bar_duration_sec * 1000
            loop_out_ms = grid_anchor_ms + end_bar * bar_duration_sec * 1000

        # Determine color
        color = '#4A90D9'
        for prefix, c in SECTION_COLORS.items():
            if sec['name'].upper().startswith(prefix):
                color = c
                break

        output_sections.append({
            'name': sec['name'],
            'loopIn_ms': round(loop_in_ms, 3),
            'loopOut_ms': round(loop_out_ms, 3),
            'startBar': start_bar,
            'endBar': end_bar,
            'color': color,
            'lightingPresetIndex': len(output_sections),
            'text': sec.get('_assigned_text', '') or section_texts.get(sec['name'], ''),
            'vocalPickup_ms': round(sec.get('vocalPickup_ms', 0), 3)
        })

    # Build track list
    tracks = []

    # Demucs now writes stems DIRECTLY into the song folder (alongside the original mix). For a
    # single-track song, after separation the folder contains: the original mix + vocals/drums/
    # bass/guitar/piano/other.wav. We build the track list by scanning the folder for those stems
    # and ALSO include the original mix as a track (option A). song.json lives in this same folder,
    # so track paths are plain filenames.
    use_separated = False
    if len(files) == 1:
        # Standard Demucs stem names expected in this folder (no click.wav — the click comes from
        # the dedicated "Auto Generate Click Track" button or the manual click).
        stem_files = {
            'vocals.wav': ('Lead Vocal', 'FOH', '#E74C3C'),
            'drums.wav': ('Drums', 'FOH', '#F39C12'),
            'bass.wav': ('Bass', 'FOH', '#27AE60'),
            'guitar.wav': ('Guitar', 'FOH', '#8E44AD'),
            'piano.wav': ('Piano', 'FOH', '#50E3C2'),
            'other.wav': ('Other', 'FOH', '#4A90D9'),
        }
        existing_stems = []
        for stem_file, (label, output, color) in stem_files.items():
            if os.path.exists(os.path.join(folder_path, stem_file)):
                existing_stems.append({
                    'file': stem_file,
                    'label': label,
                    'output': output,
                    'defaultVolume': 1.0,
                    'defaultMute': False,
                    'color': color
                })
        if len(existing_stems) >= 3:  # Need at least vocals+drums+bass
            # Option A: include the ORIGINAL mix as a track too (muted by default so the stems play
            # on their own; the operator can unmute the full mix if they want it as a reference).
            orig = Path(files[0]).name
            tracks.append({
                'file': orig,
                'label': 'Full Mix',
                'output': 'FOH',
                'defaultVolume': 1.0,
                'defaultMute': True,
                'color': '#888888'
            })
            tracks.extend(existing_stems)
            use_separated = True
            print(f"  Using Demucs-separated stems + full mix for playback ({len(tracks)} tracks)")

    if not use_separated:
        for f in files:
            label = _guess_track_label(f)
            output = 'IEM' if 'click' in label.lower() or 'cue' in label.lower() else 'FOH'
            tracks.append({
                'file': Path(f).name,
                'label': label,
                'output': output,
                'defaultVolume': 0.8 if output == 'IEM' else 1.0,
                'defaultMute': False,
                'color': '#FF6B6B' if output == 'IEM' else '#4A90D9'
            })

    return {
        'title': Path(folder_path).name,
        'artist': '',
        'bpm': round(grid['bpm'], 4),
        'timeSignatureNumerator': time_sig,
        'timeSignatureDenominator': 4,
        'beatInterval_ms': round(grid['beat_interval_sec'] * 1000, 3),
        'barDuration_ms': round(bar_duration_sec * 1000, 3),
        'gridAnchor_ms': round(grid_anchor_ms, 3),
        'originalKey': key,
        'tracks': tracks,
        'sections': output_sections,
        'lightingPresets': [],
        'chordChart': {'slides': chord_chart.get('slides', [])} if chord_chart else {'slides': []},
        'beatPositions_ms': beat_positions_ms,
        # Grid-fit analysis (verdict + metrics) so the UI can show the analyze-first dialog and
        # report whether the emitted grid is even or tempo-following.
        'gridFit': grid.get('grid_fit', {}),
    }


def _guess_track_label(filepath: str) -> str:
    """Guess a human-readable label from filename."""
    import re
    name = Path(filepath).stem
    cleaned = re.sub(r'^\d+[_\-\s]*', '', name)
    labels = {
        'click': 'Click', 'cue': 'Cues', 'drum': 'Drums',
        'bass': 'Bass', 'acousticguitar': 'Acoustic Guitar',
        'electricguitar': 'Electric Guitar', 'guitar': 'Guitar',
        'piano': 'Piano', 'keys': 'Keys', 'synth': 'Synth',
        'pad': 'Pad', 'leadvocal': 'Lead Vocal', 'vocal': 'Vocal',
        'bgv': 'Backing Vocals', 'strings': 'Strings', 'organ': 'Organ',
    }
    for key_word, label in labels.items():
        if key_word in cleaned.lower().replace(' ', '').replace('_', ''):
            return label
    return cleaned if cleaned else name



# ═══════════════════════════════════════════════════════════════════════
# DOWNBEAT PHASE DETECTION (music-driven — independent of the click's own accent)
# ═══════════════════════════════════════════════════════════════════════

def detect_downbeat_phase(classified: Dict, grid: Dict, time_sig: int = 4) -> Dict[str, Any]:
    """Determine the TRUE musical downbeat (beat 1) from the MUSIC — not from the
    click track's own high-pitch accent, which may be placed wrong.

    The click grid gives accurate beat SPACING; this finds which of the `time_sig`
    beat positions is beat 1 by scoring each phase candidate with:
      - CHORD CHANGES (bass root + piano/guitar triad) — the strongest signal;
        harmony changes on the downbeat. Bass is weighted highest.
      - KICK on the downbeat (sub 40-90 Hz) — kick usually hits beat 1 (& 3).
      - SNARE on the backbeat (180-450 Hz) — snare is NOT on the downbeat; the
        chord typically changes just AFTER the snare. Used as a corroborating
        check, not a primary driver.

    Returns a dict:
      {
        'phase': int,                # 0..time_sig-1, beats to shift anchor forward
        'shift_ms': float,           # phase * beat_interval_ms
        'corrected_anchor_sec': float,
        'confidence': float,         # 0..1 how decisive the chord-change vote was
        'scores': {phase: score},    # blended chord-change score per phase
        'snare_ok': bool,            # snare landed on a backbeat (sanity)
        'method': str
      }
    """
    import numpy as _np
    from scipy.signal import find_peaks as _find_peaks, butter as _butter, sosfiltfilt as _sosfiltfilt

    beats = grid.get('beats', [])
    if not beats or len(beats) < time_sig * 2:
        return {'phase': 0, 'shift_ms': 0.0,
                'corrected_anchor_sec': grid.get('grid_anchor_sec', 0.0),
                'confidence': 0.0, 'scores': {}, 'snare_ok': False,
                'method': 'insufficient-beats'}

    beats = _np.asarray(beats, dtype=float)
    beat_interval = grid['beat_interval_sec']
    note_names = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B']

    # ── Per-beat chord/root label for one harmonic stem ──
    # Returns a list (one label per beat). For bass we use the dominant pitch class (the root);
    # for chordal instruments we match the beat's chroma to the best major/minor triad.
    def labels_per_beat(path, bass=False):
        try:
            y, sr = librosa.load(path, sr=SAMPLE_RATE)
        except Exception:
            return None
        ch = librosa.feature.chroma_cqt(y=y, sr=sr, hop_length=1024)
        ct = librosa.frames_to_time(_np.arange(ch.shape[1]), sr=sr, hop_length=1024)
        out = []
        for bt in beats:
            m = (ct >= bt) & (ct < bt + beat_interval)
            vec = ch[:, m].mean(axis=1) if m.any() else ch[:, int(_np.argmin(_np.abs(ct - bt)))]
            if bass:
                out.append(int(_np.argmax(vec)))  # dominant pitch class (root)
            else:
                n = _np.linalg.norm(vec)
                if n > 0:
                    vec = vec / n
                best, sc = -1, -1.0
                for root in range(12):
                    for third in (4, 3):  # major / minor triad
                        tmpl = _np.zeros(12)
                        tmpl[root] = 1; tmpl[(root + 4 if third == 4 else root + 3) % 12] = 1
                        tmpl[(root + 7) % 12] = 1
                        tmpl = tmpl / _np.linalg.norm(tmpl)
                        s = float(_np.dot(vec, tmpl))
                        if s > sc:
                            sc, best = s, root * 2 + (1 if third == 3 else 0)
                out.append(best)
        return out

    # ── HARMONIC CHANGE NOVELTY (time-domain, across ALL harmonic instruments) ──
    # Beat 1 of a bar is where bass + guitar + piano + keys change chord TOGETHER. Instead of
    # quantizing chord labels to a (possibly mis-anchored) beat grid, we measure the actual
    # HARMONIC CHANGE at every time frame for each instrument — how much its chroma shifts from
    # one short window to the next — and SUM that novelty across instruments. Peaks in the summed
    # novelty are the moments where several instruments changed chord at once = candidate
    # downbeats. We then score each beat phase by how much of that coincident-change energy lands
    # on it.
    # CHORD-LABEL-CHANGE METHOD (robust to note re-articulation). A re-struck SAME chord creates a
    # new note onset but NO harmonic change — chroma-flux/onset methods therefore fire on every
    # beat and wash the downbeat out (observed: flat phase scores). Instead we LABEL each beat with
    # a chord (bass → dominant pitch-class root; guitar/piano → best major/minor triad), STABILIZE
    # labels with a short mode filter so a held chord reads as one label, then mark a CHANGE only
    # when the stabilized label differs from the previous beat. That yields ~per-bar changes, which
    # actually carry beat 1.
    from collections import Counter as _Counter

    # Use ONLY the real instrument stems — bass, guitar and piano. The 'keys' role is Demucs'
    # 'other.wav' catch-all (pads, synths, strings, bleed) and is deliberately excluded. Drums are
    # handled separately below (kick/snare phase corroboration).
    harmonic_stems = [('bass', 1.5), ('piano', 1.0), ('acoustic_guitar', 1.0),
                      ('electric_guitar', 1.0)]

    # Pre-load each stem's chroma once (hop 2048 ≈ 93 ms at 22050). Re-labelled per candidate
    # offset below; the chroma itself does not depend on the grid.
    stem_chroma = []  # list of (weight, is_bass, chroma[12,T], times[T])
    for role, weight in harmonic_stems:
        path = classified.get(role)
        if not path or not os.path.exists(path):
            continue
        try:
            y, sr = librosa.load(path, sr=SAMPLE_RATE)
        except Exception:
            continue
        ch = librosa.feature.chroma_cqt(y=y, sr=sr, hop_length=2048)
        ct = librosa.frames_to_time(_np.arange(ch.shape[1]), sr=sr, hop_length=2048)
        stem_chroma.append((weight, role == 'bass', ch, ct))
    n_harm = len(stem_chroma)
    used_any = n_harm > 0

    def _triad_label(vec):
        n = _np.linalg.norm(vec)
        if n > 0:
            vec = vec / n
        best, sc = -1, -1.0
        for root in range(12):
            for third in (4, 3):  # major / minor
                tmpl = _np.zeros(12)
                tmpl[root] = 1; tmpl[(root + third) % 12] = 1; tmpl[(root + 7) % 12] = 1
                tmpl = tmpl / _np.linalg.norm(tmpl)
                s = float(_np.dot(vec, tmpl))
                if s > sc:
                    sc, best = s, root * 2 + (1 if third == 3 else 0)
        return best

    def _change_energy_for_grid(grid_beats):
        """Summed stabilized chord-change strength per beat on the given grid."""
        ce = _np.zeros(len(grid_beats))
        for weight, is_bass, ch, ct in stem_chroma:
            labs = []
            for bt in grid_beats:
                m = (ct >= bt) & (ct < bt + beat_interval)
                vec = ch[:, m].mean(axis=1) if m.any() else ch[:, int(_np.argmin(_np.abs(ct - bt)))]
                labs.append(int(_np.argmax(vec)) if is_bass else _triad_label(vec))
            labs = _np.asarray(labs)
            # Stabilize: mode over a +/-1 beat window removes single-beat estimator flicker.
            stab = labs.copy()
            for i in range(len(labs)):
                lo = max(0, i - 1); hi = min(len(labs), i + 2)
                stab[i] = _Counter(labs[lo:hi].tolist()).most_common(1)[0][0]
            chg = _np.concatenate([[0.0], (_np.diff(stab) != 0).astype(float)]) * weight
            ce[:len(chg)] += chg
        return ce

    # ── JOINT RE-ANCHOR + BEAT-1 SEARCH ──
    # The tempo (beat spacing) is correct but the grid POSITION can be off by a fraction of a beat.
    # We scan sub-beat offsets across one beat; for each offset we compute the chord-change energy
    # per bar-position and score by how dominant the strongest position is (peakiness). The winning
    # offset both re-anchors the grid onto the chord changes AND reveals which bar-position is
    # beat 1 (the one where chord changes concentrate).
    sub_beat_shift_sec = 0.0
    align_strength = 0.0
    best_phase_energy = _np.zeros(time_sig)
    reanchored = beats
    if used_any:
        N_OFF = 12
        best = None
        for oi in range(N_OFF):
            off = (oi / N_OFF) * beat_interval
            # Shift grid within +/- half a beat (nearest representation so it barely moves).
            shift = off if off <= beat_interval / 2.0 else off - beat_interval
            gb = beats + shift
            if gb[0] < 0:
                gb = gb - gb[0]
            ce = _change_energy_for_grid(gb)
            pe = _np.zeros(time_sig)
            for i in range(len(gb)):
                pe[i % time_sig] += ce[i]
            tot = float(pe.sum()) or 1.0
            pe = pe / tot
            peak = float(_np.max(pe))  # dominance of the strongest bar position
            if best is None or peak > best[0]:
                best = (peak, shift, pe.copy(), gb)
        peak, sub_beat_shift_sec, best_phase_energy, reanchored = best
        # align_strength: how far the winning phase rises above an even 1/time_sig split (0..1).
        align_strength = max(0.0, min(1.0, (peak - 1.0 / time_sig) / (1.0 - 1.0 / time_sig)))

    scores = {p: float(best_phase_energy[p]) for p in range(time_sig)}

    # ── Kick (sub) and snare (body) phase, for corroboration ──
    # Matched against the RE-ANCHORED grid so drum hits are compared to beats that sit on the music.
    def band_phase(path, lo, hi):
        if not path or not os.path.exists(path):
            return None
        try:
            yd, srd = librosa.load(path, sr=44100)
        except Exception:
            return None
        hi = min(hi, srd / 2 - 100)
        sos = _butter(4, [lo, hi], btype='band', fs=srd, output='sos')
        filt = _sosfiltfilt(sos, yd)
        oe = librosa.onset.onset_strength(y=filt, sr=srd, hop_length=256)
        ot = librosa.frames_to_time(_np.arange(len(oe)), sr=srd, hop_length=256)
        pk, _ = _find_peaks(oe, height=_np.max(oe) * 0.25, distance=int(srd * 0.20 / 256))
        tally = {p: 0 for p in range(time_sig)}
        for t in ot[pk]:
            j = int(_np.argmin(_np.abs(reanchored - t)))
            if abs(reanchored[j] - t) < beat_interval * 0.20:
                tally[j % time_sig] += 1
        return tally

    drums_path = classified.get('drums')
    kick_tally = band_phase(drums_path, 40, 90)
    snare_tally = band_phase(drums_path, 180, 450)

    # Primary decision: where SIMULTANEOUS multi-instrument chord changes concentrate = beat 1.
    # If no harmonic stems produced a signal, fall back to the kick (kick on downbeat).
    if used_any and max(scores.values()) > 0:
        phase = max(range(time_sig), key=lambda p: scores[p])
        method = 'simultaneous-chord-change'
    elif kick_tally and sum(kick_tally.values()) > 0:
        phase = max(range(time_sig), key=lambda p: kick_tally[p])
        method = 'kick-fallback'
        scores = {p: float(kick_tally[p]) for p in range(time_sig)}
    else:
        phase = 0
        method = 'no-signal'

    # Confidence = how dominant the winning phase is vs the runner-up, GATED by how well the grid
    # could be re-anchored (align_strength). A flat phase vote OR a weak re-anchor both lower it.
    svals = sorted(scores.values(), reverse=True) if scores else [0, 0]
    top = svals[0] if svals else 0.0
    second = svals[1] if len(svals) > 1 else 0.0
    phase_margin = float((top - second) / top) if top > 0 else 0.0
    confidence = float(phase_margin * (0.5 + 0.5 * min(1.0, align_strength * 2.0)))

    # Sanity: the snare should sit on a backbeat (1 or 3 beats from the downbeat),
    # i.e. NOT on the detected downbeat. Matches "chord changes after the snare".
    snare_ok = False
    if snare_tally and sum(snare_tally.values()) > 0:
        snare_pos = max(range(time_sig), key=lambda p: snare_tally[p])
        snare_ok = ((snare_pos - phase) % time_sig) in (1, 3)

    # Snare corroboration: when the snare independently lands on a backbeat relative to the
    # chord-change downbeat, two unrelated signals agree — boost confidence. This is exactly the
    # "snare on 2 & 4, chord changes on 1" worship pattern. A disagreeing snare leaves it as-is.
    if snare_ok:
        confidence = min(1.0, confidence + 0.12)

    # The corrected anchor combines BOTH corrections:
    #   1. sub_beat_shift_sec — continuous re-anchor so beats land ON the chord changes.
    #   2. phase * beat_interval — whole-beat shift so beat 1 is the right bar position.
    anchor = grid['grid_anchor_sec'] + sub_beat_shift_sec + phase * beat_interval
    while anchor < 0:
        anchor += beat_interval
    total_shift_ms = (sub_beat_shift_sec + phase * beat_interval) * 1000.0

    print(f"  Downbeat detection: method={method}, sub-beat re-anchor={sub_beat_shift_sec*1000:+.0f} ms "
          f"(align {align_strength:.2f}), phase=+{phase} beat(s) "
          f"(+{phase * beat_interval * 1000:.0f} ms), total shift {total_shift_ms:+.0f} ms, "
          f"confidence={confidence:.2f}, snare_backbeat={snare_ok}, harmonic_stems={n_harm}")
    print(f"    simultaneous-chord-change score per beat-position: "
          f"{', '.join(f'b{p+1}={scores.get(p,0):.2f}' for p in range(time_sig))}")

    return {
        'phase': int(phase),
        'sub_beat_shift_ms': float(sub_beat_shift_sec * 1000.0),
        'align_strength': float(align_strength),
        'shift_ms': float(total_shift_ms),
        'corrected_anchor_sec': float(anchor),
        'confidence': confidence,
        'scores': scores,
        'snare_ok': bool(snare_ok),
        'method': method,
    }


def run_detect_downbeat(folder_path: str, time_sig: int = 4, grid_mode: str = None) -> Optional[Dict]:
    """Lightweight entry point: detect ONLY the musical downbeat phase.

    Fast — no Whisper, no LLM, no section detection. Classifies stems, builds the
    click-track beat grid (accurate spacing), then runs music-driven phase
    detection. Emits a small JSON result for the C# caller. `grid_mode`
    ('auto'|'even'|'following') forces how the grid is built.
    """
    print(f"\n{'='*60}")
    print(f"  DOWNBEAT DETECTION: {Path(folder_path).name}")
    print(f"{'='*60}\n")

    print("__PROGRESS__|5|Discovering audio files...")
    files = find_audio_files(folder_path)
    if not files:
        print("ERROR: No audio files found!", file=sys.stderr)
        return None

    classified = classify_tracks(files, demucs_quality='default')

    print("__PROGRESS__|30|Detecting beat grid from click track...")
    grid = detect_beat_grid(classified, files, grid_mode=grid_mode)
    if not grid:
        print("ERROR: Could not detect beat grid!", file=sys.stderr)
        return None
    if 'total_duration_sec' not in grid or grid['total_duration_sec'] <= 0:
        for f in files:
            try:
                grid['total_duration_sec'] = librosa.get_duration(path=f)
                break
            except Exception:
                continue

    print("__PROGRESS__|60|Analyzing drums, bass and chords for the downbeat...")
    result = detect_downbeat_phase(classified, grid, time_sig)

    # Apply the detected downbeat correction to the grid so we can emit the FINAL corrected beat
    # positions. This is what the click is rendered from, so the caller can use them directly
    # (keeps the chosen even/tempo-following grid intact; only shifts phase + sub-beat).
    sub_ms = result.get('sub_beat_shift_ms', 0.0)
    align = result.get('align_strength', 0.0)
    phase = int(result.get('phase', 0))
    apply_phase = phase != 0 and result.get('confidence', 0) >= 0.15
    apply_reanchor = abs(sub_ms) > 5.0 and align >= 0.20
    if apply_phase or apply_reanchor:
        _apply_downbeat_shift(grid, sub_ms / 1000.0 if apply_reanchor else 0.0)
        if apply_phase and grid.get('beats') and len(grid['beats']) > phase:
            grid['beats'] = grid['beats'][phase:]
        if grid.get('beats'):
            grid['grid_anchor_sec'] = grid['beats'][0]

    corrected_beats_ms = [round(b * 1000.0, 3) for b in grid.get('beats', [])]

    beat_interval_ms = grid['beat_interval_sec'] * 1000.0
    out = {
        'bpm': grid['bpm'],
        'beat_interval_ms': beat_interval_ms,
        'bar_duration_ms': beat_interval_ms * time_sig,
        'time_sig': time_sig,
        'original_anchor_ms': grid['grid_anchor_sec'] * 1000.0,
        'phase': result['phase'],
        'sub_beat_shift_ms': result.get('sub_beat_shift_ms', 0.0),
        'align_strength': result.get('align_strength', 0.0),
        'shift_ms': result['shift_ms'],
        'corrected_anchor_ms': (grid['beats'][0] * 1000.0) if grid.get('beats')
                               else result['corrected_anchor_sec'] * 1000.0,
        'confidence': result['confidence'],
        'snare_backbeat_ok': result['snare_ok'],
        'method': result['method'],
        'total_duration_ms': grid['total_duration_sec'] * 1000.0,
        # Grid-fit analysis so the UI can show the analyze-first dialog (verdict + metrics).
        'grid_fit': grid.get('grid_fit', {}),
        # FINAL corrected beat positions (ms) so the caller renders the click from the chosen grid.
        'corrected_beats_ms': corrected_beats_ms,
    }
    print("__PROGRESS__|100|Downbeat detection complete!")
    return out


# ═══════════════════════════════════════════════════════════════════════
# MAIN PIPELINE
# ═══════════════════════════════════════════════════════════════════════

def _apply_downbeat_shift(grid: Dict, sub_beat_shift_sec: float = 0.0) -> None:
    """Apply a downbeat correction to grid['beats'] WITHOUT destroying tempo-following.

    The beat grid is now the librosa dynamic-programming beat track — real positions that follow
    the song's tempo, so the click never drifts. A downbeat correction has two parts handled
    elsewhere: the PHASE (which existing beat is beat 1 — pure relabeling, no position change) and
    a small continuous SUB-BEAT nudge so beats sit exactly on the chord changes. Only the sub-beat
    nudge moves positions, and it moves EVERY beat by the same tiny amount, preserving the real
    inter-beat spacing (no drift reintroduced).

    If the grid holds a synthetic EVEN grid (the fallback path), we rebuild it evenly from the
    shifted anchor as before so behaviour is unchanged for that case.
    """
    interval = grid['beat_interval_sec']
    total = grid.get('total_duration_sec', 0) or 0
    beats = grid.get('beats') or []

    # Detect whether the current beats are tempo-following (variable spacing) or a synthetic even
    # grid. If variable, shift them in place; if even/empty, regenerate evenly from the anchor.
    is_even = True
    if len(beats) >= 3:
        import numpy as _np
        iv = _np.diff(_np.asarray(beats, dtype=float))
        is_even = bool(_np.std(iv) < 1e-3)

    if beats and not is_even:
        # Tempo-following: shift every real beat by the sub-beat nudge; keep spacing intact.
        grid['beats'] = [b + sub_beat_shift_sec for b in beats if 0.0 <= (b + sub_beat_shift_sec) <= total]
        if grid['beats']:
            grid['grid_anchor_sec'] = grid['beats'][0]
        return

    # Even-grid fallback: rebuild from the (already-shifted) anchor using the fixed interval.
    anchor = grid['grid_anchor_sec']
    if interval <= 0 or total <= 0:
        return
    new_beats = []
    t = anchor
    while t < total:
        new_beats.append(t)
        t += interval
    grid['beats'] = new_beats


# Backwards-compatible alias (older call sites rebuilt an even grid from the anchor).
def _rebuild_beats_from_anchor(grid: Dict) -> None:
    _apply_downbeat_shift(grid, 0.0)


def analyze_multitrack(folder_path: str, llm_mode: str = "openai",
                       time_sig: int = 4, override_bpm: float = None,
                       demucs_quality: str = 'default',
                       lines_per_slide: int = 4, slide_lead_ms: int = 500,
                       apply_downbeat_detection: bool = True,
                       anchor_override_ms: float = None,
                       grid_mode: str = None) -> Optional[Dict]:
    """Full multi-signal analysis pipeline. `grid_mode` ('auto'|'even'|'following') forces how the
    beat grid is built (shared with the Auto Generate Click path)."""
    print(f"\n{'='*60}")
    print(f"  MULTITRACK ANALYSIS: {Path(folder_path).name}")
    print(f"  LLM Mode: {llm_mode}")
    if override_bpm:
        print(f"  BPM Override: {override_bpm}")
    print(f"{'='*60}\n")

    # Discover and classify audio files
    print("__PROGRESS__|2|Discovering and classifying audio files...")
    files = find_audio_files(folder_path)
    if not files:
        print("ERROR: No audio files found!", file=sys.stderr)
        return None

    print(f"  Found {len(files)} audio files")

    # For single-track: run Demucs separation (progress shown within classify_tracks)
    if len(files) == 1:
        print("__PROGRESS__|3|Single track detected - running AI source separation (Demucs)...")

    classified = classify_tracks(files, demucs_quality=demucs_quality)

    for role, path in classified.items():
        if role != '_unclassified' and path:
            print(f"    {role:18s} → {Path(path).name}")
    if classified.get('_unclassified'):
        for f in classified['_unclassified']:
            print(f"    {'unclassified':18s} → {Path(f).name}")

    # Step 1: Beat grid detection (click track = source of truth for timing)
    grid = detect_beat_grid(classified, files, grid_mode=grid_mode)
    if not grid:
        print("ERROR: Could not detect BPM!", file=sys.stderr)
        return None

    # Get total duration if not set
    if 'total_duration_sec' not in grid or grid['total_duration_sec'] <= 0:
        for f in files:
            try:
                grid['total_duration_sec'] = librosa.get_duration(path=f)
                break
            except:
                continue

    # BPM OVERRIDE: Use user-specified BPM if provided
    if override_bpm and override_bpm > 0:
        print(f"  Applying BPM override: {grid['bpm']:.2f} → {override_bpm}")
        grid['bpm'] = override_bpm
        grid['beat_interval_sec'] = 60.0 / override_bpm
        # Rebuild beats from the new interval
        anchor = grid['grid_anchor_sec']
        total_dur = grid['total_duration_sec']
        new_beats = []
        t = anchor
        while t < total_dur:
            new_beats.append(t)
            t += grid['beat_interval_sec']
        grid['beats'] = new_beats
        grid['confidence'] = 1.0  # User-specified = maximum confidence
    else:
        # BPM SANITY CHECK: If detected BPM is unusually high, try halving/thirding
        detected_bpm = grid['bpm']
        if detected_bpm > 200:
            # Try common subdivisions
            candidates = [detected_bpm / 2, detected_bpm / 3, detected_bpm / 4]
            # Pick the candidate closest to typical worship range (60-160 BPM)
            best = detected_bpm
            best_dist = abs(detected_bpm - 110)  # 110 = center of typical range
            for c in candidates:
                dist = abs(c - 110)
                if dist < best_dist and c >= 50:
                    best = c
                    best_dist = dist
            if best != detected_bpm:
                print(f"  BPM sanity check: {detected_bpm:.2f} too high → adjusted to {best:.2f}")
                grid['bpm'] = best
                grid['beat_interval_sec'] = 60.0 / best
                # Rebuild beat grid from new interval
                anchor = grid['grid_anchor_sec']
                total_dur = grid['total_duration_sec']
                new_beats = []
                t = anchor
                while t < total_dur:
                    new_beats.append(t)
                    t += grid['beat_interval_sec']
                grid['beats'] = new_beats

    bar_duration_sec = grid['beat_interval_sec'] * time_sig

    # Step 2: Detect count-in (click with no other stems active)
    count_in_bars = detect_count_in(classified, grid, time_sig)
    if count_in_bars > 0:
        print(f"  Count-in detected: {count_in_bars} bar(s) — adjusting grid anchor")
        grid['grid_anchor_sec'] += count_in_bars * bar_duration_sec

    # Step 2b: (removed) Auto-Detect no longer generates a click track. The analyzer's old
    # generated click was on the wrong downbeat phase and too quiet. The click now comes ONLY
    # from the dedicated "Auto Generate Click Track" button (music-detected downbeat + loud,
    # compressed render) or the user's manual click. Beat-grid timing above is unaffected — it
    # is detected from the real click/librosa in Step 1.

    # Step 2c: CORRECT THE DOWNBEAT PHASE from the music (kick/bass/chords) BEFORE any bar-based
    # work. The click grid gives accurate beat SPACING, but its beat-1 phase can be wrong (the
    # first click pulse is not always the musical downbeat). Shifting the anchor here means section
    # boundaries land on REAL downbeats, so each section loops cleanly — and lyric-driven sections
    # snap to the correct bars. Auto-applied only when the detection is confident; otherwise the
    # original anchor is kept. An explicit override (anchor_override_ms) takes precedence and skips
    # detection (used when a prior "Create Multi-Track" already corrected the downbeat).
    if anchor_override_ms is not None and anchor_override_ms >= 0:
        _shift = anchor_override_ms / 1000.0 - grid['grid_anchor_sec']
        # Shift the real (tempo-following) beats by the override delta so spacing is preserved; the
        # even-grid fallback is rebuilt from the new anchor inside _apply_downbeat_shift.
        grid['grid_anchor_sec'] = anchor_override_ms / 1000.0
        _apply_downbeat_shift(grid, _shift)
        print(f"  Downbeat anchor override: {anchor_override_ms:.0f} ms "
              f"(shift {_shift*1000:+.0f} ms)")
    elif apply_downbeat_detection:
        try:
            db = detect_downbeat_phase(classified, grid, time_sig)
            # Apply the correction when it is meaningful. Two independent corrections exist:
            #   • a whole-beat phase shift (beat 1 lands on the right bar position), gated by
            #     phase-vote confidence; and
            #   • a continuous sub-beat re-anchor (beats land ON the chord changes), gated by how
            #     concentrated the chord-change energy was (align_strength).
            # Either alone is worth applying.
            sub_ms = db.get('sub_beat_shift_ms', 0.0)
            align = db.get('align_strength', 0.0)
            phase = int(db.get('phase', 0))
            apply_phase = phase != 0 and db['confidence'] >= 0.15
            apply_reanchor = abs(sub_ms) > 5.0 and align >= 0.20
            if apply_phase or apply_reanchor:
                # 1. Sub-beat nudge: move every real beat by the small amount so beats sit on the
                #    chord changes (preserves tempo-following spacing — no drift).
                _apply_downbeat_shift(grid, sub_ms / 1000.0 if apply_reanchor else 0.0)
                # 2. Phase: beat 1 = the real beat at index `phase`. This is pure relabeling of
                #    which beat starts the bar; it does NOT change beat spacing. We drop the leading
                #    `phase` beats so the grid starts on the chosen downbeat.
                if apply_phase and grid.get('beats') and len(grid['beats']) > phase:
                    grid['beats'] = grid['beats'][phase:]
                if grid.get('beats'):
                    grid['grid_anchor_sec'] = grid['beats'][0]
                print(f"  ✓ Downbeat corrected: re-anchor {sub_ms:+.0f} ms (align {align:.2f}), "
                      f"phase +{phase} beat(s), total {db['shift_ms']:+.0f} ms, "
                      f"confidence {db['confidence']:.2f}")
            else:
                print(f"  Downbeat kept as-is (phase={db['phase']}, re-anchor={sub_ms:+.0f} ms, "
                      f"align={align:.2f}, confidence={db['confidence']:.2f})")
        except Exception as e:
            print(f"  ! Downbeat detection skipped: {e}")

    # Step 3: Extract per-bar features FIRST (needed for all paths)
    features = extract_per_bar_features(classified, grid, time_sig)

    # Step 3b: Whisper transcription (cue + vocals)
    whisper_data = transcribe_with_whisper(classified, grid, time_sig)

    # Step 3c: Load section hints (from user-provided lyrics/chord chart)
    section_hints = None
    hints_path = os.path.join(folder_path, '_section_hints.json')
    if os.path.exists(hints_path):
        try:
            with open(hints_path, 'r', encoding='utf-8') as f:
                section_hints = json.loads(f.read())
            if section_hints and len(section_hints) > 0:
                print(f"  Section hints loaded: {section_hints}")
                print(f"  → LLM will use these {len(section_hints)} sections as constraints")
        except Exception as e:
            print(f"  ! Could not load section hints: {e}")
            section_hints = None

    # Step 3d: Load section lyrics (user-provided text per section)
    section_lyrics = None
    lyrics_path = os.path.join(folder_path, '_section_lyrics.json')
    if os.path.exists(lyrics_path):
        try:
            with open(lyrics_path, 'r', encoding='utf-8') as f:
                section_lyrics = json.loads(f.read())
        except Exception as e:
            print(f"  ! Could not load section lyrics: {e}")
            section_lyrics = None

    # Step 4: DETERMINE SECTION DETECTION PATH
    sections = None
    lyrics_already_assigned = False

    # PATH 0: LYRIC-DRIVEN (preferred when user gave exact sections + lyrics and
    # Whisper produced word timestamps). Anchors each section to where its words
    # are actually sung, so lyrics align to the vocals by construction.
    if section_hints and section_lyrics and whisper_data.get('word_timestamps'):
        print("\n  *** User sections + lyrics + Whisper words — using vocal-aligned pipeline ***")
        vocal_data = analyze_vocals(classified, grid, time_sig)
        lyric_sections = sections_from_lyrics_timing(
            section_hints, section_lyrics, whisper_data, grid, vocal_data, time_sig
        )
        if lyric_sections and len(lyric_sections) >= 2:
            sections = lyric_sections
            lyrics_already_assigned = True
            has_cue_track = False

    # PATH A: Cue track onset detection (timing-based, most reliable)
    # Skipped entirely if the lyric-driven path (PATH 0) already produced sections.
    if sections:
        has_cue_track = False
    else:
        cue_onsets = detect_cue_onsets(classified, grid, time_sig)
        if cue_onsets and len(cue_onsets) >= 3:
            print("\n  *** CUE TRACK ONSETS DETECTED — using timing-based pipeline ***")
            vocal_data = analyze_vocals(classified, grid, time_sig)
            sections = sections_from_cue_onsets(
                cue_onsets, grid, features, vocal_data, time_sig, llm_mode
            )
            has_cue_track = True
        elif whisper_data.get('cue_labels'):
            # PATH A2: Whisper transcribed cue labels (fallback)
            print("\n  *** CUE LABELS FROM WHISPER — using text-based pipeline ***")
            sections = sections_from_cue_track(
                whisper_data['cue_labels'], grid, time_sig, llm_mode
            )
            has_cue_track = True
        else:
            has_cue_track = False

    if not sections:
        # PATH B: NO CUE TRACK → Vocal-first + supporting signals
        print("\n  *** No cue track — using vocal-first pipeline ***")
        has_cue_track = False

        chords = detect_chords_per_bar(classified, grid, time_sig)
        vocal_data = analyze_vocals(classified, grid, time_sig)
        drum_fills = detect_drum_fills(classified, grid, time_sig)

        # Try vocal melody repetition for section grouping
        melody_groups = detect_melody_repetition(classified, grid, time_sig)

        # Build LLM prompt with vocal-focused data
        prompt = assemble_llm_prompt(
            grid, features, chords, vocal_data, whisper_data,
            drum_fills, "", time_sig,
            melody_groups=melody_groups,
            section_hints=section_hints
        )

        # Save prompt for debugging
        prompt_path = os.path.join(folder_path, '_analysis_prompt.txt')
        with open(prompt_path, 'w', encoding='utf-8') as f:
            f.write(prompt)
        print(f"  Prompt saved to: {prompt_path}")

        # Call LLM
        sections = call_llm(prompt, llm_mode)

        # Fallback to heuristic if LLM unavailable
        if sections is None:
            sections = heuristic_section_detection(
                grid, features, vocal_data, drum_fills, chords, time_sig
            )

    if not sections:
        print("ERROR: No sections detected!", file=sys.stderr)
        return None

    # Step 5: Snap all section boundaries to bar downbeats (using click grid)
    sections = snap_sections_to_grid(sections, grid, time_sig)

    # Step 5b: LYRICS ASSIGNMENT (Two-Step Approach)
    # Step 1 (already done above): LLM/heuristic found accurate MUSICAL boundaries for looping
    # Step 2 (here): Assign lyrics to sections BY CONTENT — match what's being sung
    # at each section to the correct lyrics block from user-provided text.
    #
    # This ensures: boundaries are musically accurate (loops) AND lyrics match content.
    # If the lyric-driven pipeline (PATH 0) already built vocal-aligned sections with
    # text attached, skip this step — the lyrics are already correctly placed.
    if lyrics_already_assigned:
        print("  Lyrics already aligned by the vocal-aligned pipeline — skipping reassignment")
    elif section_lyrics and section_hints:
        try:
            print("__PROGRESS__|82|Assigning lyrics to sections...")
            has_whisper_words = bool(whisper_data.get('word_timestamps'))
            if has_whisper_words:
                # Best path: content matching using Whisper word timestamps
                sections = assign_lyrics_by_content(
                    sections, section_hints, section_lyrics, whisper_data, grid, time_sig
                )
            else:
                # Fallback: assign by name matching (no Whisper available)
                print("    (No Whisper timestamps — using name-based assignment)")
                sections = _assign_lyrics_by_order(sections, section_hints, section_lyrics)
        except Exception as e:
            print(f"  ! Lyrics assignment failed: {e}")

    # Step 6: Verify boundaries against drum onsets (fine-tune if needed)
    if not has_cue_track:
        sections = verify_boundaries_with_drums(sections, classified, grid, time_sig)

    # Step 6b: AI VERIFICATION — report only (no auto-fix, detection should be correct first time)
    print("__PROGRESS__|85|Generating verification report...")
    # Score sections for reporting but do NOT modify boundaries
    _generate_score_report(sections, grid, classified, whisper_data, time_sig, folder_path)

    # Step 6c: Detect vocal pickups (for per-track loop points)
    print("__PROGRESS__|88|Detecting vocal pickups for seamless loops...")
    sections = detect_vocal_pickups(classified, grid, sections, time_sig)

    # Step 7: Key detection
    key = detect_key(classified)
    print(f"  Key: {key}")

    # Step 8: Chord detection (for chord chart)
    # Reuse chords computed in the vocal-first path if available; otherwise detect
    # now. (The lyric-driven and cue-track paths don't populate `chords`.)
    chords_for_chart = chords if ('chords' in locals() and chords) else detect_chords_per_bar(classified, grid, time_sig)

    # Step 9: Build chord chart data (honors configured lines-per-slide + read-ahead lead)
    chord_chart = build_chord_chart(grid, sections, chords_for_chart, whisper_data, time_sig,
                                    lines_per_slide=lines_per_slide, slide_lead_ms=slide_lead_ms)
    chart_slides = len(chord_chart.get('slides', []))
    if chart_slides > 0:
        print(f"  Chord chart: {chart_slides} slides generated")

    # Build final output
    song_data = build_song_json(
        grid, sections, classified, key, files, folder_path, time_sig,
        chord_chart=chord_chart
    )

    # Print summary
    print("__PROGRESS__|100|Analysis complete!")
    print(f"\n{'='*60}")
    print(f"  ANALYSIS COMPLETE")
    print(f"{'='*60}")
    bar_duration_sec_final = grid['beat_interval_sec'] * time_sig
    total_bars_final = int((grid['total_duration_sec'] - grid['grid_anchor_sec']) / bar_duration_sec_final)

    print(f"  BPM: {grid['bpm']:.2f} | Key: {key} | Bars: {total_bars_final}")
    print(f"  Sections ({len(sections)}):")
    for sec in song_data['sections']:
        dur_ms = sec['loopOut_ms'] - sec['loopIn_ms']
        print(f"    {sec['name']:<20} Bars {sec['startBar']:>2}-{sec['endBar']:<3}"
              f" | {dur_ms/1000:.1f}s")
    print(f"{'='*60}\n")

    return song_data



# ═══════════════════════════════════════════════════════════════════════
# ENTRY POINT
# ═══════════════════════════════════════════════════════════════════════

def main():
    if len(sys.argv) < 2:
        print("Usage: python analyze_multitrack.py <song_folder_path> "
              "[--llm azure|openai|ollama|none] [--output filename.json] "
              "[--bpm VALUE] [--quality fast|default|best]",
              file=sys.stderr)
        sys.exit(1)

    folder_path = sys.argv[1]
    output_file = 'song.json'
    llm_mode = 'azure'  # Default to Azure OpenAI
    override_bpm = None
    demucs_quality = 'default'  # htdemucs_6s, shifts=2
    lines_per_slide = 4         # slide grouping: lyric lines per slide (clamped 2-4)
    slide_lead_ms = 500         # slide read-ahead lead (subtracted from each slide start)
    apply_downbeat_detection = True  # correct the musical downbeat before dividing sections
    anchor_override_ms = None        # explicit corrected anchor (ms); skips detection when set
    grid_mode = None                 # 'auto'|'even'|'following' — force how the grid is built

    # --grid-mode controls the even-vs-tempo-following decision (shared by Create Multi-Track and
    # Auto Generate Click). Parsed early so it applies to both the --detect-downbeat and full paths.
    if '--grid-mode' in sys.argv:
        _gi = sys.argv.index('--grid-mode')
        if _gi + 1 < len(sys.argv):
            _gm = sys.argv[_gi + 1].lower()
            if _gm in ('auto', 'even', 'following'):
                grid_mode = _gm
                print(f"  Grid mode: {grid_mode}")
            else:
                print(f"WARNING: Invalid --grid-mode '{_gm}'. Options: auto, even, following",
                      file=sys.stderr)

    # ── LIGHTWEIGHT MODE: detect the musical downbeat only (no Whisper/LLM/sections) ──
    # Used by the "Auto Generate Click Track" button. Emits a small JSON result.
    if '--detect-downbeat' in sys.argv:
        if not os.path.isdir(folder_path):
            print(f"ERROR: Folder not found: {folder_path}", file=sys.stderr)
            sys.exit(1)
        try:
            db = run_detect_downbeat(folder_path, time_sig=4, grid_mode=grid_mode)
        except Exception as e:
            print(f"ERROR: Unhandled exception: {e}", file=sys.stderr)
            import traceback
            traceback.print_exc(file=sys.stderr)
            sys.exit(1)
        if db is None:
            print("ERROR: Downbeat detection returned no result.", file=sys.stderr)
            sys.exit(1)
        print(f"\n__RESULT_JSON__")
        print(json.dumps(db))
        sys.exit(0)

    # Parse optional arguments
    if '--output' in sys.argv:
        idx = sys.argv.index('--output')
        if idx + 1 < len(sys.argv):
            output_file = sys.argv[idx + 1]

    if '--llm' in sys.argv:
        idx = sys.argv.index('--llm')
        if idx + 1 < len(sys.argv):
            llm_mode = sys.argv[idx + 1].lower()

    if '--bpm' in sys.argv:
        idx = sys.argv.index('--bpm')
        if idx + 1 < len(sys.argv):
            try:
                override_bpm = float(sys.argv[idx + 1])
                print(f"  BPM override: {override_bpm}")
            except ValueError:
                print(f"WARNING: Invalid --bpm value, ignoring", file=sys.stderr)

    if '--quality' in sys.argv:
        idx = sys.argv.index('--quality')
        if idx + 1 < len(sys.argv):
            q = sys.argv[idx + 1].lower()
            if q in DEMUCS_QUALITY_PRESETS:
                demucs_quality = q
                print(f"  Demucs quality: {q} ({DEMUCS_QUALITY_PRESETS[q]['description']})")
            else:
                print(f"WARNING: Invalid --quality '{q}'. Options: fast, default, best",
                      file=sys.stderr)

    if '--lines-per-slide' in sys.argv:
        idx = sys.argv.index('--lines-per-slide')
        if idx + 1 < len(sys.argv):
            try:
                lines_per_slide = max(2, min(4, int(sys.argv[idx + 1])))
                print(f"  Lines per slide: {lines_per_slide}")
            except ValueError:
                print(f"WARNING: Invalid --lines-per-slide value, using 4", file=sys.stderr)

    if '--slide-lead-ms' in sys.argv:
        idx = sys.argv.index('--slide-lead-ms')
        if idx + 1 < len(sys.argv):
            try:
                slide_lead_ms = max(0, int(sys.argv[idx + 1]))
                print(f"  Slide lead: {slide_lead_ms} ms")
            except ValueError:
                print(f"WARNING: Invalid --slide-lead-ms value, using 500", file=sys.stderr)

    # Downbeat correction controls:
    #   --no-downbeat-detect   disable music-driven downbeat correction (keep the click's phase)
    #   --anchor-ms VALUE      force the corrected beat-1 anchor (ms); skips detection
    if '--no-downbeat-detect' in sys.argv:
        apply_downbeat_detection = False
        print("  Downbeat detection: disabled (keeping click-track phase)")

    if '--anchor-ms' in sys.argv:
        idx = sys.argv.index('--anchor-ms')
        if idx + 1 < len(sys.argv):
            try:
                anchor_override_ms = max(0.0, float(sys.argv[idx + 1]))
                print(f"  Downbeat anchor override: {anchor_override_ms:.1f} ms")
            except ValueError:
                print(f"WARNING: Invalid --anchor-ms value, ignoring", file=sys.stderr)

    if not os.path.isdir(folder_path):
        print(f"ERROR: Folder not found: {folder_path}", file=sys.stderr)
        sys.exit(1)

    # Run analysis
    try:
        song_data = analyze_multitrack(folder_path, llm_mode=llm_mode,
                                       override_bpm=override_bpm,
                                       demucs_quality=demucs_quality,
                                       lines_per_slide=lines_per_slide,
                                       slide_lead_ms=slide_lead_ms,
                                       apply_downbeat_detection=apply_downbeat_detection,
                                       anchor_override_ms=anchor_override_ms,
                                       grid_mode=grid_mode)
    except Exception as e:
        print(f"ERROR: Unhandled exception: {e}", file=sys.stderr)
        import traceback
        traceback.print_exc(file=sys.stderr)
        sys.exit(1)

    if song_data:
        # song.json (backup) is written directly in the song folder. Demucs now writes stems into
        # this same folder (no subfolder), so single-track and multi-track songs both save here.
        output_path = os.path.join(folder_path, output_file)

        with open(output_path, 'w', encoding='utf-8') as f:
            json.dump(song_data, f, indent=2, ensure_ascii=False)

        print(f"  Saved: {output_path}")

        # Output JSON marker for C# to capture
        print(f"\n__RESULT_JSON__")
        print(json.dumps(song_data))
    else:
        print("ERROR: Analysis completed but returned no results.", file=sys.stderr)
        sys.exit(1)


if __name__ == '__main__':
    main()
