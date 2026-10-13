using System.Text;
using System.Text.RegularExpressions;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Parses and converts chord chart formats to the unified inline [chord] notation.
/// 
/// Supported input formats:
/// 1. Ultimate Guitar (chords on separate line above lyrics)
/// 2. ChordPro format ({directives} and [chords] inline)
/// 3. Plain text with section labels
/// 
/// Output format: Inline [chord] notation
///   Example: "[B]    The Lord bless you  [E]and keep you"
/// 
/// Rules:
/// - [X] inside text = chord (where X is any chord symbol)
/// - Empty line = slide break
/// - [N.C.] = no chord
/// - Previous chord carries over to lines without new chord
/// - Section labels like [Verse 1] are extracted, not stored in text
/// </summary>
public class ChordProParserService
{
    // Known chord roots for detection
    private static readonly string[] ChordRoots = {
        "A", "B", "C", "D", "E", "F", "G",
        "Ab", "Bb", "Cb", "Db", "Eb", "Fb", "Gb",
        "A#", "B#", "C#", "D#", "E#", "F#", "G#"
    };

    // Chord quality suffixes (optional)
    private static readonly string[] ChordSuffixes = {
        "m", "min", "maj", "dim", "aug", "sus", "sus2", "sus4",
        "7", "maj7", "m7", "dim7", "aug7", "add9", "add11",
        "9", "11", "13", "6", "m6", "m9", "m11",
        "7sus4", "7sus2", "mmaj7", "5", "2", "4"
    };

    // Section label patterns
    private static readonly Regex SectionLabelRegex = new(
        @"^\s*\[?(Verse|Chorus|Bridge|Pre[\-\s]?Chorus|Intro|Outro|Interlude|Instrumental|Tag|Ending|Hook|Refrain|Coda)\s*\d*\s*\(?.*?\)?\]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ChordPro directive pattern: {key: value}
    private static readonly Regex DirectiveRegex = new(
        @"^\s*\{(\w+):\s*(.*?)\}\s*$",
        RegexOptions.Compiled);

    // Slide timestamp pattern: {M:SS.mmm} or {M:SS:mmm} at start of line
    private static readonly Regex SlideTimestampRegex = new(
        @"^\{(\d+:\d{2}[\.:]\d{1,3})\}",
        RegexOptions.Compiled);

    // Inline chord pattern: [Chord]
    private static readonly Regex InlineChordRegex = new(
        @"\[([^\]]+)\]",
        RegexOptions.Compiled);

    /// <summary>
    /// Result of parsing a chord chart text.
    /// </summary>
    public class ParseResult
    {
        /// <summary>Sections extracted from the input, with names and inline [chord] text.</summary>
        public List<ParsedSection> Sections { get; set; } = new();

        /// <summary>Metadata directives found (title, artist, key, tempo, capo).</summary>
        public Dictionary<string, string> Directives { get; set; } = new();

        /// <summary>Full text in inline [chord] format (all sections combined).</summary>
        public string FullText { get; set; } = "";
    }

    public class ParsedSection
    {
        public string Name { get; set; } = "";
        public string Text { get; set; } = ""; // Inline [chord] format
    }

    /// <summary>
    /// Parse any chord chart format and convert to inline [chord] notation.
    /// Auto-detects the input format (Ultimate Guitar, ChordPro, or plain).
    /// </summary>
    public ParseResult Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new ParseResult();

        var lines = input.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var result = new ParseResult();

        // Extract directives first
        var contentLines = new List<string>();
        foreach (var line in lines)
        {
            var directiveMatch = DirectiveRegex.Match(line);
            if (directiveMatch.Success)
            {
                string key = directiveMatch.Groups[1].Value.ToLower();
                string value = directiveMatch.Groups[2].Value.Trim();
                result.Directives[key] = value;
            }
            else
            {
                contentLines.Add(line);
            }
        }

        // Detect format and parse
        if (IsAlreadyInlineFormat(contentLines))
        {
            // Already in [chord] inline format — just extract sections
            result.Sections = ExtractSectionsFromInline(contentLines);
        }
        else if (HasChordsAboveLyrics(contentLines))
        {
            // Ultimate Guitar format — convert to inline
            result.Sections = ConvertUltimateGuitarToInline(contentLines);
        }
        else
        {
            // Plain text (lyrics only, no chords)
            result.Sections = ExtractSectionsFromPlain(contentLines);
        }

        // Build full text (all sections concatenated)
        var fullTextBuilder = new StringBuilder();
        foreach (var section in result.Sections)
        {
            if (!string.IsNullOrEmpty(section.Text))
            {
                if (fullTextBuilder.Length > 0)
                    fullTextBuilder.AppendLine(); // Empty line = section separator
                fullTextBuilder.Append(section.Text);
            }
        }
        result.FullText = fullTextBuilder.ToString();

        return result;
    }

