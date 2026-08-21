#!/usr/bin/env python3
"""
analyze_song.py — Audio analysis for Worship Backtrack Player
Uses allin1 (AI model) for section detection + librosa for beat/BPM.
All loop points are automatically aligned to the detected beat grid.

Usage: python analyze_song.py "path/to/song/folder" [--output song.json]

Outputs a song.json with:
- Exact BPM and beat positions
- Downbeat (beat 1) positions
- Section boundaries with labels (intro, verse, chorus, bridge, outro)
- All loop points snapped to bar downbeats
- Key detection
"""

import sys
import os
import json

try:
    import numpy as np
    import librosa
    from scipy.signal import find_peaks
    from scipy.spatial.distance import cdist
except ImportError as e:
    print(f"ERROR: Missing required Python package: {e}\nInstall with: pip install librosa numpy scipy", file=sys.stderr)
    sys.exit(1)

from pathlib import Path

# ═══════════════════════════════════════════════════════════════════════
# AUDIO FILE DISCOVERY
# ═══════════════════════════════════════════════════════════════════════

AUDIO_EXTENSIONS = {'.mp3', '.wav', '.flac', '.ogg', '.m4a', '.aac', '.aiff'}

def find_audio_files(folder):
    """Find all audio files in a folder."""
    files = []
    for f in sorted(Path(folder).iterdir()):
        if f.suffix.lower() in AUDIO_EXTENSIONS:
            files.append(str(f))
    return files

def find_track_by_keyword(files, keyword):
    """Find a track by keyword in filename."""
    keyword = keyword.lower()
    for f in files:
        name = Path(f).stem.lower()
        if keyword in name:
            return f
    return None

def guess_track_label(filename):
    """Guess a human-readable label from filename."""
    import re
    name = Path(filename).stem
    cleaned = re.sub(r'^\d+[_\-\s]*', '', name)
    labels = {
        'click': 'Click', 'cue': 'Cues', 'drum': 'Drums',
        'bass': 'Bass', 'acousticguitar': 'Acoustic Guitar',
        'electricguitar': 'Electric Guitar', 'guitar': 'Guitar',
        'piano': 'Piano', 'keys': 'Keys', 'synth': 'Synth',
        'pad': 'Pad', 'leadvocal': 'Lead Vocal', 'vocal': 'Vocal',
        'bgv': 'Backing Vocals', 'strings': 'Strings', 'organ': 'Organ',
    }
    for key, label in labels.items():
        if key in cleaned.lower().replace(' ', '').replace('_', ''):
            return label
    return cleaned if cleaned else name



# ═══════════════════════════════════════════════════════════════════════
# SECTION DETECTION USING allin1 (AI Model)
# ═══════════════════════════════════════════════════════════════════════

def analyze_with_allin1(audio_path):
    """
    Use allin1 AI model to detect:
    - BPM, beats, downbeats
    - Section boundaries with functional labels (intro, verse, chorus, bridge, outro)
    
    Returns dict with all analysis results.
    """
    print(f"  Running allin1 AI analysis on: {Path(audio_path).name}")
    
    try:
        import allin1
    except ImportError as e:
        raise ImportError(f"allin1 package not fully functional: {e}\nTry: pip install allin1 madmom")
    
    result = allin1.analyze(audio_path)
    
    # Extract BPM
    bpm = float(result.bpm) if result.bpm else 0
    
    # Extract beat positions (in seconds)
    beats = [float(b) for b in result.beats] if result.beats else []
    
    # Extract downbeats (beat 1 positions)
    downbeats = [float(d) for d in result.downbeats] if result.downbeats else []
    
    # Extract segments with labels
    segments = []
    if result.segments:
        for seg in result.segments:
            segments.append({
                'start': float(seg.start),
                'end': float(seg.end),
                'label': str(seg.label).lower()
            })
    
    print(f"    BPM: {bpm:.2f}")
    print(f"    Beats: {len(beats)}")
    print(f"    Downbeats: {len(downbeats)}")
    print(f"    Sections: {len(segments)}")
    for seg in segments:
        dur = seg['end'] - seg['start']
        print(f"      {seg['label']:<12} {seg['start']:.2f}s - {seg['end']:.2f}s ({dur:.1f}s)")
    
    return {
        'bpm': bpm,
        'beats': beats,
        'downbeats': downbeats,
        'segments': segments
    }



