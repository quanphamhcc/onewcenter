using System.Text.RegularExpressions;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Pure (no I/O, no audio) service that computes each slide's advance timecode from the vocal
/// word-timestamps produced by forced alignment / Whisper (see spec auto-timed-slides/design.md).
///
/// For a slide, the advance time is the start time of the FIRST sung word of the slide's first line,
/// minus a configurable lead (so the slide appears slightly before the word is sung). Times are
/// clamped to the section range, kept monotonically non-decreasing across the section, and a slide
/// with no confident word match yields null (it then advances manually / at section start).
/// </summary>
public static class SlideAutoTimingService
{
    /// <summary>
    /// Compute a <see cref="ChartSlide.StartMs"/> per grouped slide for one section.
    ///
    /// <paramref name="blocks"/> are the section's grouped slides (from <see cref="SlideGroupingService"/>).
    /// <paramref name="words"/> are the vocal word timestamps (any order; filtered to the section).
    /// <paramref name="sectionStartMs"/>/<paramref name="sectionEndMs"/> bound the search + clamp.
    /// <paramref name="leadMs"/> is subtracted from each matched time (clamped so a time never goes
    /// below the section start or 0). Returns one nullable time per block (null = no confident match).
    /// Never throws.
    /// </summary>
    public static List<double?> ComputeSlideStartTimes(
        IReadOnlyList<SlideGroupingService.SlideBlock> blocks,
        IReadOnlyList<TimedWord> words,
        double sectionStartMs,
        double sectionEndMs,
        int leadMs)
    {
        var result = new List<double?>();
        if (blocks == null || blocks.Count == 0)
            return result;

        // Words within this section, in time order.
        var sectionWords = (words ?? Array.Empty<TimedWord>())
            .Where(w => w.StartMs >= sectionStartMs - 0.001 && w.StartMs < sectionEndMs + 0.001)
            .OrderBy(w => w.StartMs)
            .Select(w => (norm: Normalize(w.Word), start: w.StartMs))
            .Where(w => w.norm.Length > 0)
            .ToList();

        double lead = Math.Max(0, leadMs);
        double floor = Math.Max(0, sectionStartMs); // no slide earlier than its section / 0
        double lastAssigned = floor;                // enforce monotonic non-decreasing times
        int searchFrom = 0;                          // advance through words in order (respects order)

        foreach (var block in blocks)
        {
            string? firstWord = FirstSignificantWord(block);
            double? time = null;

            if (firstWord != null)
            {
                for (int i = searchFrom; i < sectionWords.Count; i++)
                {
                    if (WordsMatch(firstWord, sectionWords[i].norm))
                    {
                        double candidate = sectionWords[i].start - lead;
                        if (candidate < floor) candidate = floor;
                        if (candidate < lastAssigned) candidate = lastAssigned; // monotonic
                        time = candidate;
                        lastAssigned = candidate;
                        searchFrom = i + 1; // next slide searches after this word
                        break;
                    }
                }
            }

            result.Add(time);
        }

        return result;
    }

    /// <summary>First significant (len &gt; 1, chord/punct stripped) word of a slide's first line.</summary>
    private static string? FirstSignificantWord(SlideGroupingService.SlideBlock block)
    {
        if (block.Lines == null || block.Lines.Count == 0)
            return null;

        foreach (var line in block.Lines)
        {
            foreach (var token in Normalize(line).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length > 1)
                    return token;
            }
        }
        return null;
    }

    /// <summary>Exact match, or substring match for tokens of length ≥ 3 (handles elisions/plurals).</summary>
    private static bool WordsMatch(string a, string b)
    {
        if (a == b) return true;
        if (a.Length >= 3 && b.Length >= 3 && (a.Contains(b) || b.Contains(a))) return true;
        return false;
    }

    /// <summary>Strip [chords], lowercase, drop punctuation → space-separated clean tokens.</summary>
    private static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string s = Regex.Replace(text, @"\[[^\]]*\]", " "); // remove [chord] annotations
        s = s.ToLowerInvariant();
        s = Regex.Replace(s, @"[^\w\s]", " ");               // drop punctuation
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }
}
