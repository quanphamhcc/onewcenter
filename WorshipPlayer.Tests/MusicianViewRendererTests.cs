using Xunit;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Presenter.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Tests for MusicianViewRenderer covering capo transposition, Nashville number conversion,
/// pitch shift, and combined scenarios for per-musician personalized chord views.
/// </summary>
public class MusicianViewRendererTests
{
    private readonly ChordProParserService _parser = new();
    private readonly MusicianViewRenderer _renderer;

    public MusicianViewRendererTests()
    {
        _renderer = new MusicianViewRenderer(_parser);
    }

    // --- Capo Tests (capo 0-7) ---

    [Fact]
    public void RenderForMusician_Capo0_NoChange()
    {
        string input = "[G]Amazing [C]grace";
        string result = _renderer.RenderForMusician(input, "G", capo: 0, nashville: false, pitchShift: 0);
        Assert.Equal("[G]Amazing [C]grace", result);
    }

    [Fact]
    public void RenderForMusician_Capo1_TransposesDown1()
    {
        // With capo on 1, to sound G you play F# shape
        string input = "[G]Amazing [C]grace";
        string result = _renderer.RenderForMusician(input, "G", capo: 1, nashville: false, pitchShift: 0);
        Assert.Equal("[F#]Amazing [B]grace", result);
    }

    [Fact]
    public void RenderForMusician_Capo2_TransposesDown2()
    {
        // With capo on 2, to sound G you play F shape, to sound C you play Bb shape
        string input = "[G]Amazing [C]grace";
        string result = _renderer.RenderForMusician(input, "G", capo: 2, nashville: false, pitchShift: 0);
        Assert.Equal("[F]Amazing [A#]grace", result);
    }

    [Fact]
    public void RenderForMusician_Capo3_TransposesDown3()
    {
        // With capo on 3, to sound G you play E shape
        string input = "[G]Amazing [C]grace";
        string result = _renderer.RenderForMusician(input, "G", capo: 3, nashville: false, pitchShift: 0);
        Assert.Equal("[E]Amazing [A]grace", result);
    }

    [Fact]
    public void RenderForMusician_Capo4_TransposesDown4()
    {
        // With capo on 4, to sound G you play D# shape, to sound C you play G# shape
        string input = "[G]Amazing [C]grace";
        string result = _renderer.RenderForMusician(input, "G", capo: 4, nashville: false, pitchShift: 0);
        Assert.Equal("[D#]Amazing [G#]grace", result);
    }

    [Fact]
    public void RenderForMusician_Capo5_TransposesDown5()
    {
        // With capo on 5, to sound G you play D shape, to sound C you play G shape
        string input = "[G]Amazing [C]grace";
        string result = _renderer.RenderForMusician(input, "G", capo: 5, nashville: false, pitchShift: 0);
        Assert.Equal("[D]Amazing [G]grace", result);
    }

    [Fact]
    public void RenderForMusician_Capo6_TransposesDown6()
    {
        string input = "[G]Amazing [C]grace";
        string result = _renderer.RenderForMusician(input, "G", capo: 6, nashville: false, pitchShift: 0);
        Assert.Equal("[C#]Amazing [F#]grace", result);
    }

    [Fact]
    public void RenderForMusician_Capo7_TransposesDown7()
    {
        string input = "[G]Amazing [C]grace";
        string result = _renderer.RenderForMusician(input, "G", capo: 7, nashville: false, pitchShift: 0);
        Assert.Equal("[C]Amazing [F]grace", result);
    }

    [Fact]
    public void RenderForMusician_Capo_WithMinorChords()
    {
        string input = "[Am]Verse [Em]line [Dm]here";
        string result = _renderer.RenderForMusician(input, "C", capo: 2, nashville: false, pitchShift: 0);
        // Am down 2 = Gm, Em down 2 = Dm, Dm down 2 = Cm
        Assert.Equal("[Gm]Verse [Dm]line [Cm]here", result);
    }

    [Fact]
    public void RenderForMusician_Capo_With7thChords()
    {
        string input = "[G7]Blues [C7]chord [D7]here";
        string result = _renderer.RenderForMusician(input, "G", capo: 2, nashville: false, pitchShift: 0);
        // G7 down 2 = F7, C7 down 2 = A#7, D7 down 2 = C7
        Assert.Equal("[F7]Blues [A#7]chord [C7]here", result);
    }

    // --- Nashville Tests (all 12 keys) ---