    /// <summary>
    /// Convert Ultimate Guitar format (chords on line above lyrics) to inline [chord] format.
    /// Strips empty lines, extracts section labels, maps chord character positions to lyrics.
    /// </summary>
    private List<ParsedSection> ConvertUltimateGuitarToInline(List<string> lines)
    {
        var sections = new List<ParsedSection>();
        var currentSection = new ParsedSection { Name = "" };
        var currentText = new StringBuilder();

        int i = 0;
        while (i < lines.Count)
        {
            string line = lines[i];

            // Check for section label
            if (IsSectionLabel(line))
            {
                // Save current section if it has content
                if (currentText.Length > 0)
                {
                    currentSection.Text = TrimTrailingEmptyLines(currentText.ToString());
                    if (!string.IsNullOrWhiteSpace(currentSection.Text))
                        sections.Add(currentSection);
                    currentText.Clear();
                }
                currentSection = new ParsedSection { Name = ExtractSectionName(line) };
                i++;
                continue;
            }

            // Skip empty lines (don't transfer them — user adds slide breaks manually)
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            // Check if this line is a chord line (followed by a lyric line)
            if (IsChordLine(line))
            {
                // Get the lyric line below (if it exists)
                string lyricLine = "";
                if (i + 1 < lines.Count && !IsChordLine(lines[i + 1]) && !IsSectionLabel(lines[i + 1]))
                {
                    if (!string.IsNullOrWhiteSpace(lines[i + 1]))
                    {
                        lyricLine = lines[i + 1];
                        i++; // Consume the lyric line too
                    }
                }

                // Merge chord line into lyric line as inline [chord]
                string merged = MergeChordAndLyricLines(line, lyricLine);
                if (currentText.Length > 0)
                    currentText.AppendLine();
                currentText.Append(merged);
            }
            else
            {
                // Regular lyric line (no chord above) — keep as-is
                if (currentText.Length > 0)
                    currentText.AppendLine();
                currentText.Append(line);
            }

            i++;
        }

        // Save last section
        if (currentText.Length > 0)
        {
            currentSection.Text = TrimTrailingEmptyLines(currentText.ToString());
            if (!string.IsNullOrWhiteSpace(currentSection.Text))
                sections.Add(currentSection);
        }

        // If no sections were labeled, create one default section
        if (sections.Count == 0 && currentText.Length > 0)
        {
            sections.Add(new ParsedSection
            {
                Name = "",
                Text = TrimTrailingEmptyLines(currentText.ToString())
            });
        }

        return sections;
    }

    /// <summary>
    /// Merge a chord line and lyric line into inline [chord] format.
    /// Maps each chord to its character position in the lyric line.
    /// </summary>
    private string MergeChordAndLyricLines(string chordLine, string lyricLine)
    {
        if (string.IsNullOrWhiteSpace(chordLine))
            return lyricLine;

        // Parse chord positions from the chord line
        var chords = ParseChordPositions(chordLine);

        if (chords.Count == 0)
            return lyricLine;

        if (string.IsNullOrEmpty(lyricLine))
        {
            // Chord line with no lyrics below — create chord-only line
            return string.Join("", chords.Select(c => $"[{c.chord}]"));
        }

        // Insert chords into the lyric line at their character positions
        // Work right-to-left to preserve offsets
        var result = new StringBuilder(lyricLine);

        // Pad lyric line if chords extend beyond it
        int maxChordPos = chords.Max(c => c.position);
        if (maxChordPos > result.Length)
            result.Append(new string(' ', maxChordPos - result.Length + 1));

        // Insert from right to left
        for (int c = chords.Count - 1; c >= 0; c--)
        {
            int pos = Math.Min(chords[c].position, result.Length);

            // Snap to nearest word boundary (if not already at one)
            pos = SnapToNearestWordBoundary(result.ToString(), pos);

            result.Insert(pos, $"[{chords[c].chord}]");
        }

        return result.ToString();
    }