# ═══════════════════════════════════════════════════════════════════════
# SECTION DETECTION FALLBACK (librosa-only, no allin1/madmom needed)
# ═══════════════════════════════════════════════════════════════════════

def detect_sections_librosa(audio_path, bpm, beat_interval_sec, grid_anchor_sec, sr=22050):
    """
    Detect song sections using librosa's spectral analysis.
    Uses Self-Similarity Matrix (SSM) and novelty detection to find
    structural boundaries. Less accurate than allin1 but no extra deps needed.
    """
    print(f"  Detecting sections with librosa (SSM novelty)...")
    
    y, sr = librosa.load(audio_path, sr=sr)
    total_duration = librosa.get_duration(y=y, sr=sr)
    bar_duration_sec = beat_interval_sec * 4
    
    # Compute chroma features for structural analysis
    hop_length = 512
    chroma = librosa.feature.chroma_cqt(y=y, sr=sr, hop_length=hop_length)
    
    # Compute MFCCs for timbral analysis
    mfcc = librosa.feature.mfcc(y=y, sr=sr, n_mfcc=13, hop_length=hop_length)
    
    # Combine features
    features = np.vstack([
        librosa.util.normalize(chroma, axis=1),
        librosa.util.normalize(mfcc, axis=1)
    ])
    
    # Compute self-similarity matrix
    ssm = 1 - cdist(features.T, features.T, metric='cosine')
    
    # Compute novelty curve from the SSM using a checkerboard kernel
    kernel_size = int(bar_duration_sec * sr / hop_length * 2)  # ~2 bars
    if kernel_size < 4:
        kernel_size = 64
    if kernel_size % 2 != 0:
        kernel_size += 1
    
    # Simple novelty: measure change along the diagonal
    novelty = np.zeros(ssm.shape[0])
    half_k = min(kernel_size // 2, ssm.shape[0] // 4)
    for i in range(half_k, ssm.shape[0] - half_k):
        before = ssm[i - half_k:i, i - half_k:i]
        after = ssm[i:i + half_k, i:i + half_k]
        cross = ssm[i - half_k:i, i:i + half_k]
        novelty[i] = np.mean(before) + np.mean(after) - 2 * np.mean(cross)
    
    # Normalize novelty
    if np.max(novelty) > 0:
        novelty = novelty / np.max(novelty)
    
    # Find peaks in novelty curve (section boundaries)
    # Minimum distance between sections: 4 bars
    min_dist_frames = int(4 * bar_duration_sec * sr / hop_length)
    peaks, properties = find_peaks(novelty, height=0.15, distance=min_dist_frames, prominence=0.1)
    
    # Convert frame indices to seconds
    boundary_times = librosa.frames_to_time(peaks, sr=sr, hop_length=hop_length)
    
    # Snap boundaries to bar grid
    snapped_boundaries = [grid_anchor_sec]  # Always start at grid anchor
    for bt in boundary_times:
        elapsed = bt - grid_anchor_sec
        if elapsed <= 0:
            continue
        bar_num = round(elapsed / bar_duration_sec)
        snapped = grid_anchor_sec + bar_num * bar_duration_sec
        if snapped > snapped_boundaries[-1] + bar_duration_sec * 2:  # Min 2 bars apart
            snapped_boundaries.append(snapped)
    
    # Add end
    snapped_boundaries.append(total_duration)
    
    # Limit to reasonable number of sections (5-12)
    if len(snapped_boundaries) > 14:
        # Keep only the strongest peaks
        if len(peaks) > 12:
            sorted_idx = np.argsort(novelty[peaks])[::-1][:11]
            sorted_idx = np.sort(sorted_idx)
            peaks = peaks[sorted_idx]
            boundary_times = librosa.frames_to_time(peaks, sr=sr, hop_length=hop_length)
            snapped_boundaries = [grid_anchor_sec]
            for bt in boundary_times:
                elapsed = bt - grid_anchor_sec
                if elapsed <= 0:
                    continue
                bar_num = round(elapsed / bar_duration_sec)
                snapped = grid_anchor_sec + bar_num * bar_duration_sec
                if snapped > snapped_boundaries[-1] + bar_duration_sec * 2:
                    snapped_boundaries.append(snapped)
            snapped_boundaries.append(total_duration)
    
    # Build segments with heuristic labels
    segments = []
    total_bars = int((total_duration - grid_anchor_sec) / bar_duration_sec)
    
    for i in range(len(snapped_boundaries) - 1):
        start = snapped_boundaries[i]
        end = snapped_boundaries[i + 1]
        
        # Heuristic labeling based on position in song
        position_ratio = start / total_duration
        duration_bars = round((end - start) / bar_duration_sec)
        
        if i == 0 and duration_bars <= 8:
            label = 'intro'
        elif i == len(snapped_boundaries) - 2 and duration_bars <= 8:
            label = 'outro'
        elif position_ratio < 0.3:
            label = 'verse'
        elif position_ratio < 0.6:
            label = 'chorus'
        elif position_ratio < 0.8:
            label = 'bridge'
        else:
            label = 'chorus'
        
        segments.append({
            'start': start,
            'end': end,
            'label': label
        })
    
    print(f"    Found {len(segments)} sections")
    for seg in segments:
        dur = seg['end'] - seg['start']
        print(f"      {seg['label']:<12} {seg['start']:.2f}s - {seg['end']:.2f}s ({dur:.1f}s)")
    
    return {
        'bpm': bpm,
        'beats': [],
        'downbeats': [],
        'segments': segments
    }


# ═══════════════════════════════════════════════════════════════════════
# CLICK TRACK ANALYSIS (for highest BPM accuracy)
# ═══════════════════════════════════════════════════════════════════════

def analyze_click_track(click_path, sr=44100):
    """Analyze click track for exact BPM and beat positions."""
    print(f"  Analyzing click track: {Path(click_path).name}")
    y, sr = librosa.load(click_path, sr=sr)
    
    onset_env = librosa.onset.onset_strength(y=y, sr=sr, hop_length=128)
    onset_times = librosa.times_like(onset_env, sr=sr, hop_length=128)
    
    peak_height = np.max(onset_env) * 0.3
    min_distance = int(sr * 0.3 / 128)
    peaks, _ = find_peaks(onset_env, height=peak_height, distance=min_distance)
    click_times = onset_times[peaks]
    
    if len(click_times) < 4:
        return None
    
    intervals = np.diff(click_times)
    median_interval = np.median(intervals)
    
    # Skip count-in if first interval is different
    clean_mask = np.abs(intervals - median_interval) / median_interval < 0.15
    first_good = 0
    if len(intervals) > 0 and not clean_mask[0]:
        first_good = 1
    
    clean_clicks = click_times[first_good:]
    clean_intervals = np.diff(clean_clicks)
    if len(clean_intervals) < 3:
        clean_clicks = click_times
        clean_intervals = intervals
    
    beat_interval = float(np.median(clean_intervals))
    bpm = 60.0 / beat_interval
    grid_anchor = float(clean_clicks[0])
    
    confidence = max(0, 1.0 - (float(np.std(clean_intervals)) / beat_interval) * 10)
    
    print(f"    Click BPM: {bpm:.4f} (confidence: {confidence:.0%})")
    print(f"    Grid anchor: {grid_anchor*1000:.3f} ms")
    
    return {
        'bpm': bpm,
        'beat_interval_sec': beat_interval,
        'grid_anchor_sec': grid_anchor,
        'beats': clean_clicks.tolist(),
        'confidence': confidence
    }



# ═══════════════════════════════════════════════════════════════════════
# KEY DETECTION
# ═══════════════════════════════════════════════════════════════════════

def detect_key(audio_path, sr=22050):
    """Detect musical key from audio."""
    print("  Detecting key...")
    y, sr = librosa.load(audio_path, sr=sr, duration=120)
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
    
    print(f"    Key: {best_key}")
    return best_key



# ═══════════════════════════════════════════════════════════════════════
# SNAP SECTIONS TO BAR GRID
# ═══════════════════════════════════════════════════════════════════════

def snap_sections_to_grid(segments, bpm, grid_anchor_sec, beat_interval_sec):
    """
    Snap allin1's section boundaries to exact bar downbeats.
    This ensures seamless looping.
    """
    bar_duration_sec = beat_interval_sec * 4
    
    sections = []
    verse_count = 0
    chorus_count = 0
    bridge_count = 0
    
    for seg in segments:
        start_sec = seg['start']
        end_sec = seg['end']
        label = seg['label']
        
        # Snap start to nearest bar
        elapsed_start = start_sec - grid_anchor_sec
        start_bar = max(1, round(elapsed_start / bar_duration_sec) + 1)
        snapped_start_sec = grid_anchor_sec + (start_bar - 1) * bar_duration_sec
        
        # Snap end to nearest bar
        elapsed_end = end_sec - grid_anchor_sec
        end_bar = max(start_bar, round(elapsed_end / bar_duration_sec))
        snapped_end_sec = grid_anchor_sec + end_bar * bar_duration_sec
        
        # Ensure minimum 2 bars
        if end_bar - start_bar < 1:
            end_bar = start_bar + 1
            snapped_end_sec = grid_anchor_sec + end_bar * bar_duration_sec
        
        # Generate proper label name
        name, color = format_section_label(label, verse_count, chorus_count, bridge_count)
        
        # Track counts for numbering
        if 'verse' in label:
            verse_count += 1
        elif 'chorus' in label:
            chorus_count += 1
        elif 'bridge' in label:
            bridge_count += 1
        
        loop_in_ms = snapped_start_sec * 1000
        loop_out_ms = snapped_end_sec * 1000
        
        sections.append({
            'name': name,
            'loopIn_ms': round(loop_in_ms, 3),
            'loopOut_ms': round(loop_out_ms, 3),
            'startBar': start_bar,
            'endBar': end_bar,
            'color': color,
            'lightingPresetIndex': len(sections)
        })
    
    return sections


def format_section_label(label, verse_count, chorus_count, bridge_count):
    """Format a section label with proper numbering and color."""
    label = label.lower().strip()
    
    colors = {
        'intro': '#4A90D9',
        'verse': '#7ED321',
        'chorus': '#E74C3C',
        'pre-chorus': '#F5A623',
        'bridge': '#9B59B6',
        'outro': '#4A90D9',
        'instrumental': '#50E3C2',
        'silence': '#6C6C80',
    }
    
    if 'intro' in label:
        return 'INTRO', colors['intro']
    elif 'outro' in label:
        return 'OUTRO', colors['outro']
    elif 'chorus' in label:
        num = chorus_count + 1
        name = f'CHORUS {num}' if num > 1 else 'CHORUS 1'
        return name, colors['chorus']
    elif 'verse' in label:
        num = verse_count + 1
        name = f'VERSE {num}' if num > 1 else 'VERSE 1'
        return name, colors['verse']
    elif 'bridge' in label:
        num = bridge_count + 1
        name = f'BRIDGE {num}' if num > 1 else 'BRIDGE'
        return name, colors['bridge']
    elif 'pre' in label:
        return 'PRE-CHORUS', colors['pre-chorus']
    elif 'instrument' in label:
        return 'INSTRUMENTAL', colors['instrumental']
    elif 'silence' in label:
        return 'SILENCE', colors['silence']
    else:
        return label.upper(), '#4A90D9'



# ═══════════════════════════════════════════════════════════════════════
# MAIN ANALYSIS PIPELINE
# ═══════════════════════════════════════════════════════════════════════

def analyze_song(folder_path):
    """Full song analysis pipeline using allin1 AI model."""
    print(f"\n{'='*60}")
    print(f"  ANALYZING: {Path(folder_path).name}")
    print(f"{'='*60}\n")
    
    # Find audio files
    audio_files = find_audio_files(folder_path)
    if not audio_files:
        print("ERROR: No audio files found!", file=sys.stderr)
        return None
    
    print(f"  Found {len(audio_files)} audio files")
    
    # Step 1: Get BPM from click track (most accurate) or allin1
    click_file = find_track_by_keyword(audio_files, 'click')
    click_info = None
    if click_file:
        click_info = analyze_click_track(click_file)
    
    # Step 2: Run allin1 on a representative track (drums or first non-click)
    analysis_file = find_track_by_keyword(audio_files, 'drum')
    if not analysis_file:
        # Use first non-click file
        for f in audio_files:
            if 'click' not in Path(f).stem.lower() and 'cue' not in Path(f).stem.lower():
                analysis_file = f
                break
    if not analysis_file:
        analysis_file = audio_files[0]
    
    # Run allin1 AI analysis
    ai_result = None
    try:
        ai_result = analyze_with_allin1(analysis_file)
    except Exception as e:
        print(f"  allin1 analysis failed: {e}", file=sys.stderr)
        print(f"  Falling back to librosa-only section detection...", file=sys.stderr)
    
    # If allin1 failed or returned no segments, use librosa fallback
    use_librosa_fallback = (ai_result is None or len(ai_result.get('segments', [])) == 0)
    
    if use_librosa_fallback:
        # First we need BPM — get it from click track or librosa
        if click_info and click_info['confidence'] > 0.5:
            bpm = click_info['bpm']
            beat_interval_sec = click_info['beat_interval_sec']
            grid_anchor_sec = click_info['grid_anchor_sec']
        else:
            # Detect BPM with librosa
            print("  Detecting BPM with librosa...")
            y_bpm, sr_bpm = librosa.load(analysis_file, sr=22050)
            tempo, _ = librosa.beat.beat_track(y=y_bpm, sr=sr_bpm)
            bpm = float(tempo) if not hasattr(tempo, '__len__') else float(tempo[0])
            beat_interval_sec = 60.0 / bpm
            # Find first beat onset as grid anchor
            onset_env = librosa.onset.onset_strength(y=y_bpm, sr=sr_bpm)
            tempo_est, beats = librosa.beat.beat_track(y=y_bpm, sr=sr_bpm)
            beat_times = librosa.frames_to_time(beats, sr=sr_bpm)
            grid_anchor_sec = float(beat_times[0]) if len(beat_times) > 0 else 0.0
            print(f"    Librosa BPM: {bpm:.2f}, Grid anchor: {grid_anchor_sec*1000:.1f} ms")
        
        # Now detect sections with librosa
        ai_result = detect_sections_librosa(
            analysis_file, bpm, beat_interval_sec, grid_anchor_sec
        )
        
        if ai_result is None or len(ai_result.get('segments', [])) == 0:
            print("ERROR: Section detection returned no results", file=sys.stderr)
            return None
    
    # Step 3: Determine best BPM source
    if click_info and click_info['confidence'] > 0.8:
        # Click track is most accurate
        bpm = click_info['bpm']
        beat_interval_sec = click_info['beat_interval_sec']
        grid_anchor_sec = click_info['grid_anchor_sec']
        print(f"\n  Using BPM from click track: {bpm:.4f}")
    elif ai_result['bpm'] > 0:
        # Use allin1 BPM
        bpm = ai_result['bpm']
        beat_interval_sec = 60.0 / bpm
        # Use first downbeat as grid anchor
        if ai_result['downbeats']:
            grid_anchor_sec = ai_result['downbeats'][0]
        elif ai_result['beats']:
            grid_anchor_sec = ai_result['beats'][0]
        else:
            grid_anchor_sec = 0
        print(f"\n  Using BPM from allin1: {bpm:.4f}")
    else:
        print("ERROR: Could not determine BPM", file=sys.stderr)
        return None
    
    bar_duration_sec = beat_interval_sec * 4
    
    # Step 4: Get total duration
    y_temp, sr_temp = librosa.load(analysis_file, sr=22050, duration=0.1)
    total_duration_sec = librosa.get_duration(path=analysis_file)
    total_bars = int((total_duration_sec - grid_anchor_sec) / bar_duration_sec)
    
    # Step 5: Snap AI sections to bar grid
    sections = snap_sections_to_grid(
        ai_result['segments'], bpm, grid_anchor_sec, beat_interval_sec
    )
    
    # Step 6: Key detection
    key_file = find_track_by_keyword(audio_files, 'piano')
    if not key_file:
        key_file = find_track_by_keyword(audio_files, 'keys')
    if not key_file:
        key_file = find_track_by_keyword(audio_files, 'vocal')
    if not key_file:
        key_file = analysis_file
    key = detect_key(key_file)
    
    # Step 7: Build track list
    tracks = []
    for f in audio_files:
        label = guess_track_label(f)
        output = 'IEM' if 'click' in label.lower() or 'cue' in label.lower() else 'FOH'
        tracks.append({
            'file': Path(f).name,
            'label': label,
            'output': output,
            'defaultVolume': 0.8 if output == 'IEM' else 1.0,
            'defaultMute': False,
            'color': '#FF6B6B' if output == 'IEM' else '#4A90D9'
        })
    
    # Build song.json
    song_data = {
        'title': Path(folder_path).name,
        'artist': '',
        'bpm': round(bpm, 4),
        'timeSignatureNumerator': 4,
        'timeSignatureDenominator': 4,
        'beatInterval_ms': round(beat_interval_sec * 1000, 3),
        'barDuration_ms': round(bar_duration_sec * 1000, 3),
        'gridAnchor_ms': round(grid_anchor_sec * 1000, 3),
        'originalKey': key,
        'tracks': tracks,
        'sections': sections,
        'lightingPresets': []
    }
    
    # Print summary
    print(f"\n{'='*60}")
    print(f"  ANALYSIS COMPLETE")
    print(f"{'='*60}")
    print(f"  BPM: {bpm:.4f}")
    print(f"  Key: {key}")
    print(f"  Total Bars: {total_bars}")
    print(f"  Sections: {len(sections)}")
    for sec in sections:
        dur_ms = sec['loopOut_ms'] - sec['loopIn_ms']
        print(f"    {sec['name']:<20} Bars {sec['startBar']:>2}-{sec['endBar']:<3} | {dur_ms/1000:.1f}s")
    print(f"{'='*60}\n")
    
    return song_data



# ═══════════════════════════════════════════════════════════════════════
# ENTRY POINT
# ═══════════════════════════════════════════════════════════════════════

def main():
    if len(sys.argv) < 2:
        print("Usage: python analyze_song.py <song_folder_path> [--output filename.json]", file=sys.stderr)
        sys.exit(1)
    
    folder_path = sys.argv[1]
    output_file = 'song.json'
    
    if '--output' in sys.argv:
        idx = sys.argv.index('--output')
        if idx + 1 < len(sys.argv):
            output_file = sys.argv[idx + 1]
    
    if not os.path.isdir(folder_path):
        print(f"ERROR: Folder not found: {folder_path}", file=sys.stderr)
        sys.exit(1)
    
    # Run analysis
    try:
        song_data = analyze_song(folder_path)
    except Exception as e:
        print(f"ERROR: Unhandled exception during analysis: {e}", file=sys.stderr)
        sys.exit(1)
    
    if song_data:
        # Write song.json
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