    [Fact]
    public void RenderForMusician_Nashville_KeyOfC()
    {
        string input = "[C]one [F]four [G]five [Am]six";
        string result = _renderer.RenderForMusician(input, "C", capo: 0, nashville: true, pitchShift: 0);
        Assert.Equal("[1]one [4]four [5]five [6m]six", result);
    }

    [Fact]
    public void RenderForMusician_Nashville_KeyOfDb()
    {
        // Db=1, Gb=4, Ab=5
        string input = "[Db]one [Gb]four [Ab]five";
        // Db is represented as C# internally
        string result = _renderer.RenderForMusician(input, "Db", capo: 0, nashville: true, pitchShift: 0);
        Assert.Equal("[1]one [4]four [5]five", result);
    }

    [Fact]
    public void RenderForMusician_Nashville_KeyOfD()
    {
        // D=1, G=4, A=5, Bm=6m
        string input = "[D]one [G]four [A]five [Bm]six";
        string result = _renderer.RenderForMusician(input, "D", capo: 0, nashville: true, pitchShift: 0);
        Assert.Equal("[1]one [4]four [5]five [6m]six", result);
    }

    [Fact]
    public void RenderForMusician_Nashville_KeyOfEb()
    {
        // Eb=1, Ab=4, Bb=5
        string input = "[Eb]one [Ab]four [Bb]five";
        string result = _renderer.RenderForMusician(input, "Eb", capo: 0, nashville: true, pitchShift: 0);
        Assert.Equal("[1]one [4]four [5]five", result);
    }

    [Fact]
    public void RenderForMusician_Nashville_KeyOfE()
    {
        // E=1, A=4, B=5, C#m=6m
        string input = "[E]one [A]four [B]five [C#m]six";
        string result = _renderer.RenderForMusician(input, "E", capo: 0, nashville: true, pitchShift: 0);
        Assert.Equal("[1]one [4]four [5]five [6m]six", result);
    }

    [Fact]
    public void RenderForMusician_Nashville_KeyOfF()
    {
        // F=1, Bb=4, C=5, Dm=6m
        string input = "[F]one [Bb]four [C]five [Dm]six";
        string result = _renderer.RenderForMusician(input, "F", capo: 0, nashville: true, pitchShift: 0);
        Assert.Equal("[1]one [4]four [5]five [6m]six", result);
    }

    [Fact]
    public void RenderForMusician_Nashville_KeyOfFSharp()
    {
        // F#=1, B=4, C#=5
        string input = "[F#]one [B]four [C#]five";
        string result = _renderer.RenderForMusician(input, "F#", capo: 0, nashville: true, pitchShift: 0);
        Assert.Equal("[1]one [4]four [5]five", result);
    }

    [Fact]
    public void RenderForMusician_Nashville_KeyOfG()
    {
        // G=1, C=4, D=5, Em=6m
        string input = "[G]one [C]four [D]five [Em]six";
        string result = _renderer.RenderForMusician(input, "G", capo: 0, nashville: true, pitchShift: 0);
        Assert.Equal("[1]one [4]four [5]five [6m]six", result);
    }

    [Fact]
    public void RenderForMusician_Nashville_KeyOfAb()
    {
        // Ab=1, Db=4, Eb=5
        string input = "[Ab]one [Db]four [Eb]five";
        string result = _renderer.RenderForMusician(input, "Ab", capo: 0, nashville: true, pitchShift: 0);
        Assert.Equal("[1]one [4]four [5]five", result);
    }

    [Fact]
    public void RenderForMusician_Nashville_KeyOfA()
    {
        // A=1, D=4, E=5, F#m=6m
        string input = "[A]one [D]four [E]five [F#m]six";
        string result = _renderer.RenderForMusician(input, "A", capo: 0, nashville: true, pitchShift: 0);
        Assert.Equal("[1]one [4]four [5]five [6m]six", result);
    }

    [Fact]
    public void RenderForMusician_Nashville_KeyOfBb()
    {
        // Bb=1, Eb=4, F=5
        string input = "[Bb]one [Eb]four [F]five";
        string result = _renderer.RenderForMusician(input, "Bb", capo: 0, nashville: true, pitchShift: 0);
        Assert.Equal("[1]one [4]four [5]five", result);
    }

    [Fact]
    public void RenderForMusician_Nashville_KeyOfB()
    {
        // B=1, E=4, F#=5, G#m=6m
        string input = "[B]one [E]four [F#]five [G#m]six";
        string result = _renderer.RenderForMusician(input, "B", capo: 0, nashville: true, pitchShift: 0);
        Assert.Equal("[1]one [4]four [5]five [6m]six", result);
    }