    /// <summary>
    /// Snap a position to the nearest word boundary in the text.
    /// Prefers the exact position, but if mid-word, moves to start of that word.
    /// </summary>
    private int SnapToNearestWordBoundary(string text, int pos)
    {
        if (pos <= 0) return 0;
        if (pos >= text.Length) return text.Length;

        // If already at a space or start of word, it's fine
        if (pos == 0 || text[pos - 1] == ' ' || text[pos] == ' ')
            return pos;

        // We're mid-word — keep exact position (user specified exact char alignment)
        return pos;
    }

    /// <summary>
    /// Parse chord symbols and their character positions from a chord line.
    /// </summary>
    private List<(int position, string chord)> ParseChordPositions(string chordLine)
    {
        var chords = new List<(int position, string chord)>();

        int i = 0;
        while (i < chordLine.Length)
        {
            if (chordLine[i] == ' ')
            {
                i++;
                continue;
            }

            // Found start of a chord — read until next space
            int start = i;
            while (i < chordLine.Length && chordLine[i] != ' ')
                i++;

            string chordText = chordLine[start..i];

            // Validate it looks like a chord
            if (LooksLikeChord(chordText))
            {
                chords.Add((start, chordText));
            }
        }

        return chords;
    }

    /// <summary>
    /// Determine if a line contains only chord symbols (= it's a chord line).
    /// </summary>
    public bool IsChordLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;

        var tokens = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;

        // A chord line must have at least one chord-looking token
        // and ALL non-empty tokens must be chords
        int chordCount = 0;
        foreach (var token in tokens)
        {
            if (LooksLikeChord(token))
                chordCount++;
            else
                return false; // Non-chord token found
        }

