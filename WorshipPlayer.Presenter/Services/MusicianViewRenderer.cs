using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Presenter.Services;

/// <summary>
/// Renders personalized chord/lyric text for individual musicians based on their preferences.
/// Handles capo transposition, Nashville number conversion, and pitch shift adjustment.
/// Each musician connects with their own URL parameters to see chords tailored to their instrument.
/// </summary>
public class MusicianViewRenderer
{
    private readonly ChordProParserService _chordProParser;

    public MusicianViewRenderer(ChordProParserService chordProParser)
    {
        _chordProParser = chordProParser;
    }

    /// <summary>
    /// Render text for a musician based on their personal preferences.
    /// Processing order: pitchShift first (to get concert pitch), then capo (to show shapes),
    /// then Nashville conversion if requested.
    /// </summary>
    /// <param name="text">Original text with inline [chord] notation.</param>
    /// <param name="key">The original key of the song.</param>
    /// <param name="capo">Capo fret position (0 = no capo).</param>
    /// <param name="nashville">Whether to display Nashville numbers.</param>
    /// <param name="pitchShift">Pitch shift in semitones (0 = no shift).</param>
    /// <returns>Transformed text with chords adjusted for the musician.</returns>
    public string RenderForMusician(string text, string key, int capo, bool nashville, int pitchShift)
    {
        if (string.IsNullOrEmpty(text)) return text;

        string result = text;

        // Step 1: Apply pitch shift to get concert pitch chords
        if (pitchShift != 0)
        {
            result = _chordProParser.Transpose(result, pitchShift);
        }

        // Step 2: Apply capo transposition (transpose DOWN by capo fret to show shapes)
        if (capo > 0)
        {
            result = _chordProParser.CapoTranspose(result, capo);
        }

        // Step 3: Convert to Nashville numbers if requested
        if (nashville)
        {
            // The effective key for Nashville is the original key shifted by pitchShift,
            // then adjusted for capo (since Nashville numbers are relative to what you hear)
            string effectiveKey = GetEffectiveKeyForNashville(key, pitchShift, capo);
            result = _chordProParser.ConvertToNashvilleNumbers(result, effectiveKey);
        }

        return result;
    }

    /// <summary>
    /// Create a personalized StageDisplayData for a musician by transforming the base data.
    /// </summary>
    /// <param name="baseData">The base stage display data (concert pitch chords).</param>
    /// <param name="capo">Musician's capo fret position.</param>
    /// <param name="nashville">Whether musician wants Nashville numbers.</param>
    /// <returns>A new StageDisplayData with transformed text fields.</returns>
    public StageDisplayData CreateMusicianData(StageDisplayData baseData, int capo, bool nashville)
    {
        if (baseData == null) return new StageDisplayData();

        var musicianData = new StageDisplayData
        {
            SectionName = baseData.SectionName,
            SongTitle = baseData.SongTitle,
            Key = baseData.Key,
            OriginalKey = baseData.OriginalKey,
            EffectiveKey = baseData.EffectiveKey,
            Capo = capo,
            TempoPercent = baseData.TempoPercent,
            PitchShift = baseData.PitchShift,
            IsLive = baseData.IsLive,
            IsBlacked = baseData.IsBlacked,
            CurrentSlideIndex = baseData.CurrentSlideIndex,
            TotalSlides = baseData.TotalSlides
        };

        // Transform text using the musician's preferences
        string key = baseData.OriginalKey;
        if (string.IsNullOrEmpty(key)) key = baseData.Key;

        musicianData.CurrentText = RenderForMusician(
            baseData.CurrentText, key, capo, nashville, baseData.PitchShift);
        musicianData.NextText = RenderForMusician(
            baseData.NextText, key, capo, nashville, baseData.PitchShift);

        return musicianData;
    }

    /// <summary>
    /// Calculate the effective key for Nashville number conversion.
    /// Nashville numbers are always relative to the key the chord shapes represent.
    /// After capo adjustment, the chord shapes are relative to the capo'd key.
    /// </summary>
    private string GetEffectiveKeyForNashville(string key, int pitchShift, int capo)
    {
        if (string.IsNullOrEmpty(key)) return key;

        // The effective key after pitch shift
        string effectiveKey = key;
        if (pitchShift != 0)
        {
            // Transpose the key name by pitchShift
            effectiveKey = TransposeKeyName(effectiveKey, pitchShift);
        }

        // After capo transposition, the shapes are relative to the capo'd key
        // so Nashville numbers should be relative to that capo'd key
        if (capo > 0)
        {
            effectiveKey = TransposeKeyName(effectiveKey, -capo);
        }

        return effectiveKey;
    }

    /// <summary>
    /// Transpose a key name by the given semitones.
    /// Uses ChordProParserService's Transpose on a dummy chord to get the new key name.
    /// </summary>
    private string TransposeKeyName(string key, int semitones)
    {
        if (string.IsNullOrEmpty(key) || semitones == 0) return key;

        // Wrap key in chord brackets, transpose, then extract
        string wrapped = $"[{key}]";
        string transposed = _chordProParser.Transpose(wrapped, semitones);

        // Extract from brackets
        if (transposed.StartsWith("[") && transposed.EndsWith("]"))
        {
            return transposed[1..^1];
        }
        return key;
    }
}