    // --- Combined Capo + Nashville ---

    [Fact]
    public void RenderForMusician_CapoAndNashville_KeyOfG_Capo2()
    {
        // Song in G. With capo 2, you play shapes 2 semitones lower.
        // G -> F, C -> A#, D -> C, Em -> Dm
        // Nashville relative to the capo'd key (G transposed down 2 = F):
        // F=1, A#=4, C=5, Dm=6m
        string input = "[G]one [C]four [D]five [Em]six";
        string result = _renderer.RenderForMusician(input, "G", capo: 2, nashville: true, pitchShift: 0);
        Assert.Equal("[1]one [4]four [5]five [6m]six", result);
    }

    [Fact]
    public void RenderForMusician_CapoAndNashville_KeyOfA_Capo2()
    {
        // Song in A. With capo 2, play shapes 2 lower: A->G, D->C, E->D
        // Nashville relative to capo'd key (A down 2 = G): G=1, C=4, D=5
        string input = "[A]one [D]four [E]five";
        string result = _renderer.RenderForMusician(input, "A", capo: 2, nashville: true, pitchShift: 0);
        Assert.Equal("[1]one [4]four [5]five", result);
    }

    [Fact]
    public void RenderForMusician_CapoAndNashville_KeyOfE_Capo4()
    {
        // Song in E. With capo 4, play shapes 4 lower: E->C, A->F, B->G
        // Nashville relative to capo'd key (E down 4 = C): C=1, F=4, G=5
        string input = "[E]one [A]four [B]five";
        string result = _renderer.RenderForMusician(input, "E", capo: 4, nashville: true, pitchShift: 0);
        Assert.Equal("[1]one [4]four [5]five", result);
    }

    // --- Pitch Shift Tests ---

    [Fact]
    public void RenderForMusician_PitchShiftUp2_NoCapo()
    {
        // Pitch shift +2 means concert pitch is 2 semitones higher
        // G +2 = A, C +2 = D
        string input = "[G]Amazing [C]grace";
        string result = _renderer.RenderForMusician(input, "G", capo: 0, nashville: false, pitchShift: 2);
        Assert.Equal("[A]Amazing [D]grace", result);
    }

    [Fact]
    public void RenderForMusician_PitchShiftDown3_NoCapo()
    {
        // Pitch shift -3: G -3 = E, C -3 = A
        string input = "[G]Amazing [C]grace";
        string result = _renderer.RenderForMusician(input, "G", capo: 0, nashville: false, pitchShift: -3);
        Assert.Equal("[E]Amazing [A]grace", result);
    }

    [Fact]
    public void RenderForMusician_PitchShiftAndCapo()
    {
        // Pitch shift +2 first: G -> A, C -> D
        // Then capo 2: A -> G, D -> C
        string input = "[G]Amazing [C]grace";
        string result = _renderer.RenderForMusician(input, "G", capo: 2, nashville: false, pitchShift: 2);
        Assert.Equal("[G]Amazing [C]grace", result);
    }

    [Fact]
    public void RenderForMusician_PitchShiftAndNashville()
    {
        // Pitch shift +2 first: G -> A, C -> D, D -> E
        // Nashville in effective key (G+2=A): A=1, D=4, E=5
        string input = "[G]one [C]four [D]five";
        string result = _renderer.RenderForMusician(input, "G", capo: 0, nashville: true, pitchShift: 2);
        Assert.Equal("[1]one [4]four [5]five", result);
    }

    [Fact]
    public void RenderForMusician_PitchShiftCapoAndNashville()
    {
        // Song in G, pitch shift +2, capo 2
        // Pitch shift first: G->A, C->D, D->E
        // Capo 2: A->G, D->C, E->D
        // Nashville relative to effective key after both adjustments (A transposed down 2 = G):
        // G=1, C=4, D=5
        string input = "[G]one [C]four [D]five";
        string result = _renderer.RenderForMusician(input, "G", capo: 2, nashville: true, pitchShift: 2);
        Assert.Equal("[1]one [4]four [5]five", result);
    }

    // --- Edge Cases ---

    [Fact]
    public void RenderForMusician_EmptyText_ReturnsEmpty()
    {
        string result = _renderer.RenderForMusician("", "G", capo: 2, nashville: true, pitchShift: 0);
        Assert.Equal("", result);
    }