        return chordCount > 0;
    }

    /// <summary>
    /// Check if a token looks like a chord symbol.
    /// </summary>
    public bool LooksLikeChord(string token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        if (token == "N.C." || token == "NC" || token == "N/C") return true;

        // Must start with a chord root (A-G with optional # or b)
        string remaining = token;

        // Check root
        bool hasRoot = false;
        foreach (var root in ChordRoots.OrderByDescending(r => r.Length))
        {
            if (remaining.StartsWith(root, StringComparison.Ordinal))
            {
                remaining = remaining[root.Length..];
                hasRoot = true;
                break;
            }
        }

        if (!hasRoot) return false;

        // Remove bass note (e.g., /F#, /B)
        int slashIdx = remaining.IndexOf('/');
        if (slashIdx >= 0)
        {
            string bassNote = remaining[(slashIdx + 1)..];
            remaining = remaining[..slashIdx];

            // Validate bass note is a root
            bool validBass = false;
            foreach (var root in ChordRoots)
            {
                if (bassNote.StartsWith(root, StringComparison.Ordinal))
                {
                    validBass = true;
                    break;
                }
            }
            if (!validBass && bassNote.Length > 0) return false;
        }

        // Remaining should be empty or a valid suffix
        if (remaining.Length == 0) return true;

        // Check known suffixes
        foreach (var suffix in ChordSuffixes.OrderByDescending(s => s.Length))
        {
            if (remaining.StartsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                remaining = remaining[suffix.Length..];
                break;
            }
        }

        // Allow remaining digits (e.g., "7", "9", "11")
        return remaining.Length == 0 || remaining.All(c => char.IsDigit(c) || c == '#' || c == 'b');
    }

    /// <summary>
    /// Determine if a line is a section label (e.g., [Verse 1], [Chorus], etc.)
    /// </summary>
    public bool IsSectionLabel(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        return SectionLabelRegex.IsMatch(line);
    }

    /// <summary>
    /// Extract the section name from a label line.
    /// </summary>
    private string ExtractSectionName(string line)
    {
        line = line.Trim();
        // Remove brackets if present
        if (line.StartsWith("[")) line = line[1..];
        if (line.EndsWith("]")) line = line[..^1];
        // Remove parenthetical notes like "(repeat)"
        int parenIdx = line.IndexOf('(');
        if (parenIdx > 0) line = line[..parenIdx];
        return line.Trim().ToUpper();
    }

    /// <summary>
    /// Check if the input is already in inline [chord] format.
    /// </summary>
    private bool IsAlreadyInlineFormat(List<string> lines)
    {
        int inlineChordCount = 0;
        int chordAboveCount = 0;

        foreach (var line in lines.Take(20)) // Check first 20 lines
        {
            if (InlineChordRegex.IsMatch(line)) inlineChordCount++;
            if (IsChordLine(line)) chordAboveCount++;
        }

        return inlineChordCount > chordAboveCount;
    }

    /// <summary>
    /// Check if the input has chords-above-lyrics format (Ultimate Guitar style).
    /// </summary>
    private bool HasChordsAboveLyrics(List<string> lines)
    {
        for (int i = 0; i < lines.Count - 1; i++)
        {
            if (IsChordLine(lines[i]) && !string.IsNullOrWhiteSpace(lines[i + 1]))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Extract sections from text already in inline [chord] format.
    /// </summary>
    private List<ParsedSection> ExtractSectionsFromInline(List<string> lines)
    {
        var sections = new List<ParsedSection>();
        var currentSection = new ParsedSection { Name = "" };
        var currentText = new StringBuilder();

        foreach (var line in lines)
        {
            if (IsSectionLabel(line))
            {
                if (currentText.Length > 0)
                {
                    currentSection.Text = TrimTrailingEmptyLines(currentText.ToString());
                    if (!string.IsNullOrWhiteSpace(currentSection.Text))
                        sections.Add(currentSection);
                    currentText.Clear();
                }
                currentSection = new ParsedSection { Name = ExtractSectionName(line) };
                continue;
            }

            // Skip empty lines from import
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (currentText.Length > 0) currentText.AppendLine();
            currentText.Append(line);
        }

        if (currentText.Length > 0)
        {
            currentSection.Text = TrimTrailingEmptyLines(currentText.ToString());
            if (!string.IsNullOrWhiteSpace(currentSection.Text))
                sections.Add(currentSection);
        }

        return sections;
    }

    /// <summary>
    /// Extract sections from plain text (no chords).
    /// </summary>
    private List<ParsedSection> ExtractSectionsFromPlain(List<string> lines)
    {
        return ExtractSectionsFromInline(lines); // Same logic works
    }

    /// <summary>
    /// Transpose all inline [chord] brackets in a text by a given number of semitones.
    /// </summary>
    public string Transpose(string text, int semitones)
    {
        if (semitones == 0 || string.IsNullOrEmpty(text)) return text;

        return InlineChordRegex.Replace(text, match =>
        {
            string chord = match.Groups[1].Value;
            if (chord == "N.C." || chord == "NC") return match.Value;

            string transposed = TransposeChord(chord, semitones);
            return $"[{transposed}]";
        });
    }

    /// <summary>
    /// Transpose a single chord symbol by semitones.
    /// </summary>
    private string TransposeChord(string chord, int semitones)
    {
        var noteOrder = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        var flatToSharp = new Dictionary<string, string>
        {
            {"Db", "C#"}, {"Eb", "D#"}, {"Fb", "E"}, {"Gb", "F#"}, {"Ab", "G#"}, {"Bb", "A#"}, {"Cb", "B"}
        };

        // Handle bass note (e.g., B/D#)
        int slashIdx = chord.IndexOf('/');
        string bassNote = "";
        string mainChord = chord;

        if (slashIdx > 0)
        {
            bassNote = chord[(slashIdx + 1)..];
            mainChord = chord[..slashIdx];
        }

        // Extract root from main chord
        string root = ExtractRoot(mainChord);
        if (root == "") return chord; // Can't parse

        string suffix = mainChord[root.Length..];

        // Transpose root
        string transposedRoot = TransposeNote(root, semitones, noteOrder, flatToSharp);

        // Transpose bass note if present
        string result = transposedRoot + suffix;
        if (!string.IsNullOrEmpty(bassNote))
        {
            string transposedBass = TransposeNote(bassNote, semitones, noteOrder, flatToSharp);
            result += "/" + transposedBass;
        }

        return result;
    }

    private string ExtractRoot(string chord)
    {
        foreach (var root in ChordRoots.OrderByDescending(r => r.Length))
        {
            if (chord.StartsWith(root, StringComparison.Ordinal))
                return root;
        }
        return "";
    }

    private string TransposeNote(string note, int semitones, string[] noteOrder,
                                  Dictionary<string, string> flatToSharp)
    {
        // Normalize flats to sharps for lookup
        if (flatToSharp.TryGetValue(note, out string? normalized))
            note = normalized;

        int idx = Array.IndexOf(noteOrder, note);
        if (idx < 0) return note; // Unknown note

        int newIdx = (idx + semitones % 12 + 12) % 12;
        return noteOrder[newIdx];
    }

    /// <summary>
    /// Strip chord brackets from text for lyrics-only display.
    /// "[B]The Lord [E]bless you" → "The Lord bless you"
    /// </summary>
    public string StripChords(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return InlineChordRegex.Replace(text, "");
    }

    /// <summary>
    /// Strip both chord brackets AND timestamp brackets from text.
    /// "{0:17.937}[B]The Lord [E]bless" → "The Lord bless"
    /// </summary>
    public string StripChordsAndTimestamps(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = SlideTimestampRegex.Replace(text, "");
        text = InlineChordRegex.Replace(text, "");
        return text;
    }

    /// <summary>
    /// Extract slide blocks from section text, split by empty lines.
    /// Returns list of (timestampMs, text) tuples.
    /// If {timestamp} is present, uses it. Otherwise returns -1 for auto-calculate.
    /// </summary>
    public List<(double timestampMs, string text)> ExtractSlideBlocks(string sectionText)
    {
        var blocks = new List<(double timestampMs, string text)>();
        if (string.IsNullOrWhiteSpace(sectionText)) return blocks;

        // Split by empty lines
        var slideTexts = sectionText.Split(new[] { "\n\n", "\r\n\r\n" },
            StringSplitOptions.None);

        foreach (var block in slideTexts)
        {
            string trimmed = block.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Check for {timestamp} at the start
            var match = SlideTimestampRegex.Match(trimmed);
            double timestampMs = -1; // -1 = auto-calculate

            if (match.Success)
            {
                string timeStr = match.Groups[1].Value;
                timestampMs = ParseTimecodeToMs(timeStr);
                trimmed = trimmed[match.Length..].TrimStart();
            }

            blocks.Add((timestampMs, trimmed));
        }

        return blocks;
    }

    /// <summary>
    /// Insert or update {timestamp} at the start of each slide block in section text.
    /// </summary>
    public string SetSlideTimestamps(string sectionText, List<double> timestampsMs)
    {
        var blocks = sectionText.Split(new[] { "\n\n", "\r\n\r\n" },
            StringSplitOptions.None);

        var result = new System.Text.StringBuilder();
        int tsIdx = 0;

        for (int i = 0; i < blocks.Length; i++)
        {
            string block = blocks[i].Trim();
            if (string.IsNullOrEmpty(block)) continue;

            if (result.Length > 0)
                result.Append("\n\n");

            // Strip existing timestamp if present
            var match = SlideTimestampRegex.Match(block);
            if (match.Success)
                block = block[match.Length..].TrimStart();

            // Add new timestamp
            if (tsIdx < timestampsMs.Count && timestampsMs[tsIdx] >= 0)
            {
                string timeStr = MsToTimecode(timestampsMs[tsIdx]);
                result.Append($"{{{timeStr}}}");
            }

            result.Append(block);
            tsIdx++;
        }

        return result.ToString();
    }

    /// <summary>
    /// Parse a timecode string (M:SS.mmm or M:SS:mmm) to milliseconds.
    /// </summary>
    public double ParseTimecodeToMs(string timecode)
    {
        if (string.IsNullOrWhiteSpace(timecode)) return -1;
        timecode = timecode.Trim();

        // Replace : with . for uniform parsing (handles M:SS:mmm and M:SS.mmm)
        var parts = timecode.Replace('.', ':').Split(':');

        if (parts.Length == 3)
        {
            if (int.TryParse(parts[0], out int min) &&
                int.TryParse(parts[1], out int sec) &&
                int.TryParse(parts[2], out int ms))
            {
                // Handle case where ms part is 1-3 digits
                if (parts[2].Length == 1) ms *= 100;
                else if (parts[2].Length == 2) ms *= 10;
                return min * 60000.0 + sec * 1000.0 + ms;
            }
        }
        else if (parts.Length == 2)
        {
            if (int.TryParse(parts[0], out int min) &&
                int.TryParse(parts[1], out int sec))
            {
                return min * 60000.0 + sec * 1000.0;
            }
        }

        return -1;
    }

    private static string MsToTimecode(double ms)
    {
        int totalMs = (int)Math.Round(ms);
        int minutes = totalMs / 60000;
        int seconds = (totalMs % 60000) / 1000;
        int millis = totalMs % 1000;
        return $"{minutes}:{seconds:D2}.{millis:D3}";
    }

    /// <summary>
    /// Extract all chord symbols from inline text with their character positions
    /// (position in the stripped-lyrics text, for display alignment).
    /// </summary>
    public List<(int charOffset, string chord)> ExtractChordPositions(string line)
    {
        var positions = new List<(int charOffset, string chord)>();
        int strippedOffset = 0;

        var matches = InlineChordRegex.Matches(line);
        int lastEnd = 0;

        foreach (Match match in matches)
        {
            // Count non-chord characters before this match
            strippedOffset += match.Index - lastEnd;
            positions.Add((strippedOffset, match.Groups[1].Value));
            lastEnd = match.Index + match.Length;
        }

        return positions;
    }

    /// <summary>
    /// Convert inline chord notation to Nashville number system based on the given key.
    /// Example: In key of C, [C] -> [1], [Am] -> [6m], [G7] -> [5-7], [F] -> [4]
    /// </summary>
    public string ConvertToNashvilleNumbers(string text, string key)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(key)) return text;

        return InlineChordRegex.Replace(text, match =>
        {
            string chord = match.Groups[1].Value;
            if (chord == "N.C." || chord == "NC" || chord == "N/C") return match.Value;

            string nashville = ChordToNashville(chord, key);
            return $"[{nashville}]";
        });
    }

    /// <summary>
    /// Convert a single chord symbol to its Nashville number representation.
    /// </summary>
    private string ChordToNashville(string chord, string key)
    {
        var noteOrder = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        var flatToSharp = new Dictionary<string, string>
        {
            {"Db", "C#"}, {"Eb", "D#"}, {"Fb", "E"}, {"Gb", "F#"}, {"Ab", "G#"}, {"Bb", "A#"}, {"Cb", "B"}
        };

        // Handle slash chords (e.g., G/B -> 5/7 in key of C)
        int slashIdx = chord.IndexOf('/');
        string bassNote = "";
        string mainChord = chord;

        if (slashIdx > 0)
        {
            bassNote = chord[(slashIdx + 1)..];
            mainChord = chord[..slashIdx];
        }

        // Extract root from main chord
        string root = ExtractRoot(mainChord);
        if (root == "") return chord; // Can't parse, return as-is

        string suffix = mainChord[root.Length..];

        // Get scale degree of root relative to key
        int keyIndex = GetNoteIndex(key, noteOrder, flatToSharp);
        int rootIndex = GetNoteIndex(root, noteOrder, flatToSharp);
        if (keyIndex < 0 || rootIndex < 0) return chord;

        int degree = ((rootIndex - keyIndex) % 12 + 12) % 12;
        string nashvilleNumber = DegreeToNashville(degree);

        // Convert quality suffix to Nashville suffix
        string nashvilleSuffix = ConvertSuffixToNashville(suffix);

        // Handle bass note
        string result = nashvilleNumber + nashvilleSuffix;
        if (!string.IsNullOrEmpty(bassNote))
        {
            int bassIndex = GetNoteIndex(bassNote, noteOrder, flatToSharp);
            if (bassIndex >= 0)
            {
                int bassDegree = ((bassIndex - keyIndex) % 12 + 12) % 12;
                result += "/" + DegreeToNashville(bassDegree);
            }
        }

        return result;
    }

    /// <summary>
    /// Get the index of a note in the chromatic scale (0-11).
    /// Returns -1 if not found.
    /// </summary>
    private int GetNoteIndex(string note, string[] noteOrder, Dictionary<string, string> flatToSharp)
    {
        // Normalize note - extract just the note letter + accidental
        string noteRoot = ExtractRoot(note);
        if (string.IsNullOrEmpty(noteRoot)) noteRoot = note;

        if (flatToSharp.TryGetValue(noteRoot, out string? normalized))
            noteRoot = normalized;

        return Array.IndexOf(noteOrder, noteRoot);
    }

    /// <summary>
    /// Convert a chromatic degree (0-11) to its Nashville number string.
    /// </summary>
    private string DegreeToNashville(int degree)
    {
        return degree switch
        {
            0 => "1",
            1 => "b2",
            2 => "2",
            3 => "b3",
            4 => "3",
            5 => "4",
            6 => "b5",
            7 => "5",
            8 => "b6",
            9 => "6",
            10 => "b7",
            11 => "7",
            _ => degree.ToString()
        };
    }

    /// <summary>
    /// Convert a chord quality suffix to Nashville notation.
    /// For Nashville, we use a dash separator before extensions.
    /// m -> m, 7 -> -7, maj7 -> -maj7, etc.
    /// </summary>
    private string ConvertSuffixToNashville(string suffix)
    {
        if (string.IsNullOrEmpty(suffix)) return "";

        // Minor stays as 'm'
        if (suffix == "m") return "m";
        if (suffix == "min") return "m";

        // Minor + extension
        if (suffix.StartsWith("m") && suffix.Length > 1 && suffix != "maj" && !suffix.StartsWith("maj"))
        {
            string ext = suffix[1..];
            return "m" + (ext.Length > 0 ? "-" + ext : "");
        }

        // Major extensions and other qualities - use dash separator
        return "-" + suffix;
    }

    /// <summary>
    /// Transpose chords DOWN by capoFret semitones to show what shapes to play with a capo.
    /// Example: CapoTranspose("[G]text", 2) returns "[F]text" - with capo on 2, play F shape to sound G.
    /// </summary>
    public string CapoTranspose(string text, int capoFret)
    {
        if (capoFret == 0 || string.IsNullOrEmpty(text)) return text;

        // Transpose down by capoFret semitones
        return Transpose(text, -capoFret);
    }

    private string TrimTrailingEmptyLines(string text)
    {
        return text.TrimEnd('\n', '\r', ' ');
    }

    /// <summary>
    /// Build a chord-over-lyric layout from inline [chord] text.
    /// This is the single shared source of truth for the two-line layout used by the
    /// WPF STAGE preview, the congregation output, and (optionally) ChordChartWindow.
    ///
    /// Mirrors ChordChartWindow.BuildFromInlineChords/BuildChordLine EXACTLY:
    /// - split on '\n'
    /// - skip blank/whitespace lines (same as the reference's `continue`)
    /// - per line call ExtractChordPositions + StripChords
    /// - fill a char[Math.Max(lyric.Length + 10, 80)] with spaces, write each chord at
    ///   Math.Clamp(offset, 0, len - chord.Length - 1), then TrimEnd()
    /// - carry the last chord onto chordless lines as a dim "(chord)" (CarriedOver = true)
    ///
    /// Returns strings only (no WPF types) so it is unit-testable and shareable.
    /// </summary>
    public List<ChordLyricLine> BuildChordOverLyric(string text)
    {
        var result = new List<ChordLyricLine>();
        if (string.IsNullOrEmpty(text)) return result;

        var lines = text.Split('\n');
        string lastChord = ""; // Carry over chord to lines without a new chord

        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) continue; // Skip empty lines (matches reference)

            var chordPositions = ExtractChordPositions(rawLine);
            string lyricText = StripChords(rawLine);

            string chordLine;
            bool carriedOver = false;

            if (chordPositions.Count > 0)
            {
                int lineLength = Math.Max(lyricText.Length + 10, 80);
                char[] chordChars = new char[lineLength];
                Array.Fill(chordChars, ' ');

                foreach (var (charOffset, chord) in chordPositions)
                {
                    int offset = Math.Clamp(charOffset, 0, lineLength - chord.Length - 1);
                    for (int i = 0; i < chord.Length && offset + i < lineLength; i++)
                    {
                        chordChars[offset + i] = chord[i];
                    }
                    lastChord = chord; // Track last chord for carry-over
                }

                chordLine = new string(chordChars).TrimEnd();
            }
            else if (!string.IsNullOrEmpty(lastChord))
            {
                // No chord on this line — show carried-over chord (dim)
                chordLine = $"({lastChord})";
                carriedOver = true;
            }
            else
            {
                chordLine = "";
            }

            result.Add(new ChordLyricLine
            {
                ChordLine = chordLine,
                LyricLine = lyricText,
                CarriedOver = carriedOver
            });
        }

        return result;
    }
}

/// <summary>
/// A single rendered chord-over-lyric line: the monospace chord row (<see cref="ChordLine"/>)
/// sits above the lyric row (<see cref="LyricLine"/>). <see cref="CarriedOver"/> is true when
/// the chord row is a dim "(chord)" carried over from a previous line (no new chord here).
/// </summary>
public class ChordLyricLine
{
    public string ChordLine { get; set; } = "";
    public string LyricLine { get; set; } = "";
    public bool CarriedOver { get; set; }
}
