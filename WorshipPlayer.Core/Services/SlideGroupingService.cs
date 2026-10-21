namespace WorshipPlayer.Core.Services;

/// <summary>
/// Pure (no I/O, no audio) service that groups a song section's lyric text into slide line-blocks.
///
/// Grouping rules (see spec auto-timed-slides/design.md):
///   • A "line" is a newline-separated line of the section text.
///   • Explicit BLANK-LINE breaks in the text are honored as HARD slide boundaries (preserving the
///     existing authoring style where the user inserts an empty line to force a slide break).
///   • Within each blank-line block, if it exceeds <c>linesPerSlide</c> lines, it is sub-split into
///     N-line slides.
///   • When the text contains NO blank-line breaks, the whole section is chunked into groups of
///     <c>linesPerSlide</c> lines.
///   • A trailing remainder forms a final short slide (e.g. 5 lines at 2/slide → 2,2,1).
///   • Blank/whitespace-only lines are not counted as lyric lines.
///
/// This is the single source of grouping truth shared by slide-timing recalculation and preview so
/// grouping is consistent everywhere.
/// </summary>
public static class SlideGroupingService
{
    /// <summary>One grouped slide: its index within the section, its lyric lines, and joined text.</summary>
    public readonly record struct SlideBlock(int SlideIndex, IReadOnlyList<string> Lines, string Text);

    /// <summary>
    /// Group a section's lyric text into slide blocks. <paramref name="linesPerSlide"/> is clamped to
    /// 2–4. Returns an empty list for null/whitespace text. Never throws.
    /// </summary>
    public static List<SlideBlock> GroupSection(string? sectionText, int linesPerSlide)
    {
        var result = new List<SlideBlock>();
        if (string.IsNullOrWhiteSpace(sectionText))
            return result;

        int perSlide = Math.Clamp(linesPerSlide, 2, 4);

        // Split into blank-line-separated blocks first (hard boundaries authored by the user).
        var hardBlocks = SplitOnBlankLines(sectionText);

        int slideIndex = 0;
        foreach (var block in hardBlocks)
        {
            // Lines within this hard block (non-empty only).
            var lines = block
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(l => l.TrimEnd())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            if (lines.Count == 0)
                continue;

            // Sub-split the block into N-line slides.
            for (int i = 0; i < lines.Count; i += perSlide)
            {
                var slideLines = lines.Skip(i).Take(perSlide).ToList();
                result.Add(new SlideBlock(slideIndex, slideLines, string.Join("\n", slideLines)));
                slideIndex++;
            }
        }

        return result;
    }

    /// <summary>
    /// Split text into blocks separated by one or more blank lines. Normalizes CRLF/CR to LF first.
    /// Returns a single block when there are no blank-line separators.
    /// </summary>
    private static List<string> SplitOnBlankLines(string text)
    {
        string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");

        var blocks = new List<string>();
        var current = new List<string>();

        foreach (var line in normalized.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (current.Count > 0)
                {
                    blocks.Add(string.Join("\n", current));
                    current.Clear();
                }
            }
            else
            {
                current.Add(line);
            }
        }
        if (current.Count > 0)
            blocks.Add(string.Join("\n", current));

        return blocks;
    }
}