    [Fact]
    public void RenderForMusician_NullText_ReturnsNull()
    {
        string? result = _renderer.RenderForMusician(null!, "G", capo: 2, nashville: true, pitchShift: 0);
        Assert.Null(result);
    }

    [Fact]
    public void RenderForMusician_NoChords_TextUnchanged()
    {
        string input = "Just plain text with no chords";
        string result = _renderer.RenderForMusician(input, "G", capo: 2, nashville: true, pitchShift: 0);
        Assert.Equal("Just plain text with no chords", result);
    }

    [Fact]
    public void RenderForMusician_NCChord_PreservedThroughCapo()
    {
        string input = "[N.C.]No chord here";
        string result = _renderer.RenderForMusician(input, "G", capo: 2, nashville: false, pitchShift: 0);
        Assert.Equal("[N.C.]No chord here", result);
    }

    [Fact]
    public void RenderForMusician_SlashChord_CapoTransposed()
    {
        string input = "[G/B]Bass note";
        string result = _renderer.RenderForMusician(input, "G", capo: 2, nashville: false, pitchShift: 0);
        // G/B down 2 = F/A
        Assert.Equal("[F/A]Bass note", result);
    }

    // --- CreateMusicianData Tests ---

    [Fact]
    public void CreateMusicianData_CopiesAllFields()
    {
        var baseData = new StageDisplayData
        {
            CurrentText = "[G]Hello [C]world",
            NextText = "[D]Next [Em]section",
            SectionName = "Verse 1",
            SongTitle = "Test Song",
            Key = "G",
            OriginalKey = "G",
            EffectiveKey = "G",
            Capo = 0,
            TempoPercent = 95.0,
            PitchShift = 0,
            IsLive = true,
            IsBlacked = false,
            CurrentSlideIndex = 2,
            TotalSlides = 8
        };

        var result = _renderer.CreateMusicianData(baseData, capo: 2, nashville: false);

        Assert.Equal("Verse 1", result.SectionName);
        Assert.Equal("Test Song", result.SongTitle);
        Assert.Equal("G", result.OriginalKey);
        Assert.Equal("G", result.EffectiveKey);
        Assert.Equal(2, result.Capo);
        Assert.Equal(95.0, result.TempoPercent);
        Assert.True(result.IsLive);
        Assert.False(result.IsBlacked);
        Assert.Equal(2, result.CurrentSlideIndex);
        Assert.Equal(8, result.TotalSlides);
    }

    [Fact]
    public void CreateMusicianData_TransformsTextWithCapo()
    {
        var baseData = new StageDisplayData
        {
            CurrentText = "[G]Hello [C]world",
            NextText = "[D]Next",
            Key = "G",
            OriginalKey = "G",
            EffectiveKey = "G",
            PitchShift = 0
        };

        var result = _renderer.CreateMusicianData(baseData, capo: 2, nashville: false);

        // G down 2 = F, C down 2 = A#, D down 2 = C
        Assert.Equal("[F]Hello [A#]world", result.CurrentText);
        Assert.Equal("[C]Next", result.NextText);
    }

    [Fact]
    public void CreateMusicianData_TransformsTextWithNashville()
    {
        var baseData = new StageDisplayData
        {
            CurrentText = "[G]Hello [C]world",
            NextText = "[D]Next [Em]section",
            Key = "G",
            OriginalKey = "G",
            EffectiveKey = "G",
            PitchShift = 0
        };

        var result = _renderer.CreateMusicianData(baseData, capo: 0, nashville: true);

        Assert.Equal("[1]Hello [4]world", result.CurrentText);
        Assert.Equal("[5]Next [6m]section", result.NextText);
    }

    [Fact]
    public void CreateMusicianData_NullBaseData_ReturnsEmpty()
    {
        var result = _renderer.CreateMusicianData(null!, capo: 2, nashville: true);

        Assert.NotNull(result);
        Assert.Equal("", result.CurrentText);
        Assert.Equal("", result.NextText);
    }

    [Fact]
    public void CreateMusicianData_WithPitchShift_AppliesPitchBeforeCapo()
    {
        var baseData = new StageDisplayData
        {
            CurrentText = "[G]Hello [C]world",
            Key = "G",
            OriginalKey = "G",
            EffectiveKey = "A",
            PitchShift = 2  // Pitch shift of +2
        };

        var result = _renderer.CreateMusicianData(baseData, capo: 2, nashville: false);

        // Pitch shift +2: G -> A, C -> D
        // Then capo 2: A -> G, D -> C
        Assert.Equal("[G]Hello [C]world", result.CurrentText);
    }
}
